using HarmonyLib;
using ProtoBuf;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EternalStorm.Behaviors;

public class BehaviorClassReset : CollectibleBehavior
{
    public BehaviorClassReset(CollectibleObject collObj) : base(collObj) { }

    public static string ChannelName = "eternalstorm.changeclass";

    internal ICoreClientAPI capi;
    internal ICoreServerAPI sapi;

    internal CharacterSystem characterSystem;
    internal Type customCharacterType;

    internal SimpleParticleProperties useParticle;

    public override void OnLoaded(ICoreAPI api)
    {
        capi = api as ICoreClientAPI;
        sapi = api as ICoreServerAPI;
        characterSystem = api.ModLoader.GetModSystem<CharacterSystem>();
        customCharacterType = AccessTools.TypeByName("PlayerModelLib.GuiDialogCreateCustomCharacter");

        if (api.Side == EnumAppSide.Client)
        {
            var _clientChannel = api.Network.GetChannel(ChannelName) as IClientNetworkChannel;
            _clientChannel.SetMessageHandler<ClassResetPacket>(OnClassResetPacketFromServer);

            // setup particle
            useParticle = new SimpleParticleProperties(1f, 1f, ColorUtil.ToRgba(50, 220, 220, 220), new Vec3d(), new Vec3d(), new Vec3f(-0.1f, -0.1f, -0.1f), new Vec3f(0.1f, 0.1f, 0.1f), 1.5f, 0f, 0.5f, 0.75f);
            useParticle.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -0.6f);
            useParticle.AddPos.Set(0.1, 0.1, 0.1);
            useParticle.addLifeLength = 0.5f;
            useParticle.RandomVelocityChange = true;
            useParticle.MinQuantity = 200f;
            useParticle.AddQuantity = 50f;
            useParticle.MinSize = 0.2f;
            useParticle.ParticleModel = EnumParticleModel.Quad;
            useParticle.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -150f);
        }
    }

    public void OnClassResetPacketFromServer(ClassResetPacket packet)
    {
        if (customCharacterType != null)
        {
            // Try to create an instance of the custom dialog
            var ctor = customCharacterType.GetConstructor(new Type[] { typeof(ICoreClientAPI), characterSystem.GetType() });
            if (ctor != null)
            {
                var customDlg = ctor.Invoke(new object[] { capi, characterSystem });
                var prepAndOpen = customCharacterType.GetMethod("PrepAndOpen");
                prepAndOpen?.Invoke(customDlg, null);
                return;
            }
        }
        
        // Fallback to default dialog
        var createCharDlg = new GuiDialogCreateCharacter(capi, characterSystem);
        createCharDlg.PrepAndOpen();
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        handHandling = EnumHandHandling.Handled;
    }

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
    {
        handling = EnumHandling.Handled;

        if (sapi != null)
        {
            EntityPlayer ePlayer = byEntity as EntityPlayer;
            IServerPlayer player = ePlayer.Player as IServerPlayer;
            sapi.InjectConsole("/player " + player.PlayerName + " allowcharselonce");

            var serverChannel = sapi.Network.GetChannel(ChannelName);
            serverChannel.SendPacket(new ClassResetPacket(), player);

            slot?.TakeOut(1);
            slot?.MarkDirty();
        }

        if (capi != null)
        {
            // play sound
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/coin6"), byEntity, (byEntity as EntityPlayer)?.Player, false, 12f, 1f);
            // spawn particle
            useParticle.MinPos = byEntity.SidedPos.XYZ.Add(byEntity.SelectionBox.X1, 0.0, byEntity.SelectionBox.Z1);
            useParticle.AddPos = new Vec3d(byEntity.SelectionBox.XSize, byEntity.SelectionBox.Y2, byEntity.SelectionBox.ZSize);
            useParticle.Color = ColorUtil.ReverseColorBytes(ColorUtil.HsvToRgba(110 + capi.World.Rand.Next(15), 180, 100 + capi.World.Rand.Next(50), 150));
            capi.World.SpawnParticles(useParticle);
        }
    }
}

[ProtoContract]
public class ClassResetPacket {}
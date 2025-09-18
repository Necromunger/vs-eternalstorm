using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
    internal INetworkChannel channel;

    public override void OnLoaded(ICoreAPI api)
    {
        capi = api as ICoreClientAPI;
        sapi = api as ICoreServerAPI;
        characterSystem = api.ModLoader.GetModSystem<CharacterSystem>();

        if (api.Side == EnumAppSide.Client)
        {
            var _clientChannel = api.Network.GetChannel(ChannelName) as IClientNetworkChannel;
            _clientChannel.SetMessageHandler<ClassResetPacket>(OnClassResetPacketFromServer);
        }
    }

    public void OnClassResetPacketFromServer(ClassResetPacket packet)
    {
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
    }
}

[ProtoContract]
public class ClassResetPacket {}
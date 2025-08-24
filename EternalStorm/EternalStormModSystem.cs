using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace EternalStorm;

public class EternalStormModSystem : ModSystem
{
    public static string ConfigName = "EternalStormConfig.json";

    private ICoreAPI api;
    private static ICoreServerAPI sapi;

    internal EternalStormModConfig config;
    internal static EternalStormModSystem instance;

    // Properties to access config values
    internal static double StabilityPerGear => instance?.config?.StabilityPerGear ?? 0.30;
    internal static float DamageOnGearUse => instance?.config?.DamageOnGearUse ?? 2f;
    internal static float LowStabilityDamage => instance?.config?.LowStabilityDamage ?? 2f;

    private Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        instance = this;

        this.api = api;

        config = api.LoadModConfig<EternalStormModConfig>(ConfigName) ?? EternalStormModConfig.GetDefault(api);

        api.RegisterCollectibleBehaviorClass("BehaviorNamedSkull", typeof(BehaviorNamedSkull));

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();

        api.World.RegisterCallback(_ =>
        {
            var sys = api.ModLoader.GetModSystem<SystemTemporalStability>();
            sys.OnGetTemporalStability -= BorderStability;
            sys.OnGetTemporalStability += BorderStability;
        }, 0);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        AddStabilityCommand();
        PatchPlayerCorpse(api);
    }

    private void AddStabilityCommand()
    {
        api.ChatCommands
            .GetOrCreate("player")
            .BeginSubCommands("sanity")
            .WithDescription("Updates player sanity")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(api.ChatCommands.Parsers.OnlinePlayer("target"))
            .WithArgs(api.ChatCommands.Parsers.Double("amount"))
            .HandleWith(args =>
            {
                var playerArg = args.Parsers[0] as PlayersArgParser;
                var players = args.Parsers[0].GetValue() as PlayerUidName[];
                if (players.Length == 0)
                    return TextCommandResult.Error("Target player not found or not online.");

                var playerUidName = players[0];

                var player = api.World.PlayerByUid(playerUidName.Uid);
                if (player == null)
                    return TextCommandResult.Error("Target player not found or not online.");

                var amount = (double)args.Parsers[1].GetValue();

                var be = player.Entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
                if (be == null) return TextCommandResult.Error("Player has no temporal-stability behavior.");

                be.OwnStability = GameMath.Clamp(amount, 0.0, 1.0);
                player.Entity.WatchedAttributes.SetDouble("temporalStability", be.OwnStability);
                player.Entity.WatchedAttributes.MarkAllDirty();

                return TextCommandResult.Success($"Set {player.PlayerName}'s stability to {be.OwnStability:P0}.");
            });
    }

    private float BorderStability(float baseStab, double x, double y, double z)
    {
        double dx = x - api.World.DefaultSpawnPosition.X;
        double dz = z - api.World.DefaultSpawnPosition.Z;
        double dist = Math.Sqrt(dx * dx + dz * dz);

        if (dist <= config.BorderStart) return baseStab;
        if (dist >= config.BorderEnd) return 0f;

        float t = (float)((dist - config.BorderStart) / (config.BorderEnd - config.BorderStart));

        return GameMath.Lerp(baseStab, 0f, t);
    }

    #region Patch Add skull to PlayerCorpse
    /// <summary>
    /// Patches PlayerCorpse.Systems.DeathContentManager.TakeContentFromPlayer(IServerPlayer)
    /// Append a skull to the corpse inventory.
    /// </summary>
    private void PatchPlayerCorpse(ICoreServerAPI api)
    {
        var targetType = AccessTools.TypeByName("PlayerCorpse.Systems.DeathContentManager");
        if (targetType == null)
        {
            api.Logger.Warning("[EternalStorm] PlayerCorpse DeathContentManager not found. Skipping patch.");
            return;
        }

        // private InventoryGeneric TakeContentFromPlayer(IServerPlayer byPlayer)
        var targetMethod = AccessTools.Method(targetType, "TakeContentFromPlayer", new[] { typeof(IServerPlayer) });
        if (targetMethod == null)
        {
            api.Logger.Warning("[EternalStorm] TakeContentFromPlayer(IServerPlayer) not found. Skipping patch.");
            return;
        }

        var postfix = new HarmonyMethod(typeof(EternalStormModSystem).GetMethod(
            nameof(Post_TakeContentFromPlayer),
            BindingFlags.Static | BindingFlags.NonPublic));

        harmony.Patch(targetMethod, postfix: postfix);

        api.Logger.Notification("[EternalStorm] Patched PlayerCorpse.TakeContentFromPlayer (postfix).");
    }

    private static void Post_TakeContentFromPlayer(ref InventoryGeneric __result, IServerPlayer byPlayer)
    {
        if (sapi == null || __result == null || byPlayer == null) return;

        var skullItem = sapi.World.GetItem(new AssetLocation("game", "clutter-skull/humanoid"));
        if (skullItem == null) return;

        var newInv = new InventoryGeneric(__result.Count + 1, $"playercorpse-{byPlayer.PlayerUID}", sapi);

        // Clone original inventory
        for (int i = 0; i < __result.Count; i++)
            newInv[i].Itemstack = __result[i].Itemstack;

        var skull = new ItemStack(skullItem, 1);
        skull.Attributes.SetString("playerName", byPlayer.PlayerName);
        skull.Attributes.SetString("playerUid", byPlayer.PlayerUID);

        // Put skull into last slot
        newInv[newInv.Count - 1].Itemstack = skull;

        // Swap the method result
        __result = newInv;
    }

    #endregion

    #region Patch Temporal Gear Use Damage
    [HarmonyPatch(typeof(ItemKnife)), HarmonyPatch("OnHeldInteractStep")]
    class Patch_ItemKnife_Transpile
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
        {
            var code = new List<CodeInstruction>(ins);

            var addStab = AccessTools.Method(typeof(EntityBehaviorTemporalStabilityAffected), "AddStability", new[] { typeof(double) });
            var recvDmg = AccessTools.Method(typeof(EntityAgent), "ReceiveDamage", new[] { typeof(DamageSource), typeof(float) });
            var getStab = AccessTools.PropertyGetter(typeof(EternalStormModSystem), nameof(StabilityPerGear));
            var getDmg = AccessTools.PropertyGetter(typeof(EternalStormModSystem), nameof(DamageOnGearUse));

            for (int i = 0; i < code.Count - 1; i++)
            {
                // Replace constant before AddStability
                if (code[i + 1].Calls(addStab))
                    code[i] = new CodeInstruction(OpCodes.Call, getStab);

                // Replace constant before ReceiveDamage
                if (code[i + 1].Calls(recvDmg))
                    code[i] = new CodeInstruction(OpCodes.Call, getDmg);
            }

            return code;
        }
    }
    #endregion

    #region Patch Stability Damage

    public static bool ReceiveDamageShim(Entity ent, DamageSource src, float _)
    {
        return ent.ReceiveDamage(src, LowStabilityDamage);
    }

    [HarmonyPatch(typeof(EntityBehaviorTemporalStabilityAffected), "OnGameTick")]
    static class Patch_LowStab_DamageSwap
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
        {
            var code = new List<CodeInstruction>(ins);
            var recv = AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage), new[] { typeof(DamageSource), typeof(float) });
            var shim = AccessTools.Method(typeof(EternalStormModSystem), nameof(ReceiveDamageShim));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(recv))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = shim;
                    break;
                }
            }
            return code;
        }
    }

    #endregion

    #region Patch Border Ruins

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerate")]
    static class Patch_TryGenerate
    {
        public static bool Prefix(
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            int climateUpLeft, int climateUpRight, int climateBotLeft, int climateBotRight,
            string locationCode,
            ref bool __result
        )
        {
            var inside = IsOutsideBorderStart(startPos);
            __result = inside;
            return inside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateRuinAtSurface")]
    class Patch_TryGenerateRuinAtSurface
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            string locationCode,
            ref bool __result
        )
        {
            var inside = IsOutsideBorderStart(startPos);
            __result = inside;
            return inside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateAtSurface")]
    class Patch_TryGenerateAtSurface
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            string locationCode,
            ref bool __result
        )
        {
            var inside = IsOutsideBorderStart(startPos);
            __result = inside;
            return inside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateUnderground")]
    class Patch_TryGenerateUnderground
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos pos,
            string locationCode,
            ref bool __result
        )
        {
            var inside = IsOutsideBorderStart(pos);
            __result = inside;
            return inside;
        }
    }
    public static bool IsOutsideBorderStart(BlockPos pos)
    {
        if (instance.api.World.DefaultSpawnPosition == null)
            return false;

        // Block all structures with name ruin within border 
        double dx = pos.X - instance.api.World.DefaultSpawnPosition.X;
        double dz = pos.Z - instance.api.World.DefaultSpawnPosition.Z;
        double dist = Math.Sqrt(dx * dx + dz * dz);

        if (dist > instance.config.BorderStart)
            return true;

        return false;
    }

    #endregion

    public override void Dispose()
    {
        harmony?.UnpatchAll(Mod.Info.ModID);
    }
}

public class EternalStormModConfig
{
    public float LowStabilityDamage = 0f;
    public double StabilityPerGear = 1.0f;
    public float DamageOnGearUse = 0f;
    public int BorderStart = 30;
    public int BorderEnd = 50;

    public static EternalStormModConfig GetDefault(ICoreAPI api)
    {
        var cfg = new EternalStormModConfig();
        api.StoreModConfig(cfg, EternalStormModSystem.ConfigName);
        return cfg;
    }
}
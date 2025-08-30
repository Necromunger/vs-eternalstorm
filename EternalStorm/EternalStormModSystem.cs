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

    internal static double StabilityPerGear => instance?.config?.StabilityPerGear ?? 0.30;
    internal static float DamageOnGearUse => instance?.config?.DamageOnGearUse ?? 2f;
    internal static float LowStabilityDamage => instance?.config?.LowStabilityDamage ?? 2f;

    private Harmony harmony;

    private ModSystemRifts riftSys;

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

        // prevent rifts inside BorderStart (safe radius from spawn)
        riftSys = sapi.ModLoader.GetModSystem<ModSystemRifts>();
        if (riftSys != null)
            riftSys.OnTrySpawnRift += OnTrySpawnRift_BlockInsideBorder;

        sapi.Event.RegisterGameTickListener(OnServerGameTick, 1000);

        sapi.Event.PlayerDeath += OnPlayerDeath;
    }

    private void OnTrySpawnRift_BlockInsideBorder(BlockPos pos, ref EnumHandling handling)
    {
        var spawn = api.World.DefaultSpawnPosition;
        if (spawn == null) return;

        double dx = pos.X - spawn.X;
        double dz = pos.Z - spawn.Z;
        double distSq = dx * dx + dz * dz;
        double start = config.BorderStart;
        double startSq = start * start;

        if (distSq <= startSq)
            handling = EnumHandling.PreventDefault;
    }

    private void OnPlayerDeath(IServerPlayer deadPlayer, DamageSource dmg)
    {
        if (sapi == null || deadPlayer == null) return;

        // Grab the vanilla humanoid skull item
        var skullItem = sapi.World.GetItem(new AssetLocation("game", "clutter-skull/humanoid"));
        if (skullItem == null) return;

        // Make a skull stack tagged with the player identity
        var skull = new ItemStack(skullItem, 1);
        skull.Attributes.SetString("playerName", deadPlayer.PlayerName);
        skull.Attributes.SetString("playerUid", deadPlayer.PlayerUID);

        // World drop at block-center where the player died
        BlockPos bpos = deadPlayer.Entity.ServerPos.AsBlockPos;
        var dropPos = new Vec3d(bpos.X + 0.5, bpos.Y + 0.5, bpos.Z + 0.5);

        // No throw velocity; just place it gently
        sapi.World.SpawnItemEntity(skull, dropPos, new Vec3d(0, 0, 0));
    }

    private void OnServerGameTick(float dt)
    {
        if (instance?.api == null || instance.config == null) return;
        var spawn = instance.api.World.DefaultSpawnPosition;
        if (spawn == null) return;

        double borderStart = instance.config.BorderStart;
        double borderEnd = instance.config.BorderEnd;
        if (borderEnd <= borderStart) borderEnd = borderStart + 1; // guard bad config
        double invSpan = 1.0 / (borderEnd - borderStart);
        double startRadiusSq = borderStart * borderStart;
        double endRadiusSq = borderEnd * borderEnd;
        double maxDrainPerSec = instance.config.BorderSanityPerSecond;

        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival) continue;

            var entity = player?.Entity;
            if (entity == null) continue;

            var stab = entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
            if (stab == null || stab.OwnStability <= 0.0) continue;

            double dx = entity.Pos.X - spawn.X;
            double dz = entity.Pos.Z - spawn.Z;
            double distanceSquared = dx * dx + dz * dz;

            if (distanceSquared <= startRadiusSq) continue; // inside safe zone (no drain)

            double factor;
            if (distanceSquared >= endRadiusSq)
            {
                factor = 1.0; // full drain beyond end radius
            }
            else
            {
                double distance = Math.Sqrt(distanceSquared); // drain partial in band
                factor = (distance - borderStart) * invSpan;
                if (factor <= 0) continue;
                if (factor >= 1) factor = 1.0;
            }

            double reduction = maxDrainPerSec * factor * dt;
            if (reduction <= 0) continue;

            double newStability = stab.OwnStability - reduction;
            if (newStability < 0.0) newStability = 0.0;

            // write only when there is something to write
            stab.OwnStability = newStability;
            entity.WatchedAttributes.SetDouble("temporalStability", newStability);
            entity.WatchedAttributes.MarkPathDirty("temporalStability");
        }
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

    #region Patch Temporal Gear Use Damage

    [HarmonyPatch(typeof(ItemKnife)), HarmonyPatch("OnHeldInteractStep")]
    class Patch_ItemKnife_Transpile
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
        {
            var code = new List<CodeInstruction>(ins);

            var addStab = AccessTools.Method(typeof(EntityBehaviorTemporalStabilityAffected), "AddStability", new[] { typeof(double) });
            var recvDmg = AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage), new[] { typeof(DamageSource), typeof(float) });
            var getStab = AccessTools.PropertyGetter(typeof(EternalStormModSystem), nameof(StabilityPerGear));
            var getDmg = AccessTools.PropertyGetter(typeof(EternalStormModSystem), nameof(DamageOnGearUse));

            for (int i = 0; i < code.Count - 1; i++)
            {
                // Replace constant before AddStability(...)
                if (code[i + 1].Calls(addStab))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = getStab;
                }

                // Replace constant before ReceiveDamage(...)
                if (code[i + 1].Calls(recvDmg))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = getDmg;
                }
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
        sapi.Event.PlayerDeath -= OnPlayerDeath;

        if (riftSys != null) riftSys.OnTrySpawnRift -= OnTrySpawnRift_BlockInsideBorder;

        harmony?.UnpatchAll(Mod.Info.ModID);
    }
}

public class EternalStormModConfig
{
    public float LowStabilityDamage = 0f;
    public double StabilityPerGear = 1.0f;
    public float DamageOnGearUse = 0f;
    public int BorderStart = 2000;
    public int BorderEnd = 3000;
    // How much temporal stability (sanity) is reduced per second at full intensity (t == 1.0)
    public double BorderSanityPerSecond = 0.005;

    public static EternalStormModConfig GetDefault(ICoreAPI api)
    {
        var cfg = new EternalStormModConfig();
        api.StoreModConfig(cfg, EternalStormModSystem.ConfigName);
        return cfg;
    }
}
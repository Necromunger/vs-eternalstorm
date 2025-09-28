using HarmonyLib;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using EternalStorm.Behaviors;

namespace EternalStorm;

public class EternalStormModSystem : ModSystem
{
    public static EternalStormModSystem Instance;
    public static string ConfigName = "EternalStormConfig.json";

    public ICoreAPI api;
    public  ICoreServerAPI sapi;
    private Harmony harmony;
    private ModSystemRifts riftSystem;
    private StoryStructuresSpawnConditions storySystem;

    private float stormSanityProtection = 0.3f;

    internal EternalStormModConfig config;

    public override void Start(ICoreAPI api)
    {
        // - Init
        Instance = this;
        this.api = api;

        config = api.LoadModConfig<EternalStormModConfig>(ConfigName) ?? EternalStormModConfig.GetDefault(api);
        if (config.BorderEnd <= config.BorderStart)
            config.BorderEnd = config.BorderStart + 1;

        api.RegisterEntityBehaviorClass("playerage", typeof(EntityBehaviorPlayerAge));
        api.RegisterCollectibleBehaviorClass("BehaviorNamedSkull", typeof(BehaviorNamedSkull));

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll(typeof(EternalStormModSystem).Assembly);

        // - Custom micro patches

        // Increase revive timer from EntityBehaviorPlayerRevivable
        api.World.Config.SetDouble("playerRevivableHourAmount", 2.0); // hours

        // Hook to temporal stability to apply border effects
        api.World.RegisterCallback(_ =>
        {
            var sysStability = api.ModLoader.GetModSystem<SystemTemporalStability>();
            sysStability.OnGetTemporalStability -= GetTemporalStability;
            sysStability.OnGetTemporalStability += GetTemporalStability;
        }, 0);
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        storySystem = capi.ModLoader.GetModSystem<StoryStructuresSpawnConditions>();

        var mapManager = capi.ModLoader.GetModSystem<WorldMapManager>();
        mapManager.RegisterMapLayer<StormMapLayer>("Stormwall", 1.0);

        api.Event.OnGetClimate += OnGetClimate;
        api.Event.OnGetWindSpeed += OnGetWindSpeed;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        AddStabilityCommand();
        AddRegenerateCommand();
        AddSetSeedCommand();

        storySystem = sapi.ModLoader.GetModSystem<StoryStructuresSpawnConditions>();

        // Event callbacks
        riftSystem = sapi.ModLoader.GetModSystem<ModSystemRifts>();
        if (riftSystem != null)
            riftSystem.OnTrySpawnRift += OnTrySpawnRift_BlockInsideBorder;

        sapi.Event.PlayerJoin += OnPlayerJoin;
        sapi.Event.PlayerDeath += OnPlayerDeath;

        // Server update loop
        sapi.Event.RegisterGameTickListener(ServerUpdate, 1000);

        api.Event.OnGetClimate += OnGetClimate;
        api.Event.OnGetWindSpeed += OnGetWindSpeed;
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

    private void OnPlayerJoin(IServerPlayer player)
    {
        var hunger = player.Entity?.GetBehavior<EntityBehaviorHunger>();
        if (hunger != null && hunger.MaxSaturation != config.PlayerMaxSaturation)
        {
            hunger.MaxSaturation = config.PlayerMaxSaturation;
            player.Entity?.WatchedAttributes.MarkPathDirty("hunger");
        }
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

        // Spawn skull at body location
        sapi.World.SpawnItemEntity(skull, dropPos, new Vec3d(0, 0, 0));
    }

    private void OnGetClimate(ref ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode = EnumGetClimateMode.NowValues, double totalDays = 0.0)
    {
        if (climate == null) return;

        if (!BlockInSafeZone(pos))
        {
            climate.Rainfall = 1;
            climate.RainCloudOverlay = 1;
            climate.WorldgenRainfall = 1;

            // set cap on temperature in storm to -3c
            if (climate.Temperature > -3f)
                climate.Temperature = -3f;

            if (climate.WorldGenTemperature > -3f)
                climate.WorldGenTemperature = -3f;
        }
    }

    public void OnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
    {
        if (api?.World == null) return;

        if (!BlockInSafeZone(pos.AsBlockPos))
        {
            windSpeed.X = 1 + (api.World.Rand.NextDouble() - 0.5) * 1;
            windSpeed.Z = 0.5 + (api.World.Rand.NextDouble() - 0.5) * 1;
        }
    }

    private void ServerUpdate(float delta)
    {
        if (sapi == null || config == null) return;
        var spawn = sapi.World.DefaultSpawnPosition;
        if (spawn == null) return;

        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival) continue;

            var entity = player?.Entity;
            if (entity == null) continue;

            double dx = entity.Pos.X - spawn.X;
            double dz = entity.Pos.Z - spawn.Z;
            double distanceSq = dx * dx + dz * dz;

            if (EntityInSafeZone(entity.Pos)) continue; // inside safe zone

            // Handle all storm effects
            HandleStabilityLoss(delta, player, distanceSq);
            HandleRiftDamage(delta, player);
        }
    }

    private void HandleStabilityLoss(float delta, IPlayer player, double distanceSq)
    {
        var stab = player.Entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stab == null || stab.OwnStability <= 0.0) return;

        double factor = BorderFactor(distanceSq);
        if (factor <= 0.0) return;

        double reduction = Instance.config.BorderSanityPerSecond * factor * delta;
        if (reduction <= 0) return;

        var age = player.Entity.GetBehavior<EntityBehaviorPlayerAge>();
        if (age != null)
        {
            var final = stormSanityProtection * age.BuffMagnitude;
            reduction -= reduction * final;
        }

        double newStability = stab.OwnStability - reduction;
        if (newStability < 0.0) newStability = 0.0;

        // write only when there is something to write
        stab.OwnStability = newStability;
        player.Entity.WatchedAttributes.SetDouble("temporalStability", newStability);
        player.Entity.WatchedAttributes.MarkPathDirty("temporalStability");
    }

    private void HandleRiftDamage(float delta, IPlayer player)
    {
        var entity = player?.Entity;
        if (sapi == null || entity == null || !entity.Alive) return;

        Vec3d pos = player.Entity.Pos.XYZ;
        Rift rift = riftSystem.riftsById.Values.Where(r => r.Size > 0f).Nearest(r => r.Position.SquareDistanceTo(pos));
        if (rift == null) return;

        // ignore if outside rift damage radius
        double dist = entity.Pos.DistanceTo(rift.Position);
        if (dist > config.RiftDamageRadius) return;

        // Linear falloff: 1 at center > 0 at edge
        float factor = GameMath.Clamp(1f - (float)(dist / config.RiftDamageRadius), 0f, 1f);
        float dmg = config.RiftDamagePerSecond * delta * factor;
        if (dmg <= 0f) return;

        entity.ReceiveDamage(
            new DamageSource
            {
                DamageTier = 0,
                Source = EnumDamageSource.Void,
                Type = EnumDamageType.Poison
            }, dmg
        );
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
                player.Entity.WatchedAttributes.MarkPathDirty("temporalStability");

                return TextCommandResult.Success($"Set {player.PlayerName}'s stability to {be.OwnStability:P0}.");
            });
    }

    private void AddRegenerateCommand()
    {
        api.ChatCommands
            .GetOrCreate("regenerate")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args =>
            {
                //PurgeRegions(config.BorderStart);
                return TextCommandResult.Success($"Regenerate");
            });
    }

    private void AddSetSeedCommand()
    {
        api.ChatCommands
            .GetOrCreate("setseed")
            .WithDescription("Updates server seed")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(api.ChatCommands.Parsers.Int("seed"))
            .HandleWith(args =>
            {
                var seed = (int)args.Parsers[0].GetValue();

                SetWorldSeed(sapi, seed);
                return TextCommandResult.Success($"Changed world seed to {seed}.");
            });
    }

    public static void SetWorldSeed(ICoreServerAPI sapi, int newSeed)
    {
        if (sapi?.WorldManager?.SaveGame == null) return;
        sapi.WorldManager.SaveGame.Seed = newSeed;
        sapi.Logger.Notification($"World seed set to {newSeed}");
    }

    BlockPos tmpPos = new BlockPos();
    public float GetTemporalStability(float baseStab, double x, double y, double z)
    {
        double dx = x - api.World.DefaultSpawnPosition.X;
        double dz = z - api.World.DefaultSpawnPosition.Z;
        double distanceSq = dx * dx + dz * dz;

        double start = Instance.config.BorderStart;
        double startSq = start * start;

        // inside safe zone
        if (distanceSq <= startSq && y >= api.World.SeaLevel) return 1.5f;

        // check if at story location
        tmpPos.Set((int)x, (int)y, (int)z);
        if (storySystem != null && storySystem.GetStoryStructureAt(tmpPos) != null)
            return 1f;

        // reduce sanity in zone
        double factor = BorderFactor(distanceSq);
        if (factor <= 0.0) return baseStab;
        if (factor >= 1.0) return 0f;

        return GameMath.Lerp(baseStab, 0f, (float)factor);
    }

    /// <summary>
    /// Returns a 0..1 factor representing how far between BorderStart (0) and BorderEnd (1) the given distance is
    /// </summary>
    public static double BorderFactor(double distanceSq)
    {
        if (Instance == null || Instance.config == null) return 0.0;

        double start = Instance.config.BorderStart;
        double end = Instance.config.BorderEnd;
        double startSq = start * start;
        double endSq = end * end;

        if (distanceSq <= startSq) return 0.0;
        if (distanceSq >= endSq) return 1.0;

        double distance = Math.Sqrt(distanceSq); // only here
        double band = end - start;
        if (band <= 0.0) return 1.0;
        return GameMath.Clamp((distance - start) / band, 0.0, 1.0);
    }

    public static bool EntityInSafeZone(EntityPos pos)
    {
        if (Instance == null || Instance.api == null || Instance.api.World == null || Instance.api.World.DefaultSpawnPosition == null || Instance.config == null)
            return false;

        return pos.InRangeOf(Instance.api.World.DefaultSpawnPosition, Instance.config.BorderStart);
    }

    public static bool BlockInSafeZone(BlockPos pos)
    {
        // Return true when the block is inside the configured safe zone.
        if (Instance == null || Instance.api == null || Instance.api.World == null || Instance.api.World.DefaultSpawnPosition == null || Instance.config == null)
            return false;

        double dx = pos.X - Instance.api.World.DefaultSpawnPosition.X;
        double dz = pos.Z - Instance.api.World.DefaultSpawnPosition.Z;
        double distSq = dx * dx + dz * dz;
        double start = Instance.config.BorderStart;

        return distSq <= start * start;
    }

    public override void Dispose()
    {
        if (sapi != null)
        {
            sapi.Event.PlayerDeath -= OnPlayerDeath;
        }

        if (riftSystem != null)
            riftSystem.OnTrySpawnRift -= OnTrySpawnRift_BlockInsideBorder;

        // Remove temporal stability hook if Start() registered it
        if (api != null)
        {
            var sys = api.ModLoader.GetModSystem<SystemTemporalStability>();
            if (sys != null)
                sys.OnGetTemporalStability -= GetTemporalStability;
        }

        harmony?.UnpatchAll(Mod.Info.ModID);

        // Clear static references
        Instance = null;
        sapi = null;
    }
}

public class EternalStormModConfig
{
    public float PlayerMaxSaturation = 2000f;

    public float LowStabilityDamage = 0f;
    public float LowStabilityHungerCost = 10f;
    public double StabilityPerGearUse = 1f;
    public float DamageOnTemporalGearUse = 0f;

    public float RiftDamageRadius = 4f;
    public float RiftDamagePerSecond = 3f;

    public int BorderStart = 2000;
    public int BorderEnd = 3000;

    /// <summary>
    /// How much temporal stability (sanity) is reduced per second at full intensity (t == 1.0)
    /// </summary>
    public double BorderSanityPerSecond = 0.005;

    public static EternalStormModConfig GetDefault(ICoreAPI api)
    {
        var cfg = new EternalStormModConfig();
        api.StoreModConfig(cfg, EternalStormModSystem.ConfigName);
        return cfg;
    }
}
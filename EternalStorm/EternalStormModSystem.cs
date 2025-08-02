using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EternalStorm;

public class EternalStormModSystem : ModSystem
{
    private ICoreServerAPI sapi;
    private EternalStormModConfig config;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        config = sapi.LoadModConfig<EternalStormModConfig>("EternalStormModConfig.json") ?? EternalStormModConfig.GetDefault(sapi);

        Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("eternalstorm:hello"));

        api.World.RegisterGameTickListener(UpdateServer, 200);
    }

    private void UpdateServer(float delta)
    {
        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            var pos = player.Entity.ServerPos;

            double relX = pos.X - sapi.World.DefaultSpawnPosition.X;
            double relZ = pos.Z - sapi.World.DefaultSpawnPosition.Z;
            double distance = Math.Sqrt(relX * relX + relZ * relZ);

            var beStability = player.Entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
            if (beStability == null) continue;

            if (distance < config.BorderStart)
                continue;

            // Convert seconds to drain per second (1/seconds)
            float minDrainRate = 1f / config.MinStabilityDrainSeconds;
            float maxDrainRate = 1f / config.MaxStabilityDrainSeconds;
            float drain = maxDrainRate * delta;
            if (distance < config.BorderEnd) {
                float intensity = GameMath.Clamp((float)((distance - config.BorderStart) / (config.BorderEnd - config.BorderStart)), 0f, 1f);
                float lerpedDrainRate = GameMath.Lerp(minDrainRate, maxDrainRate, intensity);
                drain = lerpedDrainRate * delta;
            }

            beStability.OwnStability = GameMath.Clamp(beStability.OwnStability - drain, 0.0, 1.0);
        }
    }
}

public class EternalStormModConfig
{
    public int BorderStart = 30;
    public int BorderEnd = 50;
    // Duration in seconds to drain from 1 to 0
    public float MinStabilityDrainSeconds = 300f;
    public float MaxStabilityDrainSeconds = 150f;

    public static EternalStormModConfig GetDefault(ICoreAPI api)
    {
        var cfg = new EternalStormModConfig();
        api.StoreModConfig(cfg, "EternalStormModConfig.json");
        return cfg;
    }
}
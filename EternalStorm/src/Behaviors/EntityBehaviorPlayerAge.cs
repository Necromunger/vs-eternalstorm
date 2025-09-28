using System;
using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EternalStorm.Behaviors;

public class EntityBehaviorPlayerAge : EntityBehavior
{
    public bool Initialized
    {
        get
        {
            return entity.WatchedAttributes.GetBool("age-initialized");
        }
        set
        {
            entity.WatchedAttributes.SetBool("age-initialized", value);
            entity.WatchedAttributes.MarkPathDirty("age-initialized");
        }
    }

    public double BirthHour
    {
        get
        {
            return entity.WatchedAttributes.GetDouble("birth");
        }
        set
        {
            entity.WatchedAttributes.SetDouble("birth", value);
            entity.WatchedAttributes.MarkPathDirty("birth");
        }
    }

    public double Age
    {
        get
        {
            return entity.Api.World.Calendar.ElapsedDays - BirthHour;
        }
    }

    public float BuffMagnitude
    {
        get
        {
            return (float)Age / daysTillMaxBonus;
        }
    }

    public float maxSaturationDefault = 1500;
    public float maxSaturationBonus = 1500;
    public float daysTillMaxBonus = 30;

    private EntityPlayer entityPlayer;
    private EntityBehaviorHunger hunger;

    private long listenerId;

    public EntityBehaviorPlayerAge(Entity entity)
        : base(entity)
    {
        entityPlayer = entity as EntityPlayer;
    }

    public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
    {

        listenerId = entity.World.RegisterGameTickListener(Tick, 6000);
        hunger = entity?.GetBehavior<EntityBehaviorHunger>();
    }

    private void Tick(float dt)
    {
        UpdateStats();
    }

    private void UpdateStats()
    {
        float bonus = GameMath.Clamp(maxSaturationBonus * BuffMagnitude, 0, maxSaturationBonus);
        hunger.MaxSaturation = maxSaturationDefault + bonus;
    }

    public override void OnEntitySpawn()
    {
        if (!Initialized)
            ResetAge();

        UpdateStats();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        entity.World.UnregisterGameTickListener(listenerId);
    }

    public override void OnEntityRevive()
    {
        ResetAge();
    }

    public void ResetAge()
    {
        Initialized = true;
        BirthHour = entity.Api.World.Calendar.ElapsedDays;

        if (hunger != null)
        {
            hunger.Saturation = maxSaturationDefault / 2;
            hunger.MaxSaturation = maxSaturationDefault;
            hunger.SaturationLossDelayFruit = 0f;
            hunger.SaturationLossDelayVegetable = 0f;
            hunger.SaturationLossDelayGrain = 0f;
            hunger.SaturationLossDelayProtein = 0f;
            hunger.SaturationLossDelayDairy = 0f;
            hunger.FruitLevel = 0f;
            hunger.VegetableLevel = 0f;
            hunger.GrainLevel = 0f;
            hunger.ProteinLevel = 0f;
            hunger.DairyLevel = 0f;

            var isPlayer = entityPlayer.Player as IServerPlayer;
            if (isPlayer != null)
            {
                var spawn = isPlayer.GetSpawnPosition(false).AsBlockPos;
                if (!EternalStormModSystem.BlockInSafeZone(spawn))
                    hunger.Saturation = 0;
            }

            hunger.UpdateNutrientHealthBoost();
        }
    }

    public override string PropertyName()
    {
        return "age";
    }
}

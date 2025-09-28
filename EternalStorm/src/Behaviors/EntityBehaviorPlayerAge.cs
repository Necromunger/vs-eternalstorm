using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace EternalStorm.Behaviors;

public class EntityBehaviorPlayerAge : EntityBehavior
{
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

    public double maxSaturationDefault = 1000;
    public double maxSaturationBonus = 2000;

    private EntityPlayer entityPlayer;

    public EntityBehaviorPlayerAge(Entity entity)
        : base(entity)
    {
        entityPlayer = entity as EntityPlayer;
    }

    public override void OnEntitySpawn()
    {
        ResetAge();
    }

    public override void OnEntityRevive()
    {
        ResetAge();
    }

    public override void OnGameTick(float deltaTime)
    {

    }

    public void ResetAge()
    {
        BirthHour = entity.Api.World.Calendar.ElapsedDays;

        var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
        if (hunger != null)
        {
            hunger.Saturation = 750;
            hunger.MaxSaturation = 1500;
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

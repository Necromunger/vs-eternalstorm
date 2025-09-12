using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace EternalStorm.Behaviors;

public class EntityBehaviorTemporalNecklace : EntityBehavior
{
    private double accum;
    private const double CheckIntervalSec = 1;

    public EntityBehaviorTemporalNecklace(Entity entity) : base(entity) { }
    public override string PropertyName() => "EntityBehaviorTemporalNecklace";

    public override void OnGameTick(float dt)
    {
        accum += dt;
        if (accum < CheckIntervalSec) return;
        accum = 0;

        if (!(entity is EntityPlayer eplayer)) return;

        // get inventory
        var player = eplayer.World.PlayerByUid(eplayer.PlayerUID);
        IInventory inv = player.InventoryManager.GetOwnInventory("character");
        if (inv == null) return;

        ItemSlot neckSlot = inv[(int)EnumCharacterDressType.Neck];
        if (neckSlot == null || neckSlot.Empty) return;

        // get actual item from slot
        var itemStack = neckSlot.Itemstack;
        if (itemStack == null) return;

        // check durability over 0
        var durability = itemStack.Collectible.GetRemainingDurability(itemStack);
        if (durability <= 0) return;

        float restore = itemStack.Collectible.Attributes["sanityRestorationAmount"].AsFloat(0);

        // check if stability behavior exists and its low enough
        var stab = entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stab == null || stab.OwnStability + restore > 1) return;

        // restore sanity
        stab.OwnStability = GameMath.Clamp(stab.OwnStability + restore, 0f, 1f);

        // damage item
        itemStack.Collectible?.DamageItem(entity.World, entity, neckSlot, 1);

        // if destroyed remove light
        durability = itemStack.Collectible.GetRemainingDurability(itemStack);
        if (durability <= 0)
        {
            itemStack.Item.LightHsv = new ThreeBytes([0, 0, 0]);
            itemStack.Collectible.LightHsv = new ThreeBytes([0, 0, 0]);
            neckSlot.MarkDirty();
        }
    }
}
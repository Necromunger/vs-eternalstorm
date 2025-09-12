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
    private const double CheckIntervalSec = 0.2; // 200ms

    private static string[] AllowedNeckCodePaths = new string[]
    {
        "clothes-neck-gear-amulet-temporal-sealed-tinbronze"
    };

    public EntityBehaviorTemporalNecklace(Entity entity) : base(entity) { }
    public override string PropertyName() => "EntityBehaviorTemporalNecklace";

    public override void OnGameTick(float dt)
    {
        accum += dt;
        if (accum < CheckIntervalSec) return;
        accum = 0;

        if (!(entity is EntityPlayer eplayer)) return;

        // check if stability behavior exists
        var stab = entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stab == null) return;

        // check if low on stability
        var curr = stab.OwnStability;
        if (curr >= 0.9f) return;

        // get inventory
        var player = eplayer.World.PlayerByUid(eplayer.PlayerUID);
        IInventory inv = player.InventoryManager.GetOwnInventory("character");
        if (inv == null) return;

        ItemSlot neckSlot = inv[(int)EnumCharacterDressType.Neck];
        if (neckSlot == null || neckSlot.Empty) return;

        // get actual item from slot
        var itemStack = neckSlot.Itemstack;
        if (itemStack == null) return;

        // Require the specific neck variant/type, otherwise bail out early
        var code = itemStack.Collectible?.Code;
        if (code == null || !AllowedNeckCodePaths.Contains(code.Path)) return;

        // check durability over 0
        var durability = itemStack.Collectible.GetRemainingDurability(itemStack);
        if (durability <= 0) return;

        // restore sanity
        float restore = itemStack.Collectible.Attributes["sanityRestorationAmount"].AsFloat(0);
        stab.OwnStability = GameMath.Clamp(curr + (restore * dt), 0f, 1f);

        // damage item
        itemStack.Collectible?.DamageItem(entity.World, entity, neckSlot, 1);
    }
}
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EternalStorm.Behaviors;

public class BehaviorTemporalNecklace : CollectibleBehavior
{
    public BehaviorTemporalNecklace(CollectibleObject collObj) : base(collObj) { }

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(slot, dsc, world, withDebugInfo);
        var stack = slot?.Itemstack;
        if (stack == null) return;

        var restoration = collObj.Attributes["sanityRestorationAmount"].AsFloat() * 100;
        dsc.AppendLine($"Sanity per durability: {restoration}%");
    }
}
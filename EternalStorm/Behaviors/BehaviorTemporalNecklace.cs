using System.Text;
using Vintagestory.API.Common;

namespace EternalStorm.Behaviors;

public class BehaviorTemporalNecklace : CollectibleBehavior
{
    public BehaviorTemporalNecklace(CollectibleObject collObj) : base(collObj) { }

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(slot, dsc, world, withDebugInfo);
        var stack = slot?.Itemstack;
        if (stack == null) return;

        var restoration = collObj.Attributes["sanityRestorationAmount"].AsFloat();
        dsc.AppendLine($"Sanity restoration per second: {restoration}");
    }
}
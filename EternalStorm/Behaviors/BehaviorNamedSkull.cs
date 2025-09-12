using System.Text;
using Vintagestory.API.Common;

namespace EternalStorm.Behaviors;

public class BehaviorNamedSkull : CollectibleBehavior
{
    public BehaviorNamedSkull(CollectibleObject collObj) : base(collObj) { }

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(slot, dsc, world, withDebugInfo);
        var stack = slot?.Itemstack;
        if (stack == null) return;

        if (stack.Collectible?.Code?.Domain != "game") return;
        if (stack.Collectible.Code.Path != "clutter-skull/humanoid") return;

        var name = stack.Attributes.GetString("playerName", null);
        if (!string.IsNullOrEmpty(name))
            dsc.AppendLine($"Belonged to {name}");
    }
}
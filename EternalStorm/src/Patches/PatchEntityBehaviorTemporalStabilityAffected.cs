using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace EternalStorm.Patches;

/// <summary>
/// Changes damage amount from low stability
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorTemporalStabilityAffected))]
public class PatchEntityBehaviorTemporalStabilityAffected
{
    internal static float LowStabilityDamage => EternalStormModSystem.Instance?.config?.LowStabilityDamage ?? 0f;
    internal static float LowStabilityHungerCost => EternalStormModSystem.Instance?.config?.LowStabilityHungerCost ?? 0f;

    public static bool ReceiveDamageShim(Entity entity, DamageSource src, float _)
    {
        // drain hunger
        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
        if (hunger != null)
        {
            hunger.ConsumeSaturation(LowStabilityHungerCost);
        }

        // apply damage
        entity.ReceiveDamage(src, LowStabilityDamage);

        return true;
    }

    [HarmonyPatch("OnGameTick", [typeof(float)])]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
    {
        var code = new List<CodeInstruction>(ins);
        var recv = AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage), new Type[] { typeof(DamageSource), typeof(float) });
        var shim = AccessTools.Method(typeof(PatchEntityBehaviorTemporalStabilityAffected),nameof(ReceiveDamageShim), new Type[] { typeof(Entity), typeof(DamageSource), typeof(float) });

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

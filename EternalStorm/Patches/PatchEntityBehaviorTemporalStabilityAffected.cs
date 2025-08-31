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
    internal static float LowStabilityDamage => EternalStormModSystem.instance?.config?.LowStabilityDamage ?? 0f;

    public static bool ReceiveDamageShim(Entity ent, DamageSource src, float _)
    {
        return ent.ReceiveDamage(src, LowStabilityDamage);
    }

    // Patch the exact overload explicitly
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

using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace EternalStorm.Patches;

/// <summary>
/// Changes damage amount from using temporal gear
/// </summary>
[HarmonyPatch(typeof(ItemKnife))]
class PatchItemKnife
{
    public static double StabilityPerGear => EternalStormModSystem.Instance?.config?.StabilityPerGearUse ?? 1f;
    public static float DamageOnGearUse => EternalStormModSystem.Instance?.config?.DamageOnTemporalGearUse ?? 0f;

    [HarmonyPatch(nameof(ItemKnife.OnHeldInteractStep))]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
    {
        var code = new List<CodeInstruction>(ins);

        var addStab = AccessTools.Method(typeof(EntityBehaviorTemporalStabilityAffected), "AddStability", new[] { typeof(double) });
        var recvDmg = AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage), new[] { typeof(DamageSource), typeof(float) });
        var getStab = AccessTools.PropertyGetter(typeof(PatchItemKnife), nameof(StabilityPerGear));
        var getDmg = AccessTools.PropertyGetter(typeof(PatchItemKnife), nameof(DamageOnGearUse));

        for (int i = 0; i < code.Count - 1; i++)
        {
            // Replace constant before AddStability(...)
            if (code[i + 1].Calls(addStab))
            {
                code[i].opcode = OpCodes.Call;
                code[i].operand = getStab;
            }

            // Replace constant before ReceiveDamage(...)
            if (code[i + 1].Calls(recvDmg))
            {
                code[i].opcode = OpCodes.Call;
                code[i].operand = getDmg;
            }
        }

        return code;
    }
}
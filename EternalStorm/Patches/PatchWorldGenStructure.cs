using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace EternalStorm.Patches;

/// <summary>
/// Limits conditions for generating world features
/// </summary>
[HarmonyPatch(typeof(WorldGenStructure))]
static class PatchWorldGenStructure
{
    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerate")]
    static class Patch_TryGenerate
    {
        public static bool Prefix(
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            int climateUpLeft, int climateUpRight, int climateBotLeft, int climateBotRight,
            string locationCode,
            ref bool __result
        )
        {
            var outside = !EternalStormModSystem.BlockInSafeZone(startPos);
            __result = outside;
            return outside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateRuinAtSurface")]
    class Patch_TryGenerateRuinAtSurface
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            string locationCode,
            ref bool __result
        )
        {
            var outside = !EternalStormModSystem.BlockInSafeZone(startPos);
            __result = outside;
            return outside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateAtSurface")]
    class Patch_TryGenerateAtSurface
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos startPos,
            string locationCode,
            ref bool __result
        )
        {
            var outside = !EternalStormModSystem.BlockInSafeZone(startPos);
            __result = outside;
            return outside;
        }
    }

    [HarmonyPatch(typeof(WorldGenStructure), "TryGenerateUnderground")]
    class Patch_TryGenerateUnderground
    {
        static bool Prefix(
            WorldGenStructure __instance,
            IBlockAccessor blockAccessor,
            IWorldAccessor worldForCollectibleResolve,
            BlockPos pos,
            string locationCode,
            ref bool __result
        )
        {
            var outside = !EternalStormModSystem.BlockInSafeZone(pos);
            __result = outside;
            return outside;
        }
    }

}

using System;
using System.Reflection;
using HarmonyLib;

namespace Map3d.Patches
{
    [HarmonyPatch]
    internal static class RadarMapVisRefreshPatch
    {
        private static MethodBase? TargetMethod()
        {
            Type? inner = AccessTools.Inner(typeof(DynamicMap), "RadarMapVis");
            return inner == null ? null : AccessTools.Method(inner, "Refresh");
        }

        [HarmonyPrefix]
        private static bool SkipWhenClothActive()
        {
            return !Map3dController.IsClothMinimapActive;
        }
    }
}

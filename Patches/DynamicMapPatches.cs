using HarmonyLib;

namespace Map3d.Patches
{
    [HarmonyPatch(typeof(DynamicMap))]
    internal static class DynamicMapPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DynamicMap.Minimize))]
        private static void Min() => Map3dController.Find()?.OnMinimized();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DynamicMap.Maximize))]
        private static void Max() => Map3dController.Find()?.OnMaximized();
    }
}

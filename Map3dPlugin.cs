using System.IO;
using BepInEx;
using Map3d.Config;

namespace Map3d
{
    [BepInPlugin(PluginGuid, PluginName, AppVersion.BepInSemVer)]
    public sealed class Map3dPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.at747.map3d";
        public const string PluginName = "Map3d";

        private void Awake()
        {
            Map3dConfig.Bind(Config);
            string? dir = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(dir))
            {
                Logger.LogError("No plugin dir.");
                return;
            }
            Map3dHost.Ensure(Logger);
            Logger.LogInfo($"{PluginName} {AppVersion.DisplayVersion} loaded (tilt-only, no extrusion).");
        }
    }
}

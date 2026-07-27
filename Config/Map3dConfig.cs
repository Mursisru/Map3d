using BepInEx.Configuration;

namespace Map3d.Config
{
    internal static class Map3dConfig
    {
        internal static bool IsBound { get; private set; }
        internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
        internal static ConfigEntry<float> TiltDegrees { get; private set; } = null!;
        internal static ConfigEntry<float> RadiusMeters { get; private set; } = null!;
        internal static ConfigEntry<bool> UseStockZoom { get; private set; } = null!;
        internal static ConfigEntry<float> LookAheadMeters { get; private set; } = null!;
        internal static ConfigEntry<float> ViewHeightScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewBackScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewLookScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewFov { get; private set; } = null!;
        internal static ConfigEntry<int> RenderSize { get; private set; } = null!;
        internal static ConfigEntry<float> IconSizeFraction { get; private set; } = null!;
        internal static ConfigEntry<float> ConeLengthFraction { get; private set; } = null!;
        internal static ConfigEntry<float> ConeLengthScale { get; private set; } = null!;

        internal static void Bind(ConfigFile config)
        {
            if (config == null || IsBound)
                return;

            Enabled = config.Bind("General", "Enabled", true, "Tilt map cloth; unit icons on the same tilted layer. No grid.");
            TiltDegrees = config.Bind("Engine", "TiltDegrees", 55f, "Map cloth tilt (degrees).");
            UseStockZoom = config.Bind("Engine", "UseStockZoom", true, "Derive visible radius from stock minimap scale.");
            RadiusMeters = config.Bind("Engine", "RadiusMeters", 7000f, "Fallback half-size (meters) when UseStockZoom is off.");
            LookAheadMeters = config.Bind("Engine", "LookAheadMeters", 4000f, "Stock CenterMinimizedMap look-ahead.");
            ViewHeightScale = config.Bind("Camera", "ViewHeightScale", 1.28f, "Cloth camera height / Radius.");
            ViewBackScale = config.Bind("Camera", "ViewBackScale", 0.10f, "Cloth camera back / Radius.");
            ViewLookScale = config.Bind("Camera", "ViewLookScale", 0.26f, "Cloth look-ahead target / Radius.");
            ViewFov = config.Bind("Camera", "ViewFov", 42f, "Cloth render FOV.");
            RenderSize = config.Bind("Camera", "RenderSize", 512, "RenderTexture size.");
            IconSizeFraction = config.Bind("Icons", "IconSizeFraction", 0.05f, "Icon world size / Radius (~stock on-screen).");
            ConeLengthFraction = config.Bind("Icons", "ConeLengthFraction", 0.30f, "Legacy unused; use ConeLengthScale.");
            ConeLengthScale = config.Bind("Icons", "ConeLengthScale", 1f, "Multiplier on stock viewIndicator meters (rect/displayFactor).");
            IsBound = true;
        }

        internal static bool IsEnabled => IsBound && Enabled.Value;
    }
}

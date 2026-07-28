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
        internal static ConfigEntry<float> HorizonFarScale { get; private set; } = null!;
        internal static ConfigEntry<float> HorizonNearScale { get; private set; } = null!;
        internal static ConfigEntry<float> HorizonSideScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewHeightScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewBackScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewLookScale { get; private set; } = null!;
        internal static ConfigEntry<float> ViewFov { get; private set; } = null!;
        internal static ConfigEntry<int> RenderSize { get; private set; } = null!;
        internal static ConfigEntry<int> RenderMsaa { get; private set; } = null!;
        internal static ConfigEntry<float> MapMipBias { get; private set; } = null!;
        internal static ConfigEntry<float> MapBrightness { get; private set; } = null!;
        internal static ConfigEntry<float> IconSizeFraction { get; private set; } = null!;
        internal static ConfigEntry<float> ConeLengthFraction { get; private set; } = null!;
        internal static ConfigEntry<float> ConeLengthScale { get; private set; } = null!;
        internal static ConfigEntry<bool> HeightEnabled { get; private set; } = null!;
        internal static ConfigEntry<int> HeightCacheResolution { get; private set; } = null!;
        internal static ConfigEntry<int> HeightBakeSamplesPerFrame { get; private set; } = null!;
        internal static ConfigEntry<int> HeightClothResolution { get; private set; } = null!;
        internal static ConfigEntry<float> HeightVisualFraction { get; private set; } = null!;
        internal static ConfigEntry<float> HeightExaggeration { get; private set; } = null!;
        internal static ConfigEntry<bool> GridEnabled { get; private set; } = null!;
        internal static ConfigEntry<float> GridOpacity { get; private set; } = null!;

        internal static void Bind(ConfigFile config)
        {
            if (config == null || IsBound)
                return;

            Enabled = config.Bind("General", "Enabled", true, "Tilt map cloth; icons on cloth; optional terrain relief.");
            TiltDegrees = config.Bind("Engine", "TiltDegrees", 55f, "Map cloth tilt (degrees).");
            UseStockZoom = config.Bind("Engine", "UseStockZoom", true, "Derive visible radius from stock minimap scale.");
            RadiusMeters = config.Bind("Engine", "RadiusMeters", 7000f, "Fallback half-size (meters) when UseStockZoom is off.");
            LookAheadMeters = config.Bind("Engine", "LookAheadMeters", 4000f, "Stock CenterMinimizedMap look-ahead.");
            HorizonFarScale = config.Bind("Engine", "HorizonFarScale", 4.5f, "Cloth extent ahead of aircraft / Radius (fills past visual horizon).");
            HorizonNearScale = config.Bind("Engine", "HorizonNearScale", 0.85f, "Cloth extent behind aircraft / Radius.");
            HorizonSideScale = config.Bind("Engine", "HorizonSideScale", 1.15f, "Minimum cloth half-width / Radius; cloth also extends to map side borders.");
            ViewHeightScale = config.Bind("Camera", "ViewHeightScale", 1.28f, "Cloth camera height / Radius.");
            ViewBackScale = config.Bind("Camera", "ViewBackScale", 0.10f, "Cloth camera back / Radius.");
            ViewLookScale = config.Bind("Camera", "ViewLookScale", 0.26f, "Cloth look-ahead target / Radius.");
            ViewFov = config.Bind("Camera", "ViewFov", 42f, "Cloth render FOV.");
            RenderSize = config.Bind("Camera", "RenderSize", 1024, "Cloth RenderTexture size (512-2048). Higher = sharper minimap.");
            RenderMsaa = config.Bind("Camera", "RenderMsaa", 4, "MSAA samples for cloth RT (0/2/4/8). Softens distant edges.");
            MapMipBias = config.Bind("Camera", "MapMipBias", 0.45f, "Mip bias on cloth map albedo (0=sharp, higher=softer far distance).");
            MapBrightness = config.Bind("Camera", "MapBrightness", 1.22f, "Cloth albedo multiplier to match stock flat mapImage brightness.");
            IconSizeFraction = config.Bind("Icons", "IconSizeFraction", 0.05f, "Icon world size / Radius.");
            ConeLengthFraction = config.Bind("Icons", "ConeLengthFraction", 0.30f, "Legacy unused; use ConeLengthScale.");
            ConeLengthScale = config.Bind("Icons", "ConeLengthScale", 1f, "Multiplier on stock viewIndicator meters.");
            HeightEnabled = config.Bind("Height", "Enabled", true, "Displace cloth from cached full-map height bake.");
            HeightCacheResolution = config.Bind("Height", "CacheResolution", 256, "Full-map height cache resolution (N x N).");
            HeightBakeSamplesPerFrame = config.Bind("Height", "BakeSamplesPerFrame", 512, "Raycasts per frame while baking the height cache.");
            HeightClothResolution = config.Bind("Height", "ClothResolution", 64, "Displaced cloth mesh resolution (N x N); scales up with large cloth span.");
            HeightVisualFraction = config.Bind("Height", "VisualFraction", 0.28f, "Relief vertical span as fraction of Radius.");
            HeightExaggeration = config.Bind("Height", "Exaggeration", 1f, "Extra multiplier on auto height scale.");
            GridEnabled = config.Bind("Grid", "Enabled", true, "Stock mapGrid tiles on the 3D cloth (flat, no relief).");
            GridOpacity = config.Bind("Grid", "Opacity", 0.5f, "Stock grid sprite alpha (0-1).");
            IsBound = true;
        }

        internal static bool IsEnabled => IsBound && Enabled.Value;
    }
}

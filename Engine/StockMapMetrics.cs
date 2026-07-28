using Map3d.Config;
using UnityEngine;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock minimap meters: mapScaleMinimized=300, mapScaleMaximized=900, mapDimension≈81920,
    /// Minimize applies mapScaleCenter×2. Visible half ≈ halfUi / (displayFactor × lossyScale).
    /// </summary>
    internal static class StockMapMetrics
    {
        internal static bool TryGetDisplayFactor(DynamicMap map, out float display)
        {
            display = 0f;
            if (map == null)
                return false;

            display = map.mapDisplayFactor;
            if (display < 1e-8f)
            {
                float dim = Mathf.Max(1f, map.mapDimension);
                float max = map.mapScaleMaximized > 1f ? map.mapScaleMaximized : 900f;
                display = max / dim;
            }
            return display > 1e-8f;
        }

        internal static bool TryGetDisplayAndLossy(DynamicMap map, out float display, out float lossy)
        {
            lossy = 1f;
            if (!TryGetDisplayFactor(map, out display))
                return false;

            if (map.mapImage != null)
                lossy = Mathf.Abs(map.mapImage.transform.lossyScale.x);
            if (lossy < 1e-5f)
                lossy = 1f;
            return true;
        }

        internal static float ResolveRadius(DynamicMap map)
        {
            float fallback = Mathf.Max(500f, Map3dConfig.RadiusMeters.Value);
            if (!Map3dConfig.UseStockZoom.Value || map == null)
                return fallback;

            if (!TryGetDisplayAndLossy(map, out float display, out float lossy))
                return fallback;

            float halfUi = map.mapScaleMinimized > 1f ? map.mapScaleMinimized * 0.5f : 150f;
            float radius = halfUi / (display * lossy);
            return Mathf.Clamp(radius, 2500f, 20000f);
        }

        internal static float ResolveIconMeters(float radius, float mapIconSize, float optionIconSize)
        {
            float frac = Mathf.Clamp(Map3dConfig.IconSizeFraction.Value, 0.02f, 0.1f);
            float meters = radius * frac
                           * Mathf.Max(0.5f, mapIconSize)
                           * Mathf.Max(0.75f, optionIconSize);
            return Mathf.Clamp(meters, 100f, 800f);
        }

        internal static float ResolveObjectiveMeters(float radius, float uiPixels)
        {
            float relative = Mathf.Max(0.5f, uiPixels / 20f);
            return ResolveIconMeters(radius, relative, 1f);
        }

        /// <summary>
        /// Perspective cloth cam shrinks distant geometry; scale billboards by distance/ref
        /// so apparent screen size stays near stock flat minimap.
        /// </summary>
        internal static float CompensatePerspectiveIconSize(
            Transform clothPivot,
            Camera? clothCam,
            Vector3 localPos,
            float baseMeters,
            float refCamDist)
        {
            if (clothCam == null || clothPivot == null || refCamDist < 1f)
                return baseMeters;

            Vector3 world = clothPivot.TransformPoint(localPos);
            float dist = Vector3.Distance(clothCam.transform.position, world);
            float ratio = Mathf.Clamp(dist / refCamDist, 1f, 3.5f);
            return baseMeters * ratio;
        }

        internal static float ResolveRefCameraDistance(Camera? clothCam, Transform clothPivot)
        {
            if (clothCam == null || clothPivot == null)
                return 5000f;
            return Mathf.Max(500f, Vector3.Distance(clothCam.transform.position, clothPivot.position));
        }

        /// <summary>
        /// Stock viewIndicator is 100×100 under mapImage (same space as mapDisplayFactor).
        /// Length ≈ rect height / displayFactor (~9 km), not a small fraction of window radius.
        /// </summary>
        internal static float ResolveConeMeters(DynamicMap map, float radius)
        {
            float scale = Mathf.Clamp(Map3dConfig.ConeLengthScale.Value, 0.5f, 2f);
            float fallback = radius * 1.2f * scale;

            if (!TryGetDisplayFactor(map, out float display))
                return fallback;

            float ui = 100f;
            if (map.viewIndicator != null)
            {
                var rt = map.viewIndicator.transform as RectTransform;
                if (rt != null)
                    ui = Mathf.Max(rt.rect.height, rt.rect.width);
            }

            float meters = ui / display;
            return Mathf.Clamp(meters * scale, radius * 0.8f, radius * 2.5f);
        }
    }
}

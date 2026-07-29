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
        /// Stock RadarMapVis width ≈ thin UI stroke. Prefer a fraction of unit icon size —
        /// rect.width/displayFactor can explode into a multi-km "slab".
        /// </summary>
        internal static float ResolveRadarLineWidthMeters(DynamicMap map, UnityEngine.UI.Image? ui, float radius)
        {
            float icon = ResolveIconMeters(radius, 1f, 1f);
            float width = icon * 0.22f;
            if (map != null && TryGetDisplayAndLossy(map, out float display, out float lossy) && ui != null)
            {
                RectTransform rt = ui.rectTransform;
                if (rt != null)
                {
                    float uiW = Mathf.Max(1f, Mathf.Min(rt.rect.width, 12f));
                    float fromStock = uiW / Mathf.Max(1e-8f, display * lossy);
                    width = Mathf.Clamp(fromStock, icon * 0.12f, icon * 0.45f);
                }
            }

            return Mathf.Clamp(width, 20f, 90f);
        }

        /// <summary>
        /// Flat map-plane line between cloth locals. Honors sprite pivot (stock UI lines often bottom-pivot).
        /// </summary>
        internal static void PlaceFlatClothLine(
            Transform lineTx,
            SpriteRenderer sr,
            Sprite sprite,
            Color color,
            Vector3 localFrom,
            Vector3 localTo,
            float widthMeters,
            int sortingOrder)
        {
            if (lineTx == null || sr == null || sprite == null)
                return;

            Vector3 delta = localTo - localFrom;
            delta.y = 0f;
            float len = delta.magnitude;
            if (len < 0.05f)
            {
                if (lineTx.gameObject.activeSelf)
                    lineTx.gameObject.SetActive(false);
                return;
            }

            if (!lineTx.gameObject.activeSelf)
                lineTx.gameObject.SetActive(true);

            Vector3 dir = delta / len;
            float y = Mathf.Max(localFrom.y, localTo.y) + Mathf.Max(4f, widthMeters * 0.1f);

            // SpriteRenderer pivot = sprite.pivot; stock radar Image often bottom-centered.
            float pivotNormY = sprite.rect.height > 0.01f
                ? Mathf.Clamp01(sprite.pivot.y / sprite.rect.height)
                : 0.5f;

            lineTx.localPosition = new Vector3(
                localFrom.x + dir.x * (pivotNormY * len),
                y,
                localFrom.z + dir.z * (pivotNormY * len));

            float ang = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            lineTx.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -ang);

            float bw = Mathf.Max(sprite.bounds.size.x, 0.0001f);
            float bh = Mathf.Max(sprite.bounds.size.y, 0.0001f);
            lineTx.localScale = new Vector3(widthMeters / bw, len / bh, widthMeters / bw);

            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Same cam-forward pull as cloth unit billboards (full 3D local).
        /// </summary>
        internal static Vector3 BillboardPullLocal(
            Transform clothPivot,
            Camera clothCam,
            Vector3 localPos,
            float scaleMeters)
        {
            if (clothPivot == null || clothCam == null)
                return localPos;

            Vector3 world = clothPivot.TransformPoint(localPos);
            world -= clothCam.transform.forward * Mathf.Max(15f, scaleMeters * 0.08f);
            return clothPivot.InverseTransformPoint(world);
        }

        /// <summary>
        /// Camera-facing line quad between two cloth-locals (matches billboard icons on tilt).
        /// </summary>
        internal static void PlaceCamFacingLine(
            Transform lineTx,
            SpriteRenderer sr,
            Sprite sprite,
            Color color,
            Vector3 localFrom,
            Vector3 localTo,
            float widthMeters,
            Camera clothCam,
            int sortingOrder)
        {
            if (lineTx == null || sr == null || sprite == null || clothCam == null)
                return;

            Vector3 delta = localTo - localFrom;
            float len = delta.magnitude;
            if (len < 0.05f)
            {
                if (lineTx.gameObject.activeSelf)
                    lineTx.gameObject.SetActive(false);
                return;
            }

            if (!lineTx.gameObject.activeSelf)
                lineTx.gameObject.SetActive(true);

            Vector3 mid = (localFrom + localTo) * 0.5f;
            lineTx.localPosition = mid;

            Vector3 worldDelta = lineTx.parent != null
                ? lineTx.parent.TransformDirection(delta.normalized)
                : delta.normalized;
            Vector3 view = -clothCam.transform.forward;
            Vector3 up = worldDelta;
            if (Mathf.Abs(Vector3.Dot(view, up)) > 0.98f)
                up = clothCam.transform.up;
            lineTx.rotation = Quaternion.LookRotation(view, up);

            float bw = Mathf.Max(sprite.bounds.size.x, 0.0001f);
            float bh = Mathf.Max(sprite.bounds.size.y, 0.0001f);
            lineTx.localScale = new Vector3(widthMeters / bw, len / bh, 1f);

            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
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

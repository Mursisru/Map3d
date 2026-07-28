using System;
using System.Collections.Generic;
using System.Reflection;
using Map3d.Config;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Unit icons + view cone on the tilted cloth pivot.
    /// Vehicles: camera-facing billboard.
    /// Large buildings (stock scale=true): camera-facing rect with stock width×length proportions
    /// and stock min-size floor (mapInverseScale×10 equiv) — flat coplanar footprints vanish under
    /// opaque cloth / become sub-pixel on km-scale windows.
    /// </summary>
    internal sealed class ClothIconLayer : IDisposable
    {
        private const float DiagIntervalSeconds = 8f;
        /// <summary>Stock building floor is mapInverseScale×10 vs vehicle ×15 → 10/15 of icon meters.</summary>
        private const float BuildingMinVsVehicle = 10f / 15f;

        private static readonly Quaternion FlatOnCloth = Quaternion.Euler(90f, 0f, 0f);

        private static readonly FieldInfo? TrackingInfoField =
            typeof(UnitMapIcon).GetField(
                "trackingInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(64);
        private Material? _iconMat;
        private Transform? _cone;
        private SpriteRenderer? _coneSr;
        private float _nextDiagTime;
        private Sprite? _fallbackSprite;
        private Texture2D? _fallbackTex;

        internal ClothIconLayer(Transform clothPivot)
        {
            _root = clothPivot;
            _iconMat = CreateAlwaysOnTopSpriteMat();
            EnsureFallbackSprite();

            var coneGo = new GameObject("ViewCone");
            coneGo.layer = MapTiltEngine.Layer;
            coneGo.transform.SetParent(_root, false);
            _cone = coneGo.transform;
            _coneSr = coneGo.AddComponent<SpriteRenderer>();
            _coneSr.sharedMaterial = _iconMat;
            _coneSr.sortingOrder = 5;
            _coneSr.shadowCastingMode = ShadowCastingMode.Off;
            _coneSr.color = new Color(1f, 1f, 1f, 0.35f);
            coneGo.SetActive(false);
        }

        internal void Sync(
            DynamicMap map,
            Aircraft own,
            Vector3 aircraftPos,
            Vector3 forward,
            float aircraftYaw,
            float radius,
            float clothZNear,
            float clothZFar,
            float clothHalfWidth,
            Camera? clothCam,
            HeightMapCache? heights,
            float heightScaleMeters,
            float iconLiftMeters)
        {
            if (map?.mapIcons == null)
            {
                HideAll();
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            float optionScale = SceneSingleton<MapOptions>.i != null
                ? Mathf.Max(0.25f, SceneSingleton<MapOptions>.i.iconSize)
                : 1f;
            float margin = 2500f;
            float lift = Mathf.Max(1f, iconLiftMeters);
            float refCamDist = StockMapMetrics.ResolveRefCameraDistance(clothCam, _root);
            float buildingMinMeters = StockMapMetrics.ResolveIconMeters(radius, 1f, optionScale) * BuildingMinVsVehicle;

            SyncViewCone(map, aircraftPos, right, forward, radius, heights, heightScaleMeters, lift);

            int used = 0;
            int culled = 0;
            int nullSprite = 0;
            int buildingFootprint = 0;
            int buildingSmall = 0;
            int vehicle = 0;

            for (int i = 0; i < map.mapIcons.Count; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon ui) || ui == null || ui.unit == null || ui.unit.disabled)
                    continue;

                Unit unit = ui.unit;
                if (unit is PilotDismounted
                    && SceneSingleton<MapOptions>.i != null
                    && !SceneSingleton<MapOptions>.i.showPilotIcons)
                    continue;

                Vector3 world = ResolveWorld(ui, map, unit);
                Vector3 delta = world - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);
                if (Mathf.Abs(x) > clothHalfWidth + margin
                    || z < clothZNear - margin
                    || z > clothZFar + margin)
                {
                    culled++;
                    continue;
                }

                bool isOwn = unit == own;
                Sprite? sprite = null;
                Color color = Color.white;
                if (ui.iconImage != null)
                {
                    sprite = ui.iconImage.sprite;
                    color = ui.iconImage.color;
                }
                if (sprite == null && unit.definition != null)
                    sprite = unit.definition.mapIcon;
                if (sprite == null)
                {
                    nullSprite++;
                    sprite = _fallbackSprite;
                }
                if (ui.iconImage == null)
                    color = isOwn ? Color.white : StockColor(unit, map.selectedIcons != null && map.selectedIcons.Contains(ui));
                color.a = 1f;

                float y = ClothSurfaceY(heights, heightScaleMeters, world, lift);
                if (isOwn)
                    y += 1f;
                var localPos = new Vector3(x, y, z);

                bool footprint = IsBuildingFootprint(unit);
                Slot slot = Get(used);
                if (footprint)
                {
                    float width = unit.definition != null ? Mathf.Max(1f, unit.definition.width) : 20f;
                    float length = unit.definition != null ? Mathf.Max(1f, unit.definition.length) : 20f;
                    ApplyStockBuildingMinSize(ref width, ref length, buildingMinMeters);
                    float maxDim = Mathf.Max(width, length);
                    float scaledMax = StockMapMetrics.CompensatePerspectiveIconSize(
                        _root, clothCam, localPos, maxDim, refCamDist);
                    if (scaledMax > maxDim + 0.01f)
                    {
                        float k = scaledMax / maxDim;
                        width *= k;
                        length *= k;
                    }

                    // Heading-aligned up so rect matches stock footprint yaw on the map plane.
                    Vector3 headingUp = clothCam != null ? clothCam.transform.up : Vector3.up;
                    Vector3 uf = unit.transform.forward;
                    uf.y = 0f;
                    if (uf.sqrMagnitude > 1e-6f)
                        headingUp = uf.normalized;

                    slot.ShowBuildingRect(sprite, color, localPos, width, length, headingUp, clothCam);
                    buildingFootprint++;
                }
                else
                {
                    if (unit is Building)
                        buildingSmall++;
                    else
                        vehicle++;

                    float mapIconSize = unit.definition != null ? Mathf.Max(0.25f, unit.definition.mapIconSize) : 1f;
                    float scale = StockMapMetrics.ResolveIconMeters(radius, mapIconSize, optionScale);
                    if (isOwn)
                        scale *= 1.1f;

                    bool orient = ShouldOrientIcon(unit);
                    Vector3 headingUp = clothCam != null ? clothCam.transform.up : Vector3.up;
                    if (orient)
                    {
                        Vector3 uf = unit.transform.forward;
                        uf.y = 0f;
                        if (uf.sqrMagnitude > 1e-6f)
                            headingUp = uf.normalized;
                    }

                    scale = StockMapMetrics.CompensatePerspectiveIconSize(_root, clothCam, localPos, scale, refCamDist);
                    slot.ShowBillboard(sprite, color, localPos, scale, headingUp, isOwn, clothCam);
                }

                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();

            MaybeLogDiag(map.mapIcons.Count, used, buildingFootprint, buildingSmall, vehicle, nullSprite, culled);
        }

        /// <summary>
        /// Stock UnitMapIcon.UpdateIcon: if trueSize max &lt; mapInverseScale×10, scale up proportions.
        /// </summary>
        private static void ApplyStockBuildingMinSize(ref float width, ref float length, float minMaxDim)
        {
            float maxDim = Mathf.Max(width, length, 0.0001f);
            if (maxDim >= minMaxDim)
                return;
            float k = minMaxDim / maxDim;
            width *= k;
            length *= k;
        }

        private void MaybeLogDiag(
            int total,
            int used,
            int footprint,
            int buildingSmall,
            int vehicle,
            int nullSprite,
            int culled)
        {
            float now = Time.unscaledTime;
            if (now < _nextDiagTime)
                return;
            _nextDiagTime = now + DiagIntervalSeconds;
            Debug.LogWarning(
                $"[Map3d] icons total={total} used={used} footprint={footprint} buildingSmall={buildingSmall} vehicle={vehicle} nullSprite={nullSprite} culled={culled}");
        }

        /// <summary>Stock UnitMapIcon.scale = Building &amp;&amp; maxRadius &gt; 10.</summary>
        private static bool IsBuildingFootprint(Unit unit)
        {
            return unit is Building && unit.maxRadius > 10f;
        }

        private void SyncViewCone(
            DynamicMap map,
            Vector3 aircraftPos,
            Vector3 right,
            Vector3 forward,
            float radius,
            HeightMapCache? heights,
            float heightScaleMeters,
            float lift)
        {
            if (_cone == null || _coneSr == null)
                return;

            if (SceneSingleton<CameraStateManager>.i == null)
            {
                _cone.gameObject.SetActive(false);
                return;
            }

            Transform camTx = SceneSingleton<CameraStateManager>.i.transform;

            Sprite? coneSprite = null;
            Color coneColor = new Color(1f, 1f, 1f, 0.07f);
            Vector2 tipNorm = new Vector2(0.5f, 0.05f);
            if (map.viewIndicator != null)
            {
                var rt = map.viewIndicator.transform as RectTransform;
                if (rt != null)
                    tipNorm = rt.pivot;

                Image? img = map.viewIndicator.GetComponentInChildren<Image>(true);
                if (img != null)
                {
                    coneSprite = img.sprite;
                    if (img.color.a > 0.01f)
                        coneColor = img.color;
                }
            }

            if (coneSprite == null)
            {
                _cone.gameObject.SetActive(false);
                return;
            }

            Vector3 look = camTx.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 1e-6f)
            {
                look = camTx.up;
                look.y = 0f;
            }
            if (look.sqrMagnitude < 1e-6f)
                look = forward;
            else
                look.Normalize();

            float ang = Mathf.Atan2(Vector3.Dot(look, right), Vector3.Dot(look, forward)) * Mathf.Rad2Deg;
            Quaternion aim = FlatOnCloth * Quaternion.Euler(0f, 0f, -ang);

            float coneLen = StockMapMetrics.ResolveConeMeters(map, radius);
            _coneSr.sprite = coneSprite;
            _coneSr.color = coneColor;
            _coneSr.sortingOrder = 8;
            ApplyUniformScale(_coneSr, coneSprite, coneLen);

            Vector3 tipSprite = TipOffsetFromPivot(coneSprite, tipNorm);
            float s = _cone.localScale.x;
            Vector3 tipInCone = aim * (tipSprite * s);

            float y = ClothSurfaceY(heights, heightScaleMeters, aircraftPos, lift);
            _cone.gameObject.SetActive(true);
            _cone.localRotation = aim;
            _cone.localPosition = new Vector3(0f, y, 0f) - tipInCone;
        }

        private static float ClothSurfaceY(
            HeightMapCache? heights,
            float heightScaleMeters,
            Vector3 world,
            float lift)
        {
            if (heights == null || heightScaleMeters <= 0.0001f || !Map3dConfig.HeightEnabled.Value)
                return lift;
            if (!heights.IsReady || !heights.TrySampleWorld(world, out float h))
                return lift;
            return (h - heights.SeaY) * heightScaleMeters + lift;
        }

        private static bool ShouldOrientIcon(Unit unit)
        {
            if (unit == null)
                return false;
            if (unit is Aircraft)
                return true;
            return unit.definition != null && unit.definition.mapOrient;
        }

        private static Vector3 TipOffsetFromPivot(Sprite sprite, Vector2 tipNorm)
        {
            Rect r = sprite.rect;
            float ppu = Mathf.Max(0.01f, sprite.pixelsPerUnit);
            Vector2 tipPx = new Vector2(r.x + r.width * tipNorm.x, r.y + r.height * tipNorm.y);
            Vector2 deltaPx = tipPx - sprite.pivot;
            return new Vector3(deltaPx.x / ppu, deltaPx.y / ppu, 0f);
        }

        private static Vector3 ResolveWorld(UnitMapIcon ui, DynamicMap map, Unit unit)
        {
            if (TrackingInfoField?.GetValue(ui) is TrackingInfo tip)
                return tip.GetPosition().ToLocalPosition();

            if (GameManager.GetLocalFaction(out _) && map.HQ != null)
            {
                TrackingInfo info = map.HQ.GetTrackingData(unit.persistentID);
                if (info != null)
                    return info.GetPosition().ToLocalPosition();
            }
            return unit.GlobalPosition().ToLocalPosition();
        }

        private static Color StockColor(Unit unit, bool selected)
        {
            FactionHQ? hq = unit.NetworkHQ;
            try
            {
                if (ThemeManager.Active == null || ThemeManager.Active.ColorTheme == null)
                    return hq?.faction != null ? hq.faction.color : Color.white;

                switch (DynamicMap.GetFactionMode(hq, checkNoFactionBeforeSpectator: true))
                {
                    case FactionMode.Spectator:
                        return hq?.faction != null
                            ? (selected ? hq.faction.selectedColor : hq.faction.color)
                            : Color.white;
                    case FactionMode.Friendly:
                        return selected
                            ? ThemeManager.Active.ColorTheme.MapIconFriendlySelected
                            : ThemeManager.Active.ColorTheme.MapIconFriendly;
                    case FactionMode.Enemy:
                        return selected
                            ? ThemeManager.Active.ColorTheme.MapIconHostileSelected
                            : ThemeManager.Active.ColorTheme.MapIconHostile;
                    default:
                        return selected
                            ? ThemeManager.Active.ColorTheme.MapIconNeutralSelected
                            : ThemeManager.Active.ColorTheme.MapIconNeutral;
                }
            }
            catch
            {
                return hq?.faction != null ? hq.faction.color : Color.white;
            }
        }

        private static Material CreateAlwaysOnTopSpriteMat()
        {
            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            var mat = new Material(sh!)
            {
                name = "Map3d.ClothIconMat",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3500
            };
            return mat;
        }

        private void EnsureFallbackSprite()
        {
            if (_fallbackSprite != null)
                return;
            _fallbackTex = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                name = "Map3d.IconFallback",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            var px = new Color32[64];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, 255);
            _fallbackTex.SetPixels32(px);
            _fallbackTex.Apply(false, true);
            _fallbackSprite = Sprite.Create(
                _fallbackTex,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                8f);
            _fallbackSprite.name = "Map3d.IconFallbackSprite";
            _fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void ApplyUniformScale(SpriteRenderer sr, Sprite? sprite, float worldSize)
        {
            if (sr == null)
                return;
            Transform t = sr.transform;
            if (sprite == null)
            {
                t.localScale = Vector3.one * worldSize;
                return;
            }
            Bounds b = sprite.bounds;
            float dim = Mathf.Max(b.size.x, b.size.y, 0.0001f);
            float s = worldSize / dim;
            t.localScale = new Vector3(s, s, s);
        }

        private Slot Get(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("ClothIcon");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = _iconMat;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                _pool.Add(new Slot(go, sr));
            }
            return _pool[index];
        }

        private void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
                _pool[i].Hide();
            if (_cone != null)
                _cone.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            HideAll();
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].Go != null)
                    UnityEngine.Object.Destroy(_pool[i].Go);
            }
            _pool.Clear();
            if (_cone != null)
            {
                UnityEngine.Object.Destroy(_cone.gameObject);
                _cone = null;
            }
            if (_iconMat != null)
            {
                UnityEngine.Object.Destroy(_iconMat);
                _iconMat = null;
            }
            if (_fallbackSprite != null)
            {
                UnityEngine.Object.Destroy(_fallbackSprite);
                _fallbackSprite = null;
            }
            if (_fallbackTex != null)
            {
                UnityEngine.Object.Destroy(_fallbackTex);
                _fallbackTex = null;
            }
        }

        private sealed class Slot
        {
            internal readonly GameObject Go;
            private readonly SpriteRenderer _sr;

            internal Slot(GameObject go, SpriteRenderer sr)
            {
                Go = go;
                _sr = sr;
            }

            internal void ShowBillboard(
                Sprite? sprite,
                Color color,
                Vector3 localPos,
                float scale,
                Vector3 headingUp,
                bool isOwn,
                Camera? clothCam)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);
                Transform t = Go.transform;
                t.localPosition = localPos;

                if (clothCam != null)
                {
                    Vector3 view = -clothCam.transform.forward;
                    Vector3 up = headingUp.sqrMagnitude > 1e-6f ? headingUp.normalized : clothCam.transform.up;
                    if (Mathf.Abs(Vector3.Dot(view, up)) > 0.98f)
                        up = clothCam.transform.up;
                    t.rotation = Quaternion.LookRotation(view, up);
                }
                else
                {
                    t.localRotation = FlatOnCloth;
                }

                ApplyUniformScale(_sr, sprite, Mathf.Max(1f, scale));
                _sr.sprite = sprite;
                _sr.color = color;
                _sr.sortingOrder = isOwn ? 40 : 20;

                if (clothCam != null)
                    t.position -= clothCam.transform.forward * Mathf.Max(15f, scale * 0.08f);
            }

            /// <summary>
            /// Building true-size rect facing cloth cam (readable), aspect = stock width×length.
            /// </summary>
            internal void ShowBuildingRect(
                Sprite? sprite,
                Color color,
                Vector3 localPos,
                float widthMeters,
                float lengthMeters,
                Vector3 headingUp,
                Camera? clothCam)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.localPosition = localPos;

                if (clothCam != null)
                {
                    Vector3 view = -clothCam.transform.forward;
                    Vector3 up = headingUp.sqrMagnitude > 1e-6f ? headingUp.normalized : clothCam.transform.up;
                    if (Mathf.Abs(Vector3.Dot(view, up)) > 0.98f)
                        up = clothCam.transform.up;
                    t.rotation = Quaternion.LookRotation(view, up);
                }
                else
                {
                    t.localRotation = FlatOnCloth;
                }

                float bw = 1f;
                float bh = 1f;
                if (sprite != null)
                {
                    Bounds b = sprite.bounds;
                    bw = Mathf.Max(b.size.x, 0.0001f);
                    bh = Mathf.Max(b.size.y, 0.0001f);
                }

                t.localScale = new Vector3(
                    Mathf.Max(1f, widthMeters) / bw,
                    Mathf.Max(1f, lengthMeters) / bh,
                    1f);
                _sr.sprite = sprite;
                _sr.color = color;
                _sr.sortingOrder = 25;

                float pull = Mathf.Max(40f, Mathf.Max(widthMeters, lengthMeters) * 0.12f);
                if (clothCam != null)
                    t.position -= clothCam.transform.forward * pull;
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

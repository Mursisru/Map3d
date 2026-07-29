using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Notch: 3D position at aircraft; full screen-plane billboard (same 2D look as unit icons).
    /// Angle = stock mapImage.z − LookRotation.yaw, drawn in camera up/right plane.
    /// </summary>
    internal sealed class ClothNotchLayer : IDisposable
    {
        private const float StockUiLength = 150f;
        private const float StockUiWidth = 1f;
        private const float MinWidthMeters = 18f;
        private const float MaxWidthMeters = 70f;

        private static readonly FieldInfo? ThreatListField =
            typeof(CombatHUD).GetField("threatList", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? ItemLookupField =
            typeof(ThreatList).GetField("itemLookup", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? MissileField =
            typeof(ThreatItem).GetField("missile", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? NotchLineField =
            typeof(ThreatItem).GetField("notchLine", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? NotchIndicatorBoxField =
            typeof(ThreatItem).GetField("notchIndicatorBox", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? NotchPrefabField =
            typeof(DynamicMap).GetField("notchLinePrefab", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(4);
        private readonly Material _mat;
        private Sprite? _sprite;
        private float _prefabAlpha = 0.5f;
        private bool _spriteResolved;

        internal ClothNotchLayer(Transform clothPivot)
        {
            _root = clothPivot;
            _mat = ClothSpriteUtil.CreateTransparentSpriteMaterial("Map3d.ClothNotchMat", 3100);
        }

        internal void Sync(
            DynamicMap map,
            Aircraft? own,
            Vector3 aircraftPos,
            Vector3 forward,
            Vector3 right,
            float radius,
            HeightMapCache? heights,
            float heightScaleMeters,
            float iconLiftMeters,
            Camera? clothCam)
        {
            if (map == null || own == null || own.disabled || clothCam == null)
            {
                HideAll();
                return;
            }

            EnsureSprite(map);
            if (_sprite == null)
            {
                HideAll();
                return;
            }

            IEnumerable? items = ResolveThreatItems();
            if (items == null)
            {
                HideAll();
                return;
            }

            if (!StockMapMetrics.TryGetDisplayFactor(map, out float display) || display < 1e-8f)
                display = 0.01f;

            float lengthMeters = Mathf.Clamp(StockUiLength / display, radius * 0.2f, radius * 2.2f);
            float widthMeters = Mathf.Clamp(StockUiWidth / display, MinWidthMeters, MaxWidthMeters);
            float refCamDist = StockMapMetrics.ResolveRefCameraDistance(clothCam, _root);

            float lift = Mathf.Max(1f, iconLiftMeters);
            float y = ClothSurfaceY(heights, heightScaleMeters, aircraftPos, lift) + 4f;
            var aircraftCloth = new Vector3(0f, y, 0f);

            // Same heading reference as DynamicMap.CenterMinimizedMap → mapImage.eulerAngles.z
            float mapZ = own.transform.eulerAngles.y;
            Rigidbody? rb = own.rb;
            int used = 0;

            foreach (object? entry in items)
            {
                if (entry is not ThreatItem item || item == null || !item.isActiveAndEnabled)
                    continue;

                if (MissileField?.GetValue(item) is not Missile missile || missile == null || missile.disabled)
                    continue;

                if (NotchLineField?.GetValue(item) is not GameObject notchGo || notchGo == null)
                    continue;
                if (!notchGo.activeSelf)
                    continue;

                Image? stockImg = notchGo.GetComponent<Image>();
                if (stockImg != null)
                    stockImg.enabled = false;

                if (!TryStockLineZ(own, rb, missile, mapZ, out float lineZ))
                    continue;

                float len = StockMapMetrics.CompensatePerspectiveIconSize(
                    _root, clothCam, aircraftCloth, lengthMeters, refCamDist);
                float wid = StockMapMetrics.CompensatePerspectiveIconSize(
                    _root, clothCam, aircraftCloth, widthMeters, refCamDist);

                Color color = ResolveNotchColor(item, missile);
                Get(used).ShowScreenBillboard2D(
                    aircraftCloth,
                    lineZ,
                    len,
                    wid,
                    _sprite,
                    color,
                    clothCam);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        /// <summary>
        /// Stock AlignNotchLine: notchLine.eulerAngles.z = mapImage.z − LookRotation(notch).y.
        /// </summary>
        private static bool TryStockLineZ(
            Aircraft own,
            Rigidbody? rb,
            Missile missile,
            float mapZ,
            out float lineZ)
        {
            lineZ = 0f;
            if (own == null || missile == null)
                return false;

            GlobalPosition evasion = missile.GetEvasionPoint();
            GlobalPosition ac = own.GlobalPosition();
            Vector3 evasionVector = evasion - ac;
            if (evasionVector.sqrMagnitude < 1e-6f)
                return false;

            Vector3 velocity = rb != null ? rb.velocity : own.transform.forward;
            Vector3 rhs = Vector3.Cross(evasionVector, velocity);
            Vector3 vector = Vector3.Cross(evasionVector, rhs);
            if (vector.sqrMagnitude < 1e-8f)
                return false;

            if (Vector3.Dot(own.transform.forward, vector) < 0f)
                vector *= -1f;

            vector.y = 0f;
            if (vector.sqrMagnitude < 1e-8f)
                return false;

            float notchYaw = Quaternion.LookRotation(vector, Vector3.up).eulerAngles.y;
            lineZ = Mathf.DeltaAngle(0f, mapZ - notchYaw);
            return true;
        }

        private Color ResolveNotchColor(ThreatItem item, Missile missile)
        {
            if (NotchIndicatorBoxField?.GetValue(item) is Image box && box != null)
            {
                Color c = box.color;
                c.a = Mathf.Clamp01(Mathf.Max(c.a, _prefabAlpha));
                return c;
            }

            Color color = Color.green;
            float t = Time.timeSinceLevelLoad;
            if (missile.seekerMode == Missile.SeekerMode.activeLock)
                color = Color.Lerp(Color.yellow, Color.red, Mathf.Sin(t * 20f) + 0.5f);
            else if (missile.seekerMode == Missile.SeekerMode.activeSearch)
                color = Color.Lerp(Color.green, Color.yellow, Mathf.Sin(t * 10f) + 0.5f);
            color.a = _prefabAlpha;
            return color;
        }

        private static IEnumerable? ResolveThreatItems()
        {
            CombatHUD? hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || ThreatListField == null || ItemLookupField == null)
                return null;

            object? list = ThreatListField.GetValue(hud);
            if (list == null)
                return null;

            object? lookup = ItemLookupField.GetValue(list);
            if (lookup is not IDictionary dict)
                return null;

            return dict.Values;
        }

        private void EnsureSprite(DynamicMap map)
        {
            if (_spriteResolved)
                return;
            _spriteResolved = true;

            GameObject? prefab = NotchPrefabField?.GetValue(map) as GameObject;
            if (prefab == null)
                return;

            Image? img = prefab.GetComponent<Image>();
            if (img == null)
                img = prefab.GetComponentInChildren<Image>(true);
            if (img == null || img.sprite == null)
                return;

            _sprite = ClothSpriteUtil.GetBlackKeyedSprite(img.sprite);
            if (img.color.a > 0.01f)
                _prefabAlpha = img.color.a;
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

        private Slot Get(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("ClothNotch");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                ClothSpriteUtil.ConfigureSpriteRenderer(sr, _mat);
                sr.sortingOrder = 35;
                _pool.Add(new Slot(go, sr));
            }
            return _pool[index];
        }

        private void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
                _pool[i].Hide();
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
            UnityEngine.Object.Destroy(_mat);
            _sprite = null;
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

            /// <summary>
            /// True 2D: quad parallel to cloth cam (like unit icons). Length along stock UI angle
            /// in the camera up/right plane (cam.up = aircraft forward on RT).
            /// </summary>
            internal void ShowScreenBillboard2D(
                Vector3 aircraftCloth,
                float stockLineZ,
                float lengthMeters,
                float widthMeters,
                Sprite sprite,
                Color color,
                Camera clothCam)
            {
                if (lengthMeters < 0.05f)
                {
                    Hide();
                    return;
                }

                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                Transform? parent = t.parent;

                Vector3 worldAircraft = parent != null
                    ? parent.TransformPoint(aircraftCloth)
                    : aircraftCloth;

                Vector3 view = -clothCam.transform.forward;
                Vector3 screenUp = clothCam.transform.up;
                Vector3 screenRight = clothCam.transform.right;
                // Stock UI: Quaternion.Euler(0,0,lineZ) * Vector3.up → cos*up - sin*right
                float rad = stockLineZ * Mathf.Deg2Rad;
                Vector3 lineUp = screenUp * Mathf.Cos(rad) - screenRight * Mathf.Sin(rad);
                if (lineUp.sqrMagnitude < 1e-8f)
                    lineUp = screenUp;
                else
                    lineUp.Normalize();

                t.rotation = Quaternion.LookRotation(view, lineUp);

                float bw = Mathf.Max(sprite.bounds.size.x, 0.0001f);
                float bh = Mathf.Max(sprite.bounds.size.y, 0.0001f);
                t.localScale = new Vector3(widthMeters / bw, lengthMeters / bh, 1f);

                t.position = worldAircraft
                             + lineUp * (lengthMeters * 0.5f)
                             - clothCam.transform.forward * Mathf.Max(40f, widthMeters * 0.3f);

                _sr.sprite = sprite;
                _sr.color = color;
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

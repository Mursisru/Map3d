using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock TargetMarker on cloth: 3D at bound unit, 2D screen billboard + info text block.
    /// </summary>
    internal sealed class ClothTargetMarkerLayer : IDisposable
    {
        private const float SizeVsUnitIcon = 1.65f;
        private const float CullMarginMeters = 2500f;
        private const float LabelCharSize = 0.55f;
        private const int LabelFontSize = 28;

        private static readonly FieldInfo? TrackingInfoField =
            typeof(UnitMapIcon).GetField(
                "trackingInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoPlayerField =
            typeof(TargetMarker).GetField("infoPlayer", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoNameField =
            typeof(TargetMarker).GetField("infoName", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoSpeedField =
            typeof(TargetMarker).GetField("infoSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoAltField =
            typeof(TargetMarker).GetField("infoAlt", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoHeadingField =
            typeof(TargetMarker).GetField("infoHeading", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? InfoRangeField =
            typeof(TargetMarker).GetField("infoRange", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(8);
        private readonly Material _mat;
        private readonly StringBuilder _sb = new StringBuilder(96);

        internal ClothTargetMarkerLayer(Transform clothPivot)
        {
            _root = clothPivot;
            _mat = ClothSpriteUtil.CreateTransparentSpriteMaterial("Map3d.ClothTargetMarkerMat", 3200);
        }

        internal void Sync(
            DynamicMap map,
            Aircraft? own,
            Vector3 aircraftPos,
            Vector3 forward,
            float radius,
            float clothZNear,
            float clothZFar,
            float clothHalfWidth,
            Camera? clothCam,
            HeightMapCache? heights,
            float heightScaleMeters,
            float iconLiftMeters)
        {
            MapOptions? opts = SceneSingleton<MapOptions>.i;
            if (map?.mapMarkers == null || opts == null || !opts.showTargetInfo || clothCam == null)
            {
                HideAll();
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            float optionScale = opts.iconSize > 0.01f ? Mathf.Max(0.25f, opts.iconSize) : 1f;
            float lift = Mathf.Max(1f, iconLiftMeters);
            float refCamDist = StockMapMetrics.ResolveRefCameraDistance(clothCam, _root);
            float baseSize = StockMapMetrics.ResolveIconMeters(radius, 1f, optionScale) * SizeVsUnitIcon;

            int used = 0;
            List<MapMarker> markers = map.mapMarkers;
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] is not TargetMarker tm || tm == null)
                    continue;

                UnitMapIcon? icon = tm.Icon;
                Unit? unit = tm.GetUnit();
                if (icon == null || unit == null || unit.disabled)
                    continue;

                Sprite? sprite = tm.markerImg != null ? tm.markerImg.sprite : null;
                if (sprite == null)
                {
                    try
                    {
                        if (GameAssets.i != null)
                        {
                            bool friendly = unit.NetworkHQ != null
                                            && map.HQ != null
                                            && unit.NetworkHQ == map.HQ;
                            sprite = friendly
                                ? GameAssets.i.targetUnitSpriteFriendly
                                : GameAssets.i.targetUnitSprite;
                        }
                    }
                    catch
                    {
                        sprite = null;
                    }
                }
                if (sprite == null)
                    continue;

                Vector3 world = ResolveWorld(icon, map, unit);
                Vector3 delta = world - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);
                if (Mathf.Abs(x) > clothHalfWidth + CullMarginMeters
                    || z < clothZNear - CullMarginMeters
                    || z > clothZFar + CullMarginMeters)
                    continue;

                float y = ClothSurfaceY(heights, heightScaleMeters, world, lift) + 3f;
                var localPos = new Vector3(x, y, z);
                float scale = StockMapMetrics.CompensatePerspectiveIconSize(
                    _root, clothCam, localPos, baseSize, refCamDist);

                Color color = tm.markerImg != null ? tm.markerImg.color : Color.white;
                color.a = Mathf.Clamp01(Mathf.Max(color.a, 0.75f));

                string label = BuildLabel(tm);
                Color labelColor = color;
                labelColor.a = 1f;

                SilenceStockUi(tm);

                Get(used).ShowBillboard(sprite, color, localPos, scale, clothCam, label, labelColor);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        private string BuildLabel(TargetMarker tm)
        {
            _sb.Length = 0;
            AppendLine(InfoPlayerField, tm);
            AppendLine(InfoNameField, tm);
            AppendLine(InfoSpeedField, tm);
            AppendLine(InfoAltField, tm);
            AppendLine(InfoHeadingField, tm);
            AppendLine(InfoRangeField, tm);
            return _sb.ToString();
        }

        private void AppendLine(FieldInfo? field, TargetMarker tm)
        {
            if (field?.GetValue(tm) is not Text ui || ui == null)
                return;
            if (string.IsNullOrEmpty(ui.text))
                return;
            if (_sb.Length > 0)
                _sb.Append('\n');
            _sb.Append(ui.text);
        }

        private static void SilenceStockUi(TargetMarker tm)
        {
            if (tm.markerImg != null && tm.markerImg.enabled)
                tm.markerImg.enabled = false;
            SilenceText(InfoPlayerField, tm);
            SilenceText(InfoNameField, tm);
            SilenceText(InfoSpeedField, tm);
            SilenceText(InfoAltField, tm);
            SilenceText(InfoHeadingField, tm);
            SilenceText(InfoRangeField, tm);
        }

        private static void SilenceText(FieldInfo? field, TargetMarker tm)
        {
            if (field?.GetValue(tm) is Text ui && ui != null && ui.enabled)
                ui.enabled = false;
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
                var go = new GameObject("ClothTargetMarker");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                ClothSpriteUtil.ConfigureSpriteRenderer(sr, _mat);
                sr.sortingOrder = 45;

                var labelGo = new GameObject("Info");
                labelGo.layer = MapTiltEngine.Layer;
                labelGo.transform.SetParent(go.transform, false);
                var mesh = labelGo.AddComponent<TextMesh>();
                mesh.anchor = TextAnchor.UpperLeft;
                mesh.alignment = TextAlignment.Left;
                mesh.characterSize = LabelCharSize;
                mesh.fontSize = LabelFontSize;
                mesh.color = Color.white;
                mesh.text = string.Empty;
                ClothSpriteUtil.SetupTextMesh(mesh);
                labelGo.SetActive(false);

                _pool.Add(new Slot(go, sr, mesh));
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
        }

        private sealed class Slot
        {
            internal readonly GameObject Go;
            private readonly SpriteRenderer _sr;
            private readonly TextMesh _label;

            internal Slot(GameObject go, SpriteRenderer sr, TextMesh label)
            {
                Go = go;
                _sr = sr;
                _label = label;
            }

            internal void ShowBillboard(
                Sprite sprite,
                Color color,
                Vector3 localPos,
                float scaleMeters,
                Camera clothCam,
                string label,
                Color labelColor)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.localPosition = localPos;

                Vector3 view = -clothCam.transform.forward;
                Vector3 up = clothCam.transform.up;
                t.rotation = Quaternion.LookRotation(view, up);

                float bw = Mathf.Max(sprite.bounds.size.x, 0.0001f);
                float bh = Mathf.Max(sprite.bounds.size.y, 0.0001f);
                float dim = Mathf.Max(bw, bh);
                float s = scaleMeters / dim;
                t.localScale = new Vector3(s, s, s);

                t.position -= clothCam.transform.forward * Mathf.Max(30f, scaleMeters * 0.1f);

                _sr.sprite = sprite;
                _sr.color = color;

                if (!string.IsNullOrEmpty(label))
                {
                    _label.text = label;
                    _label.color = labelColor;
                    float inv = 1f / Mathf.Max(s, 0.0001f);
                    _label.transform.localScale = new Vector3(inv, inv, inv);
                    // Right of brackets, slightly above center — stock info panel layout.
                    _label.transform.localPosition = new Vector3(0.55f * dim + 0.1f, 0.45f * dim, -0.02f);
                    if (!_label.gameObject.activeSelf)
                        _label.gameObject.SetActive(true);
                }
                else if (_label.gameObject.activeSelf)
                {
                    _label.gameObject.SetActive(false);
                }
            }

            internal void Hide()
            {
                if (_label.gameObject.activeSelf)
                    _label.gameObject.SetActive(false);
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

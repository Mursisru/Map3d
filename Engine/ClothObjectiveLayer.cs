using System;
using System.Collections.Generic;
using System.Reflection;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock objective markers on cloth: billboard sprite + DisplayName label (GUN/SHED).
    /// </summary>
    internal sealed class ClothObjectiveLayer : IDisposable
    {
        private static readonly FieldInfo? PosResultField =
            typeof(ObjectiveMarker).GetField("posResult", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? ObjNameField =
            typeof(ObjectiveMarker).GetField("objName", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(16);
        private readonly Material _mat;

        internal ClothObjectiveLayer(Transform clothPivot)
        {
            _root = clothPivot;
            _mat = ClothSpriteUtil.CreateTransparentSpriteMaterial("Map3d.ClothObjectiveMat", 3000);
        }

        internal void Sync(
            DynamicMap map,
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
            if (map?.iconLayer == null || opts == null || !opts.showObjectives)
            {
                HideAll();
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            float margin = 2500f;
            float lift = Mathf.Max(1f, iconLiftMeters);
            float refCamDist = StockMapMetrics.ResolveRefCameraDistance(clothCam, _root);

            Transform layer = map.iconLayer.transform;
            int used = 0;
            for (int i = 0; i < layer.childCount; i++)
            {
                ObjectiveMarker? marker = layer.GetChild(i).GetComponent<ObjectiveMarker>();
                if (marker == null || !marker.shown || marker.markerImg == null)
                    continue;

                if (PosResultField?.GetValue(marker) is not MissionPosition.PositionResult pos)
                    continue;

                Vector3 world = pos.Position.ToLocalPosition();
                Vector3 delta = world - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);
                if (Mathf.Abs(x) > clothHalfWidth + margin)
                    continue;
                if (z < clothZNear - margin || z > clothZFar + margin)
                    continue;

                Sprite? sprite = marker.markerImg.sprite;
                if (sprite == null)
                    continue;

                Color color = marker.markerImg.color;
                string? label = null;
                Color labelColor = Color.green;
                if (ObjNameField?.GetValue(marker) is Text objName && objName != null)
                {
                    if (objName.enabled && !string.IsNullOrEmpty(objName.text) && !marker.masked)
                    {
                        label = objName.text;
                        labelColor = objName.color;
                        if (labelColor.a < 0.05f)
                            labelColor = Color.green;
                        labelColor.a = 1f;
                    }
                }

                if (marker.masked)
                {
                    color *= 0.5f;
                    color.a = 0.5f;
                }
                else
                    color.a = 1f;

                float ui = Mathf.Max(20f, marker.markerImg.rectTransform.sizeDelta.x);
                float scale = StockMapMetrics.ResolveObjectiveMeters(radius, ui);
                float y = ClothSurfaceY(heights, heightScaleMeters, world, lift);
                var localPos = new Vector3(x, y + 0.5f, z);
                scale = StockMapMetrics.CompensatePerspectiveIconSize(_root, clothCam, localPos, scale, refCamDist);

                Get(used).Show(sprite, color, localPos, scale, clothCam, label, labelColor);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
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
                var go = new GameObject("ClothObjective");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                ClothSpriteUtil.ConfigureSpriteRenderer(sr, _mat);

                var labelGo = new GameObject("Label");
                labelGo.layer = MapTiltEngine.Layer;
                labelGo.transform.SetParent(go.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                var tm = labelGo.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.35f;
                tm.fontSize = 32;
                tm.color = Color.green;
                tm.text = string.Empty;
                ClothSpriteUtil.SetupTextMesh(tm);
                labelGo.SetActive(false);

                _pool.Add(new Slot(go, sr, tm));
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

            internal void Show(
                Sprite sprite,
                Color color,
                Vector3 localPos,
                float scale,
                Camera? clothCam,
                string? label,
                Color labelColor)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.localPosition = localPos;

                if (clothCam != null)
                {
                    Vector3 view = -clothCam.transform.forward;
                    Vector3 up = clothCam.transform.up;
                    if (view.sqrMagnitude < 1e-6f)
                        view = Vector3.forward;
                    if (Mathf.Abs(Vector3.Dot(view.normalized, up.normalized)) > 0.98f)
                        up = Vector3.up;
                    t.rotation = Quaternion.LookRotation(view, up);
                }

                Bounds b = sprite.bounds;
                float dim = Mathf.Max(b.size.x, b.size.y, 0.0001f);
                float s = Mathf.Max(1f, scale) / dim;
                t.localScale = new Vector3(s, s, s);
                _sr.sprite = sprite;
                _sr.color = color;
                _sr.sortingOrder = 30;

                if (clothCam != null)
                    t.position -= clothCam.transform.forward * Mathf.Max(15f, scale * 0.08f);

                if (!string.IsNullOrEmpty(label))
                {
                    _label.text = label;
                    _label.color = labelColor;
                    // Counter parent scale so text stays readable.
                    float inv = 1f / Mathf.Max(s, 0.0001f);
                    _label.transform.localScale = new Vector3(inv, inv, inv);
                    _label.transform.localPosition = new Vector3(0f, 0.55f * dim + 0.15f, 0f);
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

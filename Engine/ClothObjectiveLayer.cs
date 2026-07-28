using System;
using System.Collections.Generic;
using System.Reflection;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock objective markers on cloth: 3D world position, billboard sprite like unit icons.
    /// </summary>
    internal sealed class ClothObjectiveLayer : IDisposable
    {
        private static readonly FieldInfo? PosResultField =
            typeof(ObjectiveMarker).GetField("posResult", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(16);
        private readonly Material _mat;

        internal ClothObjectiveLayer(Transform clothPivot)
        {
            _root = clothPivot;
            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            _mat = new Material(sh!)
            {
                name = "Map3d.ClothObjectiveMat",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        internal void Sync(
            DynamicMap map,
            Vector3 aircraftPos,
            Vector3 forward,
            float radius,
            float clothFarMeters,
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

            float cullFar = Mathf.Max(radius, clothFarMeters, clothHalfWidth);
            float cull = cullFar + 500f;
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
                if (x * x + z * z > cull * cull)
                    continue;

                Sprite? sprite = marker.markerImg.sprite;
                if (sprite == null)
                    continue;

                Color color = marker.markerImg.color;
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

                Get(used).Show(sprite, color, localPos, scale, clothCam);
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
                sr.sharedMaterial = _mat;
                sr.shadowCastingMode = ShadowCastingMode.Off;
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

            internal void Show(Sprite sprite, Color color, Vector3 localPos, float scale, Camera? clothCam)
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
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

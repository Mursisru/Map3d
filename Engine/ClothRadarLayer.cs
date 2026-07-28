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
    /// Stock radar ping lines on cloth: emitter → own aircraft, billboard stretch like stock radarVisPrefab.
    /// </summary>
    internal sealed class ClothRadarLayer : IDisposable
    {
        private const float GridYOffset = 0.006f;
        private const float LineWidthStockMul = 1f;

        private static readonly FieldInfo? RadarVisListField =
            typeof(DynamicMap).GetField("radarVisualizations", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(16);
        private readonly Material _mat;

        internal ClothRadarLayer(Transform clothPivot)
        {
            _root = clothPivot;
            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            _mat = new Material(sh!)
            {
                name = "Map3d.ClothRadarMat",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        internal void Sync(
            DynamicMap map,
            Aircraft? own,
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
            if (map == null || own == null || own.disabled || RadarVisListField?.GetValue(map) is not IList list)
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
            float now = Time.timeSinceLevelLoad;

            int used = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                    continue;

                if (!TryReadEntry(list[i], out Image? ui, out Unit? emitter, out float pingTime, out float delay))
                    continue;

                float age = now - pingTime;
                if (ui == null || emitter == null || emitter.disabled || age >= delay)
                    continue;

                Vector3 emitWorld = emitter.GlobalPosition().ToLocalPosition();
                Vector3 delta = emitWorld - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);
                if (x * x + z * z > cull * cull)
                    continue;

                Vector3 fromCloth = ToClothLocal(emitWorld, aircraftPos, right, forward, heights, heightScaleMeters, lift);
                fromCloth.y = GridYOffset;
                Vector3 toCloth = ToClothLocal(aircraftPos, aircraftPos, right, forward, heights, heightScaleMeters, lift);
                toCloth.y = GridYOffset;

                Sprite? sprite = ui.sprite;
                if (sprite == null)
                    continue;

                Color color = ui.color;
                color.a = Mathf.Lerp(color.a, 0f, age * 0.05f);
                if (color.a < 0.02f)
                    continue;

                ui.enabled = false;

                float width = StockMapMetrics.ResolveIconMeters(radius, LineWidthStockMul, 1f) * 0.15f;
                width = StockMapMetrics.CompensatePerspectiveIconSize(_root, clothCam, fromCloth, width, refCamDist);

                Get(used).ShowStretch(sprite, color, fromCloth, toCloth, width, clothCam);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        private static bool TryReadEntry(
            object entry,
            out Image? ui,
            out Unit? emitter,
            out float pingTime,
            out float delay)
        {
            ui = null;
            emitter = null;
            pingTime = 0f;
            delay = 1f;
            if (entry == null)
                return false;

            Type t = entry.GetType();
            ui = t.GetField("vectorImage")?.GetValue(entry) as Image;
            emitter = t.GetField("emitter")?.GetValue(entry) as Unit;
            if (t.GetField("pingTime")?.GetValue(entry) is float pt)
                pingTime = pt;
            if (t.GetField("delay")?.GetValue(entry) is float d)
                delay = d;
            return ui != null;
        }

        private static Vector3 ToClothLocal(
            Vector3 world,
            Vector3 aircraftPos,
            Vector3 right,
            Vector3 forward,
            HeightMapCache? heights,
            float heightScaleMeters,
            float lift)
        {
            Vector3 delta = world - aircraftPos;
            float x = Vector3.Dot(delta, right);
            float z = Vector3.Dot(delta, forward);
            float y = ClothSurfaceY(heights, heightScaleMeters, world, lift);
            return new Vector3(x, y, z);
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
                var go = new GameObject("ClothRadar");
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

            internal void ShowStretch(
                Sprite sprite,
                Color color,
                Vector3 clothFrom,
                Vector3 clothTo,
                float widthMeters,
                Camera? clothCam)
            {
                Vector3 w0 = Go.transform.parent!.TransformPoint(clothFrom);
                Vector3 w1 = Go.transform.parent!.TransformPoint(clothTo);
                Vector3 delta = w1 - w0;
                float len = delta.magnitude;
                if (len < 0.05f)
                {
                    Hide();
                    return;
                }

                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.position = w0;

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

                Vector3 localDir = t.InverseTransformDirection(delta.normalized);
                float zAng = Mathf.Atan2(localDir.x, localDir.y) * Mathf.Rad2Deg;
                t.rotation *= Quaternion.Euler(0f, 0f, zAng);

                float h = Mathf.Max(sprite.bounds.size.y, 0.0001f);
                t.localScale = new Vector3(widthMeters, len / h, widthMeters);
                _sr.sprite = sprite;
                _sr.color = color;
                _sr.sortingOrder = 25;
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

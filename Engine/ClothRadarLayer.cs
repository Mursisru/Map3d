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
    /// Stock radar ping lines on cloth: flat stretch emitter → own aircraft (same as RadarMapVis.Refresh).
    /// </summary>
    internal sealed class ClothRadarLayer : IDisposable
    {
        private static readonly Quaternion FlatOnCloth = Quaternion.Euler(90f, 0f, 0f);

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
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3000
            };
            _mat.SetInt("_ZTest", (int)CompareFunction.Always);
            _mat.SetInt("_ZWrite", 0);
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

            float lift = Mathf.Max(1f, iconLiftMeters);
            float margin = 2500f;
            float now = Time.timeSinceLevelLoad;

            Vector3 ownCloth = ToClothLocal(
                aircraftPos, aircraftPos, right, forward, heights, heightScaleMeters, lift);

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
                Vector3 fromCloth = ToClothLocal(
                    emitWorld, aircraftPos, right, forward, heights, heightScaleMeters, lift);

                if (!InClothWindow(fromCloth.x, fromCloth.z, clothZNear, clothZFar, clothHalfWidth, margin)
                    && !InClothWindow(ownCloth.x, ownCloth.z, clothZNear, clothZFar, clothHalfWidth, margin))
                    continue;

                Sprite? sprite = ui.sprite;
                if (sprite == null)
                    continue;

                Color color = ui.color;
                color.a = Mathf.Lerp(color.a, 0f, age * 0.05f);
                if (color.a < 0.02f)
                    continue;

                ui.enabled = false;

                float width = StockMapMetrics.ResolveRadarLineWidthMeters(map, ui, radius);
                Get(used).ShowFlatStretch(sprite, color, fromCloth, ownCloth, width);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        private static bool InClothWindow(
            float x,
            float z,
            float zNear,
            float zFar,
            float halfW,
            float margin)
        {
            if (Mathf.Abs(x) > halfW + margin)
                return false;
            return z >= zNear - margin && z <= zFar + margin;
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

            /// <summary>
            /// Flat on cloth XZ like stock UI line: pivot at emitter, length along +Y of sprite.
            /// </summary>
            internal void ShowFlatStretch(
                Sprite sprite,
                Color color,
                Vector3 clothFrom,
                Vector3 clothTo,
                float widthMeters)
            {
                Vector3 delta = clothTo - clothFrom;
                delta.y = 0f;
                float len = delta.magnitude;
                if (len < 0.05f)
                {
                    Hide();
                    return;
                }

                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                // Center-pivot SpriteRenderer: midpoint so ends land on emitter and own aircraft.
                float y = Mathf.Max(clothFrom.y, clothTo.y) + Mathf.Max(8f, widthMeters * 0.15f);
                t.localPosition = new Vector3(
                    (clothFrom.x + clothTo.x) * 0.5f,
                    y,
                    (clothFrom.z + clothTo.z) * 0.5f);

                float ang = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                t.localRotation = FlatOnCloth * Quaternion.Euler(0f, 0f, -ang);

                float bw = Mathf.Max(sprite.bounds.size.x, 0.0001f);
                float bh = Mathf.Max(sprite.bounds.size.y, 0.0001f);
                float sx = widthMeters / bw;
                float sy = len / bh;
                t.localScale = new Vector3(sx, sy, sx);

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

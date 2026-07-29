using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Map3d.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock RadarMapVis: flat cloth stretch emitter → own aircraft map slot (XZ before cam-pull).
    /// </summary>
    internal sealed class ClothRadarLayer : IDisposable
    {
        private static readonly FieldInfo? RadarVisListField =
            typeof(DynamicMap).GetField("radarVisualizations", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? TrackingInfoField =
            typeof(UnitMapIcon).GetField(
                "trackingInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Transform _root;
        private readonly List<Slot> _pool = new List<Slot>(16);
        private readonly Material _mat;
        private Sprite? _fallbackSprite;
        private Texture2D? _fallbackTex;

        internal ClothRadarLayer(Transform clothPivot)
        {
            _root = clothPivot;
            _mat = ClothSpriteUtil.CreateTransparentSpriteMaterial("Map3d.ClothRadarMat", 3000);
            EnsureFallbackSprite();
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
            float iconLiftMeters,
            Vector3? ownIconClothLocal)
        {
            if (map == null || own == null || own.disabled
                || RadarVisListField?.GetValue(map) is not IList list)
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

            // Same slot as own cloth icon / view cone: heading-frame origin (never tracking offset).
            float ownY = lift + 1f;
            if (ownIconClothLocal.HasValue)
                ownY = ownIconClothLocal.Value.y;
            Vector3 ownCloth = new Vector3(0f, ownY, 0f);

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
                if (DynamicMap.TryGetMapIcon(emitter, out UnitMapIcon emitIcon) && emitIcon != null)
                    emitWorld = ResolveWorld(emitIcon, map, emitter);

                Vector3 fromCloth = ToClothLocal(
                    emitWorld, aircraftPos, right, forward, heights, heightScaleMeters, lift);

                if (!InClothWindow(fromCloth.x, fromCloth.z, clothZNear, clothZFar, clothHalfWidth, margin)
                    && !InClothWindow(ownCloth.x, ownCloth.z, clothZNear, clothZFar, clothHalfWidth, margin))
                    continue;

                Sprite? sprite = ui.sprite;
                if (sprite == null)
                    sprite = _fallbackSprite;
                // Prefer known center-pivot fallback when stock pivot would skew endpoints.
                if (sprite != null && sprite.rect.height > 0.01f)
                {
                    float pivotY = sprite.pivot.y / sprite.rect.height;
                    if (pivotY < 0.15f || pivotY > 0.85f)
                        sprite = _fallbackSprite != null ? _fallbackSprite : sprite;
                }
                if (sprite == null)
                    continue;

                Color color = ui.color;
                color.a = Mathf.Lerp(color.a, 0f, age * 0.05f);
                if (color.a < 0.02f)
                    continue;

                ui.enabled = false;

                float width = StockMapMetrics.ResolveRadarLineWidthMeters(map, ui, radius);
                Get(used).Show(sprite, color, fromCloth, ownCloth, width);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        private void EnsureFallbackSprite()
        {
            if (_fallbackSprite != null)
                return;
            _fallbackTex = new Texture2D(2, 8, TextureFormat.RGBA32, false)
            {
                name = "Map3d.RadarLineFallback",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, 255);
            _fallbackTex.SetPixels32(px);
            _fallbackTex.Apply(false, true);
            _fallbackSprite = Sprite.Create(
                _fallbackTex,
                new Rect(0f, 0f, 2f, 8f),
                new Vector2(0.5f, 0.5f),
                8f);
            _fallbackSprite.name = "Map3d.RadarLineFallbackSprite";
            _fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
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
                ClothSpriteUtil.ConfigureSpriteRenderer(sr, _mat);
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

            internal void Show(
                Sprite sprite,
                Color color,
                Vector3 clothFrom,
                Vector3 clothTo,
                float widthMeters)
            {
                StockMapMetrics.PlaceFlatClothLine(
                    Go.transform,
                    _sr,
                    sprite,
                    color,
                    clothFrom,
                    clothTo,
                    widthMeters,
                    25);
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }
    }
}

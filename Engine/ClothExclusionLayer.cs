using System;
using System.Collections.Generic;
using NuclearOption;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Stock exclusion-zone circles on the cloth canvas (same flat layer as StockClothGrid).
    /// Diameter = ExclusionZone.radius (matches SizeDelta 1×1 × scale on DynamicMap).
    /// </summary>
    internal sealed class ClothExclusionLayer : IDisposable
    {
        private const float LocalYAboveGrid = 0.006f;
        private const float CullMarginMeters = 2500f;

        private static readonly Quaternion FlatOnCloth = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Color StockTint = new Color(1f, 0.4166667f, 0f, 0.5019608f);

        private readonly Transform _canvas;
        private readonly List<Slot> _pool = new List<Slot>(8);
        private readonly Material _mat;
        private Sprite? _sprite;
        private Color _color = StockTint;

        internal ClothExclusionLayer(Transform clothCanvas)
        {
            _canvas = clothCanvas;
            _mat = ClothSpriteUtil.CreateTransparentSpriteMaterial("Map3d.ClothExclusionMat", 3002);
        }

        internal void Sync(
            DynamicMap map,
            Vector3 aircraftPos,
            Vector3 forward,
            Vector3 right,
            float clothNear,
            float clothFar,
            float clothHalfW,
            float lookAhead)
        {
            if (map == null || _canvas == null)
            {
                HideAll();
                return;
            }

            EnsureSprite(map);

            FactionHQ? hq = map.HQ;
            if (hq == null || _sprite == null)
            {
                HideAll();
                return;
            }

            List<ExclusionZone>? zones = null;
            try
            {
                zones = hq.GetExclusionZones();
            }
            catch
            {
                HideAll();
                return;
            }

            if (zones == null || zones.Count == 0)
            {
                HideAll();
                return;
            }

            float sx = Mathf.Abs(_canvas.localScale.x);
            float sz = Mathf.Abs(_canvas.localScale.z);
            if (sx < 1e-4f || sz < 1e-4f)
            {
                HideAll();
                return;
            }

            float canvasZ = _canvas.localPosition.z;
            float zNear = lookAhead + clothNear;
            float zFar = lookAhead + clothFar;
            int used = 0;

            for (int i = 0; i < zones.Count; i++)
            {
                ExclusionZone zone = zones[i];
                float diameter = Mathf.Max(1f, zone.radius);
                Vector3 world = zone.position.ToLocalPosition();
                Vector3 delta = world - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);

                float reach = diameter * 0.5f + CullMarginMeters;
                if (Mathf.Abs(x) > clothHalfW + reach
                    || z < zNear - reach
                    || z > zFar + reach)
                    continue;

                float lx = x / sx;
                float lz = (z - canvasZ) / sz;
                Get(used).Show(_sprite, _color, new Vector3(lx, LocalYAboveGrid, lz), diameter, sx, sz);
                used++;
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();

            SilenceStockDisplays(map);
        }

        private void EnsureSprite(DynamicMap map)
        {
            if (_sprite != null)
                return;

            GameObject? prefab = null;
            try
            {
                if (GameAssets.i != null)
                    prefab = GameAssets.i.exclusionZoneDisplay;
            }
            catch
            {
                prefab = null;
            }

            if (prefab == null)
                return;

            Image? img = prefab.GetComponent<Image>();
            if (img == null)
                img = prefab.GetComponentInChildren<Image>(true);
            if (img == null || img.sprite == null)
                return;

            _sprite = img.sprite;
            if (img.color.a > 0.01f)
                _color = img.color;
        }

        /// <summary>Stock UI circles sit on soft-hidden iconLayer; disable Image to skip extra work.</summary>
        private void SilenceStockDisplays(DynamicMap map)
        {
            if (map.iconLayer == null || _sprite == null)
                return;

            Transform root = map.iconLayer.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c == null)
                    continue;
                Image? img = c.GetComponent<Image>();
                if (img == null || img.sprite != _sprite)
                    continue;
                if (img.enabled)
                    img.enabled = false;
            }
        }

        private Slot Get(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("ClothExclusion");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_canvas, false);
                var sr = go.AddComponent<SpriteRenderer>();
                ClothSpriteUtil.ConfigureSpriteRenderer(sr, _mat);
                sr.sortingOrder = 12;
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

            internal void Show(
                Sprite sprite,
                Color color,
                Vector3 canvasLocalPos,
                float diameterMeters,
                float parentScaleX,
                float parentScaleZ)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.localPosition = canvasLocalPos;
                t.localRotation = FlatOnCloth;

                float bw = 1f;
                float bh = 1f;
                Bounds b = sprite.bounds;
                bw = Mathf.Max(b.size.x, 0.0001f);
                bh = Mathf.Max(b.size.y, 0.0001f);

                // Undo non-uniform canvas scale so the circle stays round in tilt/world meters.
                float sx = diameterMeters / (bw * Mathf.Max(1e-4f, parentScaleX));
                float sy = diameterMeters / (bh * Mathf.Max(1e-4f, parentScaleZ));
                t.localScale = new Vector3(sx, sy, 1f);

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

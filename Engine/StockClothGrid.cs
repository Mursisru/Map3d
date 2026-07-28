using System;
using System.Collections.Generic;
using Map3d.Config;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Flat stock mapGrid tiles in 3D on the cloth plane (no terrain displacement).
    /// SpriteRenderer + Sprites/Default (same as icons). Cloth materials untouched.
    /// </summary>
    internal sealed class StockClothGrid : IDisposable
    {
        private const int MajorsPerTile = 4;
        private const float FlatYMeters = 1.5f;
        private static readonly Quaternion FlatOnCloth = Quaternion.Euler(90f, 0f, 0f);

        private readonly Transform _pivot;
        private readonly List<TileSlot> _pool = new List<TileSlot>(64);
        private readonly List<TileInfo> _tiles = new List<TileInfo>(64);
        private Material? _mat;
        private Sprite? _sprite;
        private bool _cached;
        private int _gridSizeX;
        private int _gridSizeY;
        private float _tileSizeX = 40000f;
        private float _tileSizeZ = 40000f;

        private sealed class TileInfo
        {
            internal int M;
            internal int N;
            internal float CenterGx;
            internal float CenterGz;
        }

        private sealed class TileSlot
        {
            internal readonly GameObject Go;
            private readonly SpriteRenderer _sr;

            internal TileSlot(GameObject go, SpriteRenderer sr)
            {
                Go = go;
                _sr = sr;
            }

            internal void Show(
                Sprite sprite,
                Material mat,
                Color color,
                Vector3 localPos,
                float sizeX,
                float sizeZ,
                float alignYawDeg)
            {
                if (!Go.activeSelf)
                    Go.SetActive(true);

                Transform t = Go.transform;
                t.localPosition = localPos;
                t.localRotation = FlatOnCloth * Quaternion.Euler(0f, 0f, -alignYawDeg);

                _sr.sharedMaterial = mat;
                _sr.sprite = sprite;
                _sr.color = color;
                _sr.sortingOrder = 3;
                _sr.shadowCastingMode = ShadowCastingMode.Off;

                Bounds b = sprite.bounds;
                float bx = Mathf.Max(b.size.x, 0.0001f);
                float by = Mathf.Max(b.size.y, 0.0001f);
                t.localScale = new Vector3(sizeX / bx, sizeZ / by, 1f);
            }

            internal void Hide()
            {
                if (Go.activeSelf)
                    Go.SetActive(false);
            }
        }

        internal StockClothGrid(Transform clothPivot)
        {
            _pivot = clothPivot;
            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            _mat = new Material(sh!)
            {
                name = "Map3d.StockClothGridMat",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        internal void Sync(
            DynamicMap map,
            Vector3 aircraftPos,
            Vector3 forward,
            Vector3 right,
            float clothNear,
            float clothFar,
            float clothHalfW,
            Vector2 mapSize,
            bool visible)
        {
            if (!visible || !Map3dConfig.GridEnabled.Value || map?.gridLabels == null)
            {
                HideAll();
                return;
            }

            if (!EnsureTiles(map, mapSize))
            {
                HideAll();
                return;
            }

            Color color = ResolveColor(Mathf.Clamp01(Map3dConfig.GridOpacity.Value));
            float alignYaw = Mathf.Atan2(
                Vector3.Dot(Vector3.right, right),
                Vector3.Dot(Vector3.right, forward)) * Mathf.Rad2Deg;

            float cull = Mathf.Max(clothFar, clothHalfW) + _tileSizeX;
            int used = 0;

            for (int i = 0; i < _tiles.Count; i++)
            {
                TileInfo tile = _tiles[i];
                Vector3 world = new GlobalPosition(tile.CenterGx, 0f, tile.CenterGz).AsVector3();
                Vector3 delta = world - aircraftPos;
                float x = Vector3.Dot(delta, right);
                float z = Vector3.Dot(delta, forward);

                if (z < clothNear - _tileSizeZ || z > clothFar + _tileSizeZ)
                    continue;
                if (Mathf.Abs(x) > clothHalfW + _tileSizeX)
                    continue;
                if (x * x + z * z > cull * cull)
                    continue;

                Get(used++).Show(
                    _sprite!,
                    _mat!,
                    color,
                    new Vector3(x, FlatYMeters, z),
                    _tileSizeX,
                    _tileSizeZ,
                    alignYaw);
            }

            for (int i = used; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        private bool EnsureTiles(DynamicMap map, Vector2 mapSize)
        {
            if (mapSize.x < 1f || mapSize.y < 1f)
                return false;

            LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings? settings = level != null ? level.LoadedMapSettings : null;
            if (settings != null)
            {
                _gridSizeX = settings.GridSizeX;
                _gridSizeY = settings.GridSizeY;
            }

            if (_gridSizeX < MajorsPerTile || _gridSizeY < MajorsPerTile)
                return false;

            if (!_cached)
            {
                _tiles.Clear();
                _sprite = ResolveSprite(map);
                if (_sprite == null)
                    return false;

                Transform root = map.gridLabels.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null || !child.name.StartsWith("mapGrid_", StringComparison.Ordinal))
                        continue;
                    if (!TryParseTileName(child.name, out int m, out int n))
                        continue;
                    _tiles.Add(new TileInfo { M = m, N = n });
                }

                _cached = _tiles.Count > 0;
            }

            if (!_cached || _sprite == null)
                return false;

            int tilesX = Mathf.Max(1, _gridSizeX / MajorsPerTile);
            int tilesY = Mathf.Max(1, _gridSizeY / MajorsPerTile);
            _tileSizeX = mapSize.x / tilesX;
            _tileSizeZ = mapSize.y / tilesY;
            float halfX = mapSize.x * 0.5f;
            float halfZ = mapSize.y * 0.5f;

            for (int i = 0; i < _tiles.Count; i++)
            {
                TileInfo tile = _tiles[i];
                tile.CenterGx = -halfX + (tile.M + 0.5f) * _tileSizeX;
                tile.CenterGz = halfZ - (tile.N + 0.5f) * _tileSizeZ;
            }

            return true;
        }

        private static Sprite? ResolveSprite(DynamicMap map)
        {
            if (map.gridLabels.gridImage_prefab != null)
            {
                Image? img = map.gridLabels.gridImage_prefab.GetComponent<Image>();
                if (img == null)
                    img = map.gridLabels.gridImage_prefab.GetComponentInChildren<Image>(true);
                if (img != null && img.sprite != null)
                    return img.sprite;
            }

            Transform root = map.gridLabels.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || !child.name.StartsWith("mapGrid_", StringComparison.Ordinal))
                    continue;
                Image? img = child.GetComponent<Image>();
                if (img != null && img.sprite != null)
                    return img.sprite;
            }

            return null;
        }

        private static bool TryParseTileName(string name, out int m, out int n)
        {
            m = 0;
            n = 0;
            if (!name.StartsWith("mapGrid_", StringComparison.Ordinal))
                return false;
            string tail = name.Substring("mapGrid_".Length);
            int sep = tail.IndexOf('_');
            if (sep <= 0 || sep >= tail.Length - 1)
                return false;
            return int.TryParse(tail.Substring(0, sep), out m)
                && int.TryParse(tail.Substring(sep + 1), out n);
        }

        private static Color ResolveColor(float opacity)
        {
            Color c = new Color(0.75f, 0.78f, 0.82f, opacity);
            try
            {
                if (ThemeManager.Active != null && ThemeManager.Active.ColorTheme != null)
                {
                    c = ThemeManager.Active.ColorTheme.MapBackground;
                    c.a = opacity;
                }
            }
            catch
            {
                // Theme may be unavailable during load.
            }

            return c;
        }

        private TileSlot Get(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("StockGridTile");
                go.layer = MapTiltEngine.Layer;
                go.transform.SetParent(_pivot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = _mat;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                _pool.Add(new TileSlot(go, sr));
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
            _tiles.Clear();
            if (_mat != null)
            {
                UnityEngine.Object.Destroy(_mat);
                _mat = null;
            }
        }
    }
}

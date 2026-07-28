using System;
using Map3d.Config;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Single flat stock mapGrid quad on the cloth canvas — same world UV footprint as the cloth,
    /// map-geography locked, drawn over the opaque base (no relief, no extra rotation).
    /// </summary>
    internal sealed class StockClothGrid : IDisposable
    {
        private const int MajorsPerTile = 4;
        private const float LocalYOffset = 0.004f;
        private const int GridAniso = 4;

        private readonly Transform _root;
        private readonly MeshFilter _filter;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly Vector3[] _verts =
        {
            new Vector3(-0.5f, LocalYOffset, -0.5f),
            new Vector3( 0.5f, LocalYOffset, -0.5f),
            new Vector3(-0.5f, LocalYOffset,  0.5f),
            new Vector3( 0.5f, LocalYOffset,  0.5f)
        };
        private readonly Vector2[] _uvs = new Vector2[4];
        private readonly int[] _tris = { 0, 2, 1, 1, 2, 3 };

        private Texture2D? _tex;
        private float _tileSizeMeters = 40000f;
        private Vector2 _mapSize;
        private bool _ready;

        internal StockClothGrid(Transform clothCanvas)
        {
            var go = new GameObject("StockClothGrid");
            go.layer = MapTiltEngine.Layer;
            go.transform.SetParent(clothCanvas, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _root = go.transform;

            _filter = go.AddComponent<MeshFilter>();
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;

            _mesh = new Mesh
            {
                name = "Map3d.StockClothGridMesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            _mesh.vertices = _verts;
            _mesh.triangles = _tris;
            _filter.sharedMesh = _mesh;

            Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            _material = new Material(sh!)
            {
                name = "Map3d.StockClothGridMat",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3001
            };
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            _renderer.sharedMaterial = _material;
            go.SetActive(false);
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
                SetActive(false);
                return;
            }

            _mapSize = mapSize;
            if (!EnsureTexture(map, mapSize))
            {
                SetActive(false);
                return;
            }

            SetCornerUv(0, aircraftPos, forward, right, clothNear, clothFar, clothHalfW, 0f, 0f);
            SetCornerUv(1, aircraftPos, forward, right, clothNear, clothFar, clothHalfW, 1f, 0f);
            SetCornerUv(2, aircraftPos, forward, right, clothNear, clothFar, clothHalfW, 0f, 1f);
            SetCornerUv(3, aircraftPos, forward, right, clothNear, clothFar, clothHalfW, 1f, 1f);

            _mesh.uv = _uvs;
            ApplyColor(Mathf.Clamp01(Map3dConfig.GridOpacity.Value));
            SetActive(true);
        }

        private void SetCornerUv(
            int index,
            Vector3 aircraftPos,
            Vector3 forward,
            Vector3 right,
            float clothNear,
            float clothFar,
            float clothHalfW,
            float u,
            float v)
        {
            float lx = (u - 0.5f) * 2f * clothHalfW;
            float lz = Mathf.Lerp(clothNear, clothFar, v);
            Vector3 world = aircraftPos + right * lx + forward * lz;
            GlobalPosition gp = world.ToGlobalPosition();
            float halfX = _mapSize.x * 0.5f;
            float halfZ = _mapSize.y * 0.5f;
            _uvs[index] = new Vector2(
                (gp.x + halfX) / _tileSizeMeters,
                (gp.z + halfZ) / _tileSizeMeters);
        }

        private bool EnsureTexture(DynamicMap map, Vector2 mapSize)
        {
            if (_ready && _tex != null)
                return true;

            Sprite? sprite = ResolveSprite(map);
            if (sprite == null || sprite.texture == null)
                return false;

            Texture2D? baked = BakeSprite(sprite);
            if (baked == null)
                return false;

            if (_tex != null)
                UnityEngine.Object.Destroy(_tex);

            _tex = baked;
            _tex.wrapMode = TextureWrapMode.Repeat;
            _tex.filterMode = FilterMode.Trilinear;
            _tex.anisoLevel = GridAniso;
            _material.mainTexture = _tex;
            if (_material.HasProperty("_MainTex"))
                _material.SetTexture("_MainTex", _tex);

            LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings? settings = level != null ? level.LoadedMapSettings : null;
            if (settings != null && settings.GridSizeX >= MajorsPerTile && mapSize.x > 1f)
                _tileSizeMeters = mapSize.x / Mathf.Max(1, settings.GridSizeX / MajorsPerTile);
            else if (mapSize.x > 1f)
                _tileSizeMeters = mapSize.x / 2f;

            _ready = true;
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

        private static Texture2D? BakeSprite(Sprite sprite)
        {
            Texture2D src = sprite.texture;
            if (src == null)
                return null;

            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "Map3d.StockGridTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = GridAniso
            };

            try
            {
                RenderTexture prev = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                dst.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                // Keep only line texels — stock grid fill would wash the opaque cloth underneath.
                Color[] px = dst.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    Color p = px[i];
                    float lum = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                    if (p.a < 0.2f || lum < 0.08f)
                        px[i] = new Color(0f, 0f, 0f, 0f);
                    else
                        px[i] = new Color(p.r, p.g, p.b, 1f);
                }

                dst.SetPixels(px);
                dst.Apply(true, false);
                return dst;
            }
            catch
            {
                UnityEngine.Object.Destroy(dst);
                return null;
            }
        }

        private void ApplyColor(float opacity)
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

            _material.color = c;
            if (_material.HasProperty("_Color"))
                _material.SetColor("_Color", c);
        }

        private void SetActive(bool on)
        {
            if (_root.gameObject.activeSelf != on)
                _root.gameObject.SetActive(on);
        }

        public void Dispose()
        {
            if (_mesh != null)
                UnityEngine.Object.Destroy(_mesh);
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_tex != null)
            {
                UnityEngine.Object.Destroy(_tex);
                _tex = null;
            }
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}

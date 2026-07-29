using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Map3d.Engine
{
    /// <summary>
    /// Shared sprite/TextMesh setup so overlays stay alpha-cut (no black quad behind icons).
    /// </summary>
    internal static class ClothSpriteUtil
    {
        /// <summary>Only near-pure black (dash gaps). Soft grays / icon darks stay intact.</summary>
        private const float BlackKeyMaxChannel = 0.04f;
        private const float BlackKeyMinAlpha = 0.55f;

        private static Material? _spriteTemplate;
        private static readonly Dictionary<int, Sprite> LineSpriteCache = new Dictionary<int, Sprite>(32);
        private static readonly List<Object> Owned = new List<Object>(32);

        internal static Material CreateTransparentSpriteMaterial(string name, int renderQueue)
        {
            Material mat = new Material(ResolveSpriteTemplate())
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = renderQueue
            };

            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", (int)CullMode.Off);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            return mat;
        }

        /// <summary>
        /// Notch dash textures paint gaps as opaque black (UI Image hides them).
        /// Only for notch — do not run on icons/cone/markers.
        /// </summary>
        internal static Sprite GetBlackKeyedSprite(Sprite? src)
        {
            if (src == null || src.texture == null)
                return src!;

            int id = src.GetInstanceID();
            if (LineSpriteCache.TryGetValue(id, out Sprite? cached) && cached != null)
                return cached;

            Texture2D? baked = BakeBlackKeyed(src);
            if (baked == null)
            {
                LineSpriteCache[id] = src;
                return src;
            }

            Owned.Add(baked);
            float rw = Mathf.Max(1f, src.rect.width);
            float rh = Mathf.Max(1f, src.rect.height);
            var pivot = new Vector2(src.pivot.x / rw, src.pivot.y / rh);
            Sprite sp = Sprite.Create(
                baked,
                new Rect(0f, 0f, baked.width, baked.height),
                pivot,
                Mathf.Max(0.01f, src.pixelsPerUnit),
                0u,
                SpriteMeshType.FullRect);
            sp.name = src.name + "_Map3dAlpha";
            sp.hideFlags = HideFlags.HideAndDontSave;
            Owned.Add(sp);
            LineSpriteCache[id] = sp;
            return sp;
        }

        private static Texture2D? BakeBlackKeyed(Sprite sprite)
        {
            Texture2D src = sprite.texture;
            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Map3d.KeyedSprite",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
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

                Color[] px = dst.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    Color p = px[i];
                    float maxC = p.r > p.g ? (p.r > p.b ? p.r : p.b) : (p.g > p.b ? p.g : p.b);
                    if (p.a >= BlackKeyMinAlpha && maxC <= BlackKeyMaxChannel)
                        px[i] = new Color(0f, 0f, 0f, 0f);
                }

                dst.SetPixels(px);
                dst.Apply(false, true);
                return dst;
            }
            catch
            {
                Object.Destroy(dst);
                return null;
            }
        }

        private static Material ResolveSpriteTemplate()
        {
            if (_spriteTemplate != null)
                return _spriteTemplate;

            var probe = new GameObject("Map3d.SpriteMatProbe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            var sr = probe.AddComponent<SpriteRenderer>();
            Material? live = sr.sharedMaterial;
            if (live != null)
            {
                _spriteTemplate = new Material(live)
                {
                    name = "Map3d.SpriteTemplate",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                Shader? sh = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                             ?? Shader.Find("Unlit/Transparent");
                _spriteTemplate = new Material(sh!)
                {
                    name = "Map3d.SpriteTemplate",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            Object.Destroy(probe);
            return _spriteTemplate;
        }

        internal static void SetupTextMesh(TextMesh tm)
        {
            if (tm == null)
                return;

            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                return;

            tm.font = font;
            MeshRenderer? mr = tm.GetComponent<MeshRenderer>();
            if (mr != null && font.material != null)
            {
                var mat = new Material(font.material)
                {
                    name = "Map3d.TextMeshFont",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 3201
                };
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetInt("_ZWrite", 0);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                Owned.Add(mat);
            }
        }

        internal static void ConfigureSpriteRenderer(SpriteRenderer sr, Material? mat)
        {
            if (sr == null)
                return;
            if (mat != null)
                sr.sharedMaterial = mat;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.drawMode = SpriteDrawMode.Simple;
        }

        internal static void DisposeOwned()
        {
            LineSpriteCache.Clear();
            for (int i = 0; i < Owned.Count; i++)
            {
                if (Owned[i] != null)
                    Object.Destroy(Owned[i]);
            }
            Owned.Clear();
            if (_spriteTemplate != null)
            {
                Object.Destroy(_spriteTemplate);
                _spriteTemplate = null;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace Map3d.Engine
{
    /// <summary>
    /// Shared sprite/TextMesh setup so overlays stay alpha-cut (no black quad behind icons).
    /// </summary>
    internal static class ClothSpriteUtil
    {
        private static Material? _spriteTemplate;

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
            mat.EnableKeyword("ETC1_EXTERNAL_ALPHA");

            return mat;
        }

        private static Material ResolveSpriteTemplate()
        {
            if (_spriteTemplate != null)
                return _spriteTemplate;

            // Clone Unity's live default SpriteRenderer material (correct blend/atlas support).
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
    }
}

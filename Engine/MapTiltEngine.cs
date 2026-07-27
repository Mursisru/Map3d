using System;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Tilts stock MapImage cloth; unit icons + view cone on the same pivot (Layer 31).
    /// Geography UVs are heading-aligned so position matches the real world / stock map.
    /// </summary>
    internal sealed class MapTiltEngine : IDisposable
    {
        internal const int Layer = 31;

        private GameObject? _root;
        private Transform? _yaw;
        private Transform? _tilt;
        private Transform? _canvas;
        private Camera? _cam;
        private RenderTexture? _rt;
        private MeshFilter? _filter;
        private MeshRenderer? _renderer;
        private Mesh? _mesh;
        private Material? _mat;
        private ClothIconLayer? _icons;
        private int _rtSize;
        private float _radius;
        private float _lookAhead;
        private Vector2 _mapSize;
        private Vector4 _uvRect;
        private bool _built;

        internal RenderTexture? Output => _rt;

        internal static MapTiltEngine Create()
        {
            var e = new MapTiltEngine();
            e.Build();
            return e;
        }

        private void Build()
        {
            _root = new GameObject("Map3d.TiltEngine");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.transform.position = new Vector3(0f, -40000f, 0f);
            SetLayer(_root);

            _yaw = Child(_root.transform, "Yaw");
            _tilt = Child(_yaw, "Pivot");
            _canvas = Child(_tilt, "Cloth");
            _icons = new ClothIconLayer(_tilt);

            var camGo = Child(_root.transform, "Cam").gameObject;
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -120;
            _cam.enabled = false;
            EnsureRt(Map3dConfig.RenderSize.Value);
            _cam.targetTexture = _rt;

            _filter = _canvas.gameObject.AddComponent<MeshFilter>();
            _renderer = _canvas.gameObject.AddComponent<MeshRenderer>();
            _mesh = CreateFlatQuad();
            _filter.sharedMesh = _mesh;
            _mat = CreateMapMaterial();
            _renderer.sharedMaterial = _mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;

            var lightGo = Child(_yaw, "Light").gameObject;
            lightGo.transform.localRotation = Quaternion.Euler(55f, -25f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;

            _built = true;
        }

        /// <summary>Render tilted stock map cloth for own aircraft pose (CombatHUD.aircraft).</summary>
        internal bool Tick(Aircraft? ownAircraft)
        {
            if (!_built || _cam == null || _tilt == null || _yaw == null || _canvas == null || _mesh == null)
                return false;

            DynamicMap? map = SceneSingleton<DynamicMap>.i;
            if (map == null)
                return false;

            if (!TryResolveStock(map, out Sprite? sprite, out Color tint, out Vector2 mapSize))
                return false;

            if (ownAircraft == null || ownAircraft.disabled)
                return false;

            Vector3 aircraft = ownAircraft.transform.position;
            Vector3 forward = ownAircraft.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            float aircraftYaw = ownAircraft.transform.eulerAngles.y;

            _radius = StockMapMetrics.ResolveRadius(map);
            _lookAhead = Mathf.Max(0f, Map3dConfig.LookAheadMeters.Value);
            _mapSize = mapSize;
            ResolveUvRect(sprite, out Texture? tex, out _uvRect);
            if (tex == null)
                return false;

            EnsureRt(Map3dConfig.RenderSize.Value);
            _cam.targetTexture = _rt;
            _cam.backgroundColor = Color.black;

            // Heading-up like stock: +Z = aircraft forward.
            _yaw.rotation = Quaternion.LookRotation(forward, Vector3.up);

            float size = _radius * 2f;
            // Stock look-ahead: cloth center ahead of aircraft; aircraft at pivot (0).
            _canvas.localPosition = new Vector3(0f, 0f, _lookAhead);
            _canvas.localRotation = Quaternion.identity;
            _canvas.localScale = new Vector3(size, 1f, size);

            ApplyHeadingAlignedUvs(tex, tint, aircraft, forward);

            float tilt = Mathf.Clamp(Map3dConfig.TiltDegrees.Value, 0f, 80f);
            _tilt.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            _tilt.localPosition = Vector3.zero;

            FrameClothCamera();
            _icons?.Sync(map, ownAircraft, aircraft, forward, aircraftYaw, _radius, _cam);

            _cam.enabled = true;
            return true;
        }

        internal void SetActive(bool on)
        {
            if (_cam != null)
                _cam.enabled = on;
        }

        /// <summary>
        /// UV each cloth corner from its real world XZ (heading-aligned window).
        /// Fixes "not at airport" caused by axis-aligned UV on a rotated cloth.
        /// </summary>
        private void ApplyHeadingAlignedUvs(Texture tex, Color tint, Vector3 aircraft, Vector3 forward)
        {
            if (_mat == null || _mesh == null)
                return;

            _mat.mainTexture = tex;
            _mat.SetTexture("_MainTex", tex);
            _mat.color = tint;
            if (_mat.HasProperty("_Color"))
                _mat.SetColor("_Color", tint);

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            // Canvas local: center at aircraft+forward*lookAhead, extents ±radius on right/forward.
            Vector3 center = aircraft + forward * _lookAhead;
            // Quad verts order: (-0.5,-0.5), (+0.5,-0.5), (-0.5,+0.5), (+0.5,+0.5) in (x,z)
            Vector3[] corners =
            {
                center + right * (-_radius) + forward * (-_radius),
                center + right * ( _radius) + forward * (-_radius),
                center + right * (-_radius) + forward * ( _radius),
                center + right * ( _radius) + forward * ( _radius)
            };

            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
                uvs[i] = WorldToMapUv(corners[i]);

            _mesh.uv = uvs;
        }

        private Vector2 WorldToMapUv(Vector3 worldLocal)
        {
            GlobalPosition gp = worldLocal.ToGlobalPosition();
            float u01 = (gp.x + _mapSize.x * 0.5f) / _mapSize.x;
            float v01 = (gp.z + _mapSize.y * 0.5f) / _mapSize.y;
            u01 = Mathf.Clamp01(u01);
            v01 = Mathf.Clamp01(v01);
            return new Vector2(_uvRect.x + u01 * _uvRect.z, _uvRect.y + v01 * _uvRect.w);
        }

        private void FrameClothCamera()
        {
            float r = _radius;
            // No Min(0.8) floor — that kept the camera unnaturally far from stock.
            float height = r * Mathf.Clamp(Map3dConfig.ViewHeightScale.Value, 0.35f, 3f);
            float back = r * Mathf.Clamp(Map3dConfig.ViewBackScale.Value, 0f, 0.5f);
            float look = r * Mathf.Clamp(Map3dConfig.ViewLookScale.Value, 0f, 1f);

            Vector3 pivot = _tilt!.position;
            Vector3 forward = _yaw!.forward;
            Vector3 up = _yaw.up;

            Vector3 camPos = pivot - forward * back + up * height;
            Vector3 lookAt = _tilt.TransformPoint(new Vector3(0f, 0f, look));

            _cam!.transform.SetParent(_root!.transform, true);
            _cam.transform.position = camPos;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - camPos, forward);
            _cam.fieldOfView = Mathf.Clamp(Map3dConfig.ViewFov.Value, 20f, 75f);
            _cam.nearClipPlane = Mathf.Max(1f, height * 0.02f);
            _cam.farClipPlane = Mathf.Max(50000f, r * 8f);
        }

        private static bool TryResolveStock(DynamicMap map, out Sprite? sprite, out Color tint, out Vector2 mapSize)
        {
            sprite = null;
            tint = Color.white;
            mapSize = new Vector2(81920f, 81920f);

            LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings? settings = level != null ? level.LoadedMapSettings : null;
            if (settings != null)
            {
                mapSize = settings.MapSize;
                sprite = settings.MapImage;
            }

            if (map.mapImage != null)
            {
                Image? img = map.mapImage.GetComponent<Image>();
                if (img != null)
                {
                    tint = img.color;
                    if (sprite == null)
                        sprite = img.sprite;
                }
            }

            return sprite != null && sprite.texture != null && mapSize.x > 1f;
        }

        private static void ResolveUvRect(Sprite? sprite, out Texture? tex, out Vector4 rect)
        {
            tex = null;
            rect = new Vector4(0f, 0f, 1f, 1f);
            if (sprite == null)
                return;
            tex = sprite.texture;
            if (tex == null)
                return;
            Rect tr = sprite.textureRect;
            float tw = Mathf.Max(1f, tex.width);
            float th = Mathf.Max(1f, tex.height);
            rect = new Vector4(tr.x / tw, tr.y / th, tr.width / tw, tr.height / th);
        }

        private static Mesh CreateFlatQuad()
        {
            var mesh = new Mesh { name = "Map3d.Cloth" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateMapMaterial()
        {
            Shader? shader = Shader.Find("UI/Default")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Sprites/Default");
            return new Material(shader!)
            {
                name = "Map3d.ClothMat",
                hideFlags = HideFlags.HideAndDontSave,
                color = Color.white
            };
        }

        private void EnsureRt(int size)
        {
            size = Mathf.Clamp(size, 128, 2048);
            if (_rt != null && _rtSize == size)
                return;
            _rtSize = size;
            if (_cam != null)
                _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
            _rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "Map3d.RT",
                filterMode = FilterMode.Bilinear
            };
            _rt.Create();
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SetLayer(go);
            return go.transform;
        }

        private static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayer(go.transform.GetChild(i).gameObject);
        }

        public void Dispose()
        {
            _icons?.Dispose();
            _icons = null;
            if (_cam != null)
            {
                _cam.targetTexture = null;
                _cam.enabled = false;
            }
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
            if (_mesh != null)
            {
                UnityEngine.Object.Destroy(_mesh);
                _mesh = null;
            }
            if (_mat != null)
            {
                UnityEngine.Object.Destroy(_mat);
                _mat = null;
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _cam = null;
            _built = false;
        }
    }
}

using System;
using Map3d.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Map3d.Engine
{
    /// <summary>
    /// Tilts stock MapImage cloth with optional terrain height displacement from HeightMapCache.
    /// Geography UVs are heading-aligned around stock look-ahead center; icons/cone share the cloth pivot.
    /// </summary>
    internal sealed class MapTiltEngine : IDisposable
    {
        internal const int Layer = 31;
        private const float IconLiftMeters = 4f;
        private const float IconLiftHeightScale = 120f;
        private const float DiagonalBorderFactor = 1.41421356f;
        private const float ExtentCommitMeters = 400f;
        private const float ExtentCommitRel = 0.04f;
        private const float ClothResRefSpanMeters = 20000f;

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
        private ClothObjectiveLayer? _objectives;
        private ClothRadarLayer? _radar;
        private StockClothGrid? _grid;
        private Vector3[] _verts = Array.Empty<Vector3>();
        private Vector2[] _uvs = Array.Empty<Vector2>();
        private int[] _tris = Array.Empty<int>();
        private int _meshRes;
        private int _rtSize;
        private int _rtMsaa;
        private RenderTexture? _softMap;
        private Texture? _softMapSrc;
        private int _softMapId;
        private float _radius;
        private float _lookAhead;
        private float _clothNear;
        private float _clothFar;
        private float _clothHalfW;
        private float _heightScaleMeters;
        private Vector2 _mapSize;
        private Vector4 _uvRect;
        private bool _built;
        private bool _extentsInit;
        private Vector3 _extentCommitPos;

        internal RenderTexture? Output => _rt;
        internal float HeightScaleMeters => _heightScaleMeters;

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
            _objectives = new ClothObjectiveLayer(_tilt);
            _radar = new ClothRadarLayer(_tilt);
            _grid = new StockClothGrid(_canvas);

            var camGo = Child(_root.transform, "Cam").gameObject;
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -120;
            _cam.enabled = false;
            EnsureRt(Map3dConfig.RenderSize.Value, Map3dConfig.RenderMsaa.Value);
            _cam.targetTexture = _rt;

            _filter = _canvas.gameObject.AddComponent<MeshFilter>();
            _renderer = _canvas.gameObject.AddComponent<MeshRenderer>();
            EnsureGridMesh(Map3dConfig.HeightClothResolution.Value);
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

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            float aircraftYaw = ownAircraft.transform.eulerAngles.y;

            _radius = StockMapMetrics.ResolveRadius(map);
            _lookAhead = Mathf.Max(0f, Map3dConfig.LookAheadMeters.Value);
            _mapSize = mapSize;
            Vector3 mapCenter = aircraft + forward * _lookAhead;
            ResolveClothExtents(aircraft);
            if (!EnsureSoftMap(sprite, out Texture tex))
                return false;

            EnsureRt(Map3dConfig.RenderSize.Value, Map3dConfig.RenderMsaa.Value);
            _cam.targetTexture = _rt;
            _cam.backgroundColor = Color.black;

            _yaw.rotation = Quaternion.LookRotation(forward, Vector3.up);

            float width = _clothHalfW * 2f;
            float depth = Mathf.Max(1f, _clothFar - _clothNear);
            // Shift canvas so mesh geometric Z matches ClothWorld offsets around mapCenter.
            _canvas.localPosition = new Vector3(0f, 0f, _lookAhead + (_clothNear + _clothFar) * 0.5f);
            _canvas.localRotation = Quaternion.identity;
            _canvas.localScale = new Vector3(width, 1f, depth);

            HeightMapCache cache = HeightMapCache.Instance;
            bool heightOn = Map3dConfig.HeightEnabled.Value && cache.IsReady;
            int baseRes = Mathf.Clamp(Map3dConfig.HeightClothResolution.Value, 8, 96);
            float span = Mathf.Max(_clothFar - _clothNear, _clothHalfW * 2f, 1f);
            int res = heightOn
                ? Mathf.Clamp(Mathf.RoundToInt(baseRes * span / ClothResRefSpanMeters), baseRes, 96)
                : 2;
            EnsureGridMesh(res);

            ApplyMaterial(tex, tint);

            if (heightOn)
            {
                _heightScaleMeters = cache.ResolveHeightScaleMeters(_radius);
                ApplyDisplacedMesh(mapCenter, forward, right, cache);
            }
            else
            {
                _heightScaleMeters = 0f;
                ApplyFlatUvs(mapCenter, forward, right);
            }

            float tilt = Mathf.Clamp(Map3dConfig.TiltDegrees.Value, 0f, 80f);
            _tilt.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            _tilt.localPosition = Vector3.zero;

            FrameClothCamera(cache);
            float clothZNear = _lookAhead + _clothNear;
            float clothZFar = _lookAhead + _clothFar;
            // Lift must clear mesh bilinear error: heightScale * Δh can bury icons under opaque cloth.
            float iconLift = Mathf.Max(
                IconLiftMeters,
                _heightScaleMeters * IconLiftHeightScale + 25f);
            _icons?.Sync(
                map,
                ownAircraft,
                aircraft,
                forward,
                aircraftYaw,
                _radius,
                clothZNear,
                clothZFar,
                _clothHalfW,
                _cam,
                cache,
                _heightScaleMeters,
                iconLift);

            _objectives?.Sync(
                map,
                aircraft,
                forward,
                _radius,
                clothZNear,
                clothZFar,
                _clothHalfW,
                _cam,
                cache,
                _heightScaleMeters,
                iconLift);

            _radar?.Sync(
                map,
                ownAircraft,
                aircraft,
                forward,
                _radius,
                clothZNear,
                clothZFar,
                _clothHalfW,
                _cam,
                cache,
                _heightScaleMeters,
                iconLift);

            bool showGrid = SceneSingleton<MapOptions>.i == null || SceneSingleton<MapOptions>.i.showGridLabels;
            _grid?.Sync(
                map,
                mapCenter,
                forward,
                right,
                _clothNear,
                _clothFar,
                _clothHalfW,
                _mapSize,
                showGrid);

            _cam.enabled = true;
            return true;
        }

        internal void SetActive(bool on)
        {
            if (_cam != null)
                _cam.enabled = on;
        }

        private void ApplyMaterial(Texture tex, Color tint)
        {
            if (_mat == null)
                return;
            tint.a = 1f;
            float boost = Mathf.Clamp(Map3dConfig.MapBrightness.Value, 0.5f, 2.5f);
            tint.r = Mathf.Clamp01(tint.r * boost);
            tint.g = Mathf.Clamp01(tint.g * boost);
            tint.b = Mathf.Clamp01(tint.b * boost);
            _mat.mainTexture = tex;
            _mat.SetTexture("_MainTex", tex);
            _mat.color = tint;
            if (_mat.HasProperty("_Color"))
                _mat.SetColor("_Color", tint);
        }

        private void ResolveClothExtents(Vector3 aircraftPos)
        {
            float r = Mathf.Max(500f, _radius);
            float farScale = Mathf.Clamp(Map3dConfig.HorizonFarScale.Value, 1f, 12f);
            float nearScale = Mathf.Clamp(Map3dConfig.HorizonNearScale.Value, 0.1f, 4f);
            float sideScale = Mathf.Clamp(Map3dConfig.HorizonSideScale.Value, 0.5f, 4f);

            CardinalReach(aircraftPos, out float reachX, out float reachZ);
            float borderCardinal = Mathf.Max(50f, reachX, reachZ);
            float horizonFar = r * farScale - _lookAhead;
            float borderForward = Mathf.Max(50f, borderCardinal * DiagonalBorderFactor - _lookAhead);
            float targetFar = Mathf.Max(horizonFar, borderForward);
            float targetHalfW = Mathf.Max(r * sideScale, borderCardinal);
            float targetNear = -r * nearScale;

            // Near always tracks radius (yaw-stable); far/halfW freeze until flight moves enough.
            _clothNear = targetNear;

            if (!_extentsInit)
            {
                CommitExtents(aircraftPos, targetFar, targetHalfW);
                return;
            }

            float movedSq = (aircraftPos - _extentCommitPos).sqrMagnitude;
            bool movedFar = movedSq >= ExtentCommitMeters * ExtentCommitMeters;
            bool farChanged = RelDelta(_clothFar, targetFar) > ExtentCommitRel;
            bool halfChanged = RelDelta(_clothHalfW, targetHalfW) > ExtentCommitRel;
            if (movedFar || farChanged || halfChanged)
                CommitExtents(aircraftPos, targetFar, targetHalfW);
        }

        private void CommitExtents(Vector3 aircraftPos, float far, float halfW)
        {
            _clothFar = far;
            _clothHalfW = halfW;
            _extentCommitPos = aircraftPos;
            _extentsInit = true;
        }

        private static float RelDelta(float current, float target)
        {
            float denom = Mathf.Max(Mathf.Abs(current), 1f);
            return Mathf.Abs(target - current) / denom;
        }

        private void CardinalReach(Vector3 originLocal, out float reachX, out float reachZ)
        {
            if (_mapSize.x < 1f || _mapSize.y < 1f)
            {
                float fallback = Mathf.Max(500f, _radius) * 2f;
                reachX = fallback;
                reachZ = fallback;
                return;
            }

            GlobalPosition g = originLocal.ToGlobalPosition();
            float hx = _mapSize.x * 0.5f;
            float hz = _mapSize.y * 0.5f;
            reachX = hx - Mathf.Abs(g.x);
            reachZ = hz - Mathf.Abs(g.z);
        }

        private Vector3 ClothWorld(Vector3 origin, Vector3 forward, Vector3 right, float u, float v)
        {
            float x = (u - 0.5f) * 2f * _clothHalfW;
            float z = Mathf.Lerp(_clothNear, _clothFar, v);
            return origin + right * x + forward * z;
        }

        private void ApplyFlatUvs(Vector3 origin, Vector3 forward, Vector3 right)
        {
            if (_mesh == null || _uvs.Length < 4)
                return;

            _uvs[0] = WorldToMapUv(ClothWorld(origin, forward, right, 0f, 0f));
            _uvs[1] = WorldToMapUv(ClothWorld(origin, forward, right, 1f, 0f));
            _uvs[2] = WorldToMapUv(ClothWorld(origin, forward, right, 0f, 1f));
            _uvs[3] = WorldToMapUv(ClothWorld(origin, forward, right, 1f, 1f));
            _verts[0] = new Vector3(-0.5f, 0f, -0.5f);
            _verts[1] = new Vector3( 0.5f, 0f, -0.5f);
            _verts[2] = new Vector3(-0.5f, 0f,  0.5f);
            _verts[3] = new Vector3( 0.5f, 0f,  0.5f);
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            // Unlit — skip RecalculateNormals; fixed unit-cube bounds (Y relief stays inside).
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.2f, 4f, 1.2f));
        }

        private void ApplyDisplacedMesh(
            Vector3 origin,
            Vector3 forward,
            Vector3 right,
            HeightMapCache cache)
        {
            if (_mesh == null || _meshRes < 2)
                return;

            int n = _meshRes;
            float step = 1f / (n - 1);
            float sea = cache.SeaY;
            float scale = _heightScaleMeters;
            float maxAbsY = 0.5f;

            for (int iz = 0; iz < n; iz++)
            {
                float v = iz * step;
                for (int ix = 0; ix < n; ix++)
                {
                    float u = ix * step;
                    int idx = iz * n + ix;
                    Vector3 world = ClothWorld(origin, forward, right, u, v);
                    _uvs[idx] = WorldToMapUv(world);

                    float h = sea;
                    if (!cache.TrySampleWorld(world, out h))
                        h = sea;
                    float yLocal = (h - sea) * scale;
                    float absY = Mathf.Abs(yLocal);
                    if (absY > maxAbsY)
                        maxAbsY = absY;
                    _verts[idx] = new Vector3(u - 0.5f, yLocal, v - 0.5f);
                }
            }

            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.2f, maxAbsY * 2.2f + 0.5f, 1.2f));
        }

        private Vector2 WorldToMapUv(Vector3 worldLocal)
        {
            GlobalPosition gp = worldLocal.ToGlobalPosition();
            // No Clamp01 — GPU Clamp wrap on softMap handles OOB per-fragment (avoids vertex UV pile-up).
            float u01 = (gp.x + _mapSize.x * 0.5f) / _mapSize.x;
            float v01 = (gp.z + _mapSize.y * 0.5f) / _mapSize.y;
            return new Vector2(_uvRect.x + u01 * _uvRect.z, _uvRect.y + v01 * _uvRect.w);
        }

        private void EnsureGridMesh(int resolution)
        {
            resolution = Mathf.Clamp(resolution, 2, 96);
            if (_mesh != null && _meshRes == resolution && _verts.Length == resolution * resolution)
            {
                if (_filter != null && _filter.sharedMesh != _mesh)
                    _filter.sharedMesh = _mesh;
                return;
            }

            _meshRes = resolution;
            int n = resolution;
            int vertCount = n * n;
            _verts = new Vector3[vertCount];
            _uvs = new Vector2[vertCount];
            int quadCount = (n - 1) * (n - 1);
            _tris = new int[quadCount * 6];

            float step = n > 1 ? 1f / (n - 1) : 0f;
            for (int iz = 0; iz < n; iz++)
            {
                float v = iz * step;
                for (int ix = 0; ix < n; ix++)
                {
                    float u = ix * step;
                    int idx = iz * n + ix;
                    _verts[idx] = new Vector3(u - 0.5f, 0f, v - 0.5f);
                    _uvs[idx] = new Vector2(u, v);
                }
            }

            int t = 0;
            for (int iz = 0; iz < n - 1; iz++)
            {
                for (int ix = 0; ix < n - 1; ix++)
                {
                    int i0 = iz * n + ix;
                    int i1 = i0 + 1;
                    int i2 = i0 + n;
                    int i3 = i2 + 1;
                    _tris[t++] = i0;
                    _tris[t++] = i2;
                    _tris[t++] = i1;
                    _tris[t++] = i1;
                    _tris[t++] = i2;
                    _tris[t++] = i3;
                }
            }

            if (_mesh != null)
                UnityEngine.Object.Destroy(_mesh);

            _mesh = new Mesh
            {
                name = "Map3d.ClothGrid",
                indexFormat = vertCount > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.triangles = _tris;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_filter != null)
                _filter.sharedMesh = _mesh;
        }

        private void FrameClothCamera(HeightMapCache cache)
        {
            float r = _radius;
            float height = r * Mathf.Clamp(Map3dConfig.ViewHeightScale.Value, 0.35f, 3f);
            float back = r * Mathf.Clamp(Map3dConfig.ViewBackScale.Value, 0f, 0.5f);
            float lookFrac = Mathf.Clamp(Map3dConfig.ViewLookScale.Value, 0f, 1f);
            float look = Mathf.Lerp(0f, Mathf.Max(_lookAhead, r * 1.2f), lookFrac + 0.35f);
            look = Mathf.Clamp(look, r * 0.15f, Mathf.Max(_lookAhead + r, r * 2f));

            Vector3 pivot = _tilt!.position;
            Vector3 forward = _yaw!.forward;
            Vector3 up = _yaw.up;

            float relief = Mathf.Max(0f, _heightScaleMeters)
                * Mathf.Max(0f, cache.MaxY - cache.SeaY);
            height += relief * 0.35f;

            Vector3 camPos = pivot - forward * back + up * height;
            Vector3 lookAt = _tilt.TransformPoint(new Vector3(0f, 0f, look));

            _cam!.transform.SetParent(_root!.transform, true);
            _cam.transform.position = camPos;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - camPos, forward);
            _cam.orthographic = false;
            _cam.fieldOfView = Mathf.Clamp(Map3dConfig.ViewFov.Value, 20f, 75f);
            _cam.nearClipPlane = Mathf.Max(1f, height * 0.02f);
            _cam.farClipPlane = Mathf.Max(80000f, (_clothFar + _lookAhead) * 4f, r * 12f);
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

        /// <summary>
        /// Blit stock MapImage sprite rect into a mipmapped RT so distant cloth samples soft-filter
        /// instead of harsh bilinear shimmer. UV space becomes 0..1 over the sprite.
        /// </summary>
        private bool EnsureSoftMap(Sprite? sprite, out Texture tex)
        {
            tex = Texture2D.blackTexture;
            if (sprite == null || sprite.texture == null)
                return false;

            Texture src = sprite.texture;
            int id = sprite.GetInstanceID();
            float bias = Mathf.Clamp(Map3dConfig.MapMipBias.Value, -1f, 3f);

            if (_softMap != null && _softMapSrc == src && _softMapId == id)
            {
                _softMap.mipMapBias = bias;
                _uvRect = new Vector4(0f, 0f, 1f, 1f);
                tex = _softMap;
                return true;
            }

            ReleaseSoftMap();

            Rect tr = sprite.textureRect;
            int w = Mathf.Clamp(Mathf.RoundToInt(tr.width), 64, 4096);
            int h = Mathf.Clamp(Mathf.RoundToInt(tr.height), 64, 4096);

            _softMap = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "Map3d.SoftMap",
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                wrapMode = TextureWrapMode.Clamp,
                mipMapBias = bias
            };
            _softMap.Create();

            // Blit only the sprite atlas rect into soft map (full 0..1 UV).
            float tw = Mathf.Max(1f, src.width);
            float th = Mathf.Max(1f, src.height);
            var scale = new Vector2(tr.width / tw, tr.height / th);
            var offset = new Vector2(tr.x / tw, tr.y / th);
            Graphics.Blit(src, _softMap, scale, offset);

            _softMapSrc = src;
            _softMapId = id;
            _uvRect = new Vector4(0f, 0f, 1f, 1f);
            tex = _softMap;
            return true;
        }

        private void ReleaseSoftMap()
        {
            if (_softMap != null)
            {
                _softMap.Release();
                UnityEngine.Object.Destroy(_softMap);
                _softMap = null;
            }
            _softMapSrc = null;
            _softMapId = 0;
        }

        private static Material CreateMapMaterial()
        {
            // Opaque unlit — UI/Default alpha-blends and washed the cloth vs stock.
            Shader? shader = Shader.Find("Unlit/Texture")
                ?? Shader.Find("UI/Default")
                ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader!)
            {
                name = "Map3d.ClothMat",
                hideFlags = HideFlags.HideAndDontSave,
                color = Color.white,
                renderQueue = 2000
            };
            if (mat.HasProperty("_ZWrite"))
                mat.SetInt("_ZWrite", 1);
            return mat;
        }

        private void EnsureRt(int size, int msaa)
        {
            size = Mathf.Clamp(size, 128, 2048);
            msaa = NormalizeMsaa(msaa);
            if (_rt != null && _rtSize == size && _rtMsaa == msaa)
                return;
            _rtSize = size;
            _rtMsaa = msaa;
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
                antiAliasing = msaa,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _rt.Create();
        }

        private static int NormalizeMsaa(int samples)
        {
            if (samples <= 0)
                return 1;
            if (samples <= 2)
                return 2;
            if (samples <= 4)
                return 4;
            return 8;
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
            _objectives?.Dispose();
            _objectives = null;
            _radar?.Dispose();
            _radar = null;
            _grid?.Dispose();
            _grid = null;
            _extentsInit = false;
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
            ReleaseSoftMap();
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

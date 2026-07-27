using System;
using Map3d.Config;
using UnityEngine;

namespace Map3d.Engine
{
    /// <summary>
    /// Full-map height cache baked once at mission load via PathfindingAgent.RaycastTerrain.
    /// Stores GlobalPosition.y (sea ≈ 0). Cloth samples bilinear from this cache.
    /// </summary>
    internal sealed class HeightMapCache
    {
        private static HeightMapCache? _instance;

        private float[] _heights = Array.Empty<float>();
        private int _resolution;
        private Vector2 _mapSize;
        private float _minY;
        private float _maxY;
        private int _bakeIndex;
        private bool _baking;
        private bool _ready;
        private string _mapKey = string.Empty;

        internal static HeightMapCache Instance => _instance ??= new HeightMapCache();

        internal bool IsReady => _ready && _heights.Length == _resolution * _resolution;
        internal bool IsBaking => _baking;
        internal float Progress =>
            !_baking || _heights.Length == 0
                ? (IsReady ? 1f : 0f)
                : Mathf.Clamp01(_bakeIndex / (float)_heights.Length);
        internal float MinY => _minY;
        internal float MaxY => _maxY;
        internal float SeaY => 0f;
        internal int Resolution => _resolution;

        internal void Invalidate()
        {
            _baking = false;
            _ready = false;
            _bakeIndex = 0;
            _mapKey = string.Empty;
            _heights = Array.Empty<float>();
            _resolution = 0;
            _minY = 0f;
            _maxY = 0f;
        }

        /// <summary>Begin or continue bake when LevelInfo map settings are available.</summary>
        internal void EnsureBaking()
        {
            if (!Map3dConfig.IsBound || !Map3dConfig.HeightEnabled.Value)
                return;

            LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings? settings = level != null ? level.LoadedMapSettings : null;
            if (settings == null || settings.MapSize.x < 1f || settings.MapSize.y < 1f)
                return;

            int res = Mathf.Clamp(Map3dConfig.HeightCacheResolution.Value, 64, 512);
            string key = $"{settings.MapSize.x:F0}x{settings.MapSize.y:F0}:{res}";
            if (_ready && _mapKey == key)
                return;
            if (_baking && _mapKey == key)
                return;

            _mapKey = key;
            _mapSize = settings.MapSize;
            _resolution = res;
            int count = res * res;
            _heights = new float[count];
            for (int i = 0; i < count; i++)
                _heights[i] = 0f;
            _bakeIndex = 0;
            _baking = true;
            _ready = false;
            _minY = 0f;
            _maxY = 0f;
        }

        internal void TickBake()
        {
            if (!_baking || _heights.Length == 0)
                return;

            int perFrame = Mathf.Clamp(Map3dConfig.HeightBakeSamplesPerFrame.Value, 32, 2048);
            int total = _heights.Length;
            int end = Mathf.Min(_bakeIndex + perFrame, total);
            int n = _resolution;
            float step = n > 1 ? 1f / (n - 1) : 0f;
            float halfX = _mapSize.x * 0.5f;
            float halfZ = _mapSize.y * 0.5f;

            for (int i = _bakeIndex; i < end; i++)
            {
                int ix = i % n;
                int iz = i / n;
                float u = ix * step;
                float v = iz * step;
                float gx = -halfX + u * _mapSize.x;
                float gz = -halfZ + v * _mapSize.y;
                _heights[i] = SampleGlobalY(gx, gz);
            }

            _bakeIndex = end;
            if (_bakeIndex >= total)
            {
                _baking = false;
                _ready = true;
                RecomputeStats();
                try
                {
                    Debug.Log($"[Map3d] Height cache ready {_resolution}x{_resolution} y=[{_minY:F0}..{_maxY:F0}]");
                }
                catch
                {
                    // ignored
                }
            }
        }

        internal bool TrySampleWorld(Vector3 worldLocal, out float globalY)
        {
            globalY = 0f;
            if (!IsReady)
                return false;

            GlobalPosition gp = worldLocal.ToGlobalPosition();
            return TrySampleGlobal(gp.x, gp.z, out globalY);
        }

        internal bool TrySampleGlobal(float gx, float gz, out float globalY)
        {
            globalY = 0f;
            if (!IsReady || _mapSize.x < 1f || _mapSize.y < 1f)
                return false;

            float u = (gx + _mapSize.x * 0.5f) / _mapSize.x;
            float v = (gz + _mapSize.y * 0.5f) / _mapSize.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                globalY = SampleGlobalY(gx, gz);
                return true;
            }

            globalY = Bilinear(u, v);
            return true;
        }

        internal float ResolveHeightScaleMeters(float radius)
        {
            float visual = Mathf.Clamp(Map3dConfig.HeightVisualFraction.Value, 0.05f, 0.8f);
            float ex = Mathf.Clamp(Map3dConfig.HeightExaggeration.Value, 0.1f, 5f);
            float span = Mathf.Max(1f, _maxY - _minY);
            return (visual * Mathf.Max(500f, radius) / span) * ex;
        }

        private float Bilinear(float u, float v)
        {
            int n = _resolution;
            float fx = u * (n - 1);
            float fz = v * (n - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, n - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, n - 1);
            int x1 = Mathf.Min(x0 + 1, n - 1);
            int z1 = Mathf.Min(z0 + 1, n - 1);
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = _heights[z0 * n + x0];
            float h10 = _heights[z0 * n + x1];
            float h01 = _heights[z1 * n + x0];
            float h11 = _heights[z1 * n + x1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        private void RecomputeStats()
        {
            if (_heights.Length == 0)
                return;
            float min = _heights[0];
            float max = _heights[0];
            for (int i = 1; i < _heights.Length; i++)
            {
                float h = _heights[i];
                if (h < min) min = h;
                if (h > max) max = h;
            }
            _minY = min;
            _maxY = max;
        }

        private static float SampleGlobalY(float gx, float gz)
        {
            try
            {
                var gp = new GlobalPosition(gx, 0f, gz);
                if (PathfindingAgent.RaycastTerrain(gp, out RaycastHit hit))
                {
                    float gy = hit.point.GlobalY();
                    return gy < 0f ? 0f : gy;
                }
            }
            catch
            {
                // ignored
            }
            return 0f;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Map3d.Integration
{
    /// <summary>
    /// Replaces flat mapImage with engine RT; hides stock iconLayer, viewIndicator, mapGrid_*.
    /// 3D stock grid is drawn inside the RT by StockClothGrid.
    /// </summary>
    internal sealed class MinimapSlot : IDisposable
    {
        private RawImage? _raw;
        private Image? _mapImage;
        private GameObject? _iconLayer;
        private GameObject? _viewIndicator;
        private DynamicMap? _map;
        private bool _bound;
        private bool _applied;
        private bool _wasMapOn = true;
        private bool _wasIconLayerOn = true;
        private bool _wasViewOn = true;

        internal bool TryBind(DynamicMap map)
        {
            if (map?.mapBackground == null || map.mapImage == null)
                return false;
            if (_bound && _raw != null)
            {
                _map = map;
                return true;
            }

            RectTransform parent = map.mapBackground.rectTransform;
            var go = new GameObject("Map3d.Slot", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _raw = go.GetComponent<RawImage>();
            _raw.color = Color.white;
            _raw.raycastTarget = false;
            _raw.enabled = false;
            _mapImage = map.mapImage.GetComponent<Image>();
            _iconLayer = map.iconLayer;
            _viewIndicator = map.viewIndicator;
            _map = map;
            _bound = true;
            return true;
        }

        internal void Show(RenderTexture rt)
        {
            if (_raw == null || rt == null)
                return;

            if (_raw.texture != rt)
                _raw.texture = rt;
            _raw.enabled = true;
            if (!_raw.gameObject.activeSelf)
                _raw.gameObject.SetActive(true);

            if (!_applied)
            {
                if (_mapImage != null)
                {
                    _wasMapOn = _mapImage.enabled;
                    _mapImage.enabled = false;
                }
                if (_iconLayer != null)
                {
                    _wasIconLayerOn = _iconLayer.activeSelf;
                    _iconLayer.SetActive(false);
                }
                if (_viewIndicator != null)
                {
                    _wasViewOn = _viewIndicator.activeSelf;
                    _viewIndicator.SetActive(false);
                }
                _applied = true;
            }
            else
            {
                if (_iconLayer != null && _iconLayer.activeSelf)
                    _iconLayer.SetActive(false);
                if (_viewIndicator != null && _viewIndicator.activeSelf)
                    _viewIndicator.SetActive(false);
            }

            HideStockGridTiles(_map);
        }

        internal void Hide()
        {
            if (_raw != null)
            {
                _raw.enabled = false;
                _raw.gameObject.SetActive(false);
            }

            if (!_applied)
                return;

            if (_mapImage != null)
                _mapImage.enabled = _wasMapOn;
            if (_iconLayer != null)
                _iconLayer.SetActive(_wasIconLayerOn);
            if (_viewIndicator != null)
                _viewIndicator.SetActive(_wasViewOn);
            RestoreStockGridTiles(_map);

            _applied = false;
        }

        private static void HideStockGridTiles(DynamicMap? map)
        {
            if (map?.gridLabels == null)
                return;
            Transform root = map.gridLabels.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c == null || !c.name.StartsWith("mapGrid_", StringComparison.Ordinal))
                    continue;
                if (c.gameObject.activeSelf)
                    c.gameObject.SetActive(false);
            }
        }

        private static void RestoreStockGridTiles(DynamicMap? map)
        {
            if (map?.gridLabels == null)
                return;
            if (SceneSingleton<MapOptions>.i != null && !SceneSingleton<MapOptions>.i.showGridLabels)
                return;
            Transform root = map.gridLabels.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c == null || !c.name.StartsWith("mapGrid_", StringComparison.Ordinal))
                    continue;
                if (!c.gameObject.activeSelf)
                    c.gameObject.SetActive(true);
            }

            try
            {
                map.gridLabels.UpdateGridColor();
            }
            catch
            {
                // Theme may be unavailable during load.
            }
        }

        public void Dispose()
        {
            Hide();
            if (_raw != null)
            {
                UnityEngine.Object.Destroy(_raw.gameObject);
                _raw = null;
            }
            _mapImage = null;
            _iconLayer = null;
            _viewIndicator = null;
            _map = null;
            _bound = false;
        }
    }
}

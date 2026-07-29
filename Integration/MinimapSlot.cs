using System;
using UnityEngine;
using UnityEngine.UI;

namespace Map3d.Integration
{
    /// <summary>
    /// Replaces flat mapImage with engine RT; soft-hides stock iconLayer (keeps Update alive),
    /// hides viewIndicator and mapGrid_*. 3D grid drawn by StockClothGrid.
    /// infoLayer stays active (TargetMarker / JammedMarker visuals silenced per-frame by cloth layers).
    /// </summary>
    internal sealed class MinimapSlot : IDisposable
    {
        private RawImage? _raw;
        private Image? _mapImage;
        private GameObject? _iconLayer;
        private CanvasGroup? _iconGroup;
        private GameObject? _viewIndicator;
        private GameObject? _gridLabels;
        private DynamicMap? _map;
        private bool _bound;
        private bool _applied;
        private bool _wasMapOn = true;
        private bool _wasViewOn = true;
        private bool _wasGridLabelsOn = true;
        private float _wasIconAlpha = 1f;
        private bool _wasIconBlocks;
        private bool _wasIconInteractable = true;
        private bool _createdIconGroup;

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
            _gridLabels = map.gridLabels != null ? map.gridLabels.gameObject : null;
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
                SoftHideIconLayer(true);
                if (_viewIndicator != null)
                {
                    _wasViewOn = _viewIndicator.activeSelf;
                    _viewIndicator.SetActive(false);
                }
                if (_gridLabels != null)
                {
                    _wasGridLabelsOn = _gridLabels.activeSelf;
                    _gridLabels.SetActive(false);
                }
                _applied = true;
            }
            else
            {
                SoftHideIconLayer(false);
                if (_viewIndicator != null && _viewIndicator.activeSelf)
                    _viewIndicator.SetActive(false);
                if (_gridLabels != null && _gridLabels.activeSelf)
                    _gridLabels.SetActive(false);
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
            SoftRestoreIconLayer();
            if (_viewIndicator != null)
                _viewIndicator.SetActive(_wasViewOn);
            if (_gridLabels != null)
                _gridLabels.SetActive(_wasGridLabelsOn);
            RestoreStockGridTiles(_map);

            _applied = false;
        }

        private void SoftHideIconLayer(bool firstApply)
        {
            if (_iconLayer == null)
                return;

            if (!_iconLayer.activeSelf)
                _iconLayer.SetActive(true);

            _iconGroup = _iconLayer.GetComponent<CanvasGroup>();
            if (_iconGroup == null)
            {
                _iconGroup = _iconLayer.AddComponent<CanvasGroup>();
                _createdIconGroup = true;
            }

            if (firstApply)
            {
                _wasIconAlpha = _iconGroup.alpha;
                _wasIconBlocks = _iconGroup.blocksRaycasts;
                _wasIconInteractable = _iconGroup.interactable;
            }

            _iconGroup.alpha = 0f;
            _iconGroup.blocksRaycasts = false;
            _iconGroup.interactable = false;
        }

        private void SoftRestoreIconLayer()
        {
            if (_iconGroup != null)
            {
                _iconGroup.alpha = _wasIconAlpha;
                _iconGroup.blocksRaycasts = _wasIconBlocks;
                _iconGroup.interactable = _wasIconInteractable;
                if (_createdIconGroup)
                {
                    UnityEngine.Object.Destroy(_iconGroup);
                    _createdIconGroup = false;
                }
                _iconGroup = null;
            }
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
                // ignore
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
            _gridLabels = null;
            _map = null;
            _bound = false;
        }
    }
}

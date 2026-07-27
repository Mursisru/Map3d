using Map3d.Config;
using Map3d.Engine;
using Map3d.Integration;
using UnityEngine;

namespace Map3d
{
    internal sealed class Map3dController : MonoBehaviour
    {
        private MapTiltEngine? _engine;
        private MinimapSlot? _slot;
        private bool _active;

        internal void Activate()
        {
            _active = true;
            _engine ??= MapTiltEngine.Create();
            _slot ??= new MinimapSlot();
        }

        internal void Deactivate()
        {
            _active = false;
            _slot?.Hide();
            _slot?.Dispose();
            _slot = null;
            _engine?.Dispose();
            _engine = null;
        }

        internal void OnMinimized() { }

        internal void OnMaximized()
        {
            _slot?.Hide();
            _engine?.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_active && Map3dConfig.IsEnabled)
            {
                HeightMapCache cache = HeightMapCache.Instance;
                cache.EnsureBaking();
                cache.TickBake();
            }

            if (!_active || !Map3dConfig.IsEnabled)
            {
                _slot?.Hide();
                _engine?.SetActive(false);
                return;
            }

            DynamicMap? map = SceneSingleton<DynamicMap>.i;
            if (map == null)
                return;

            if (DynamicMap.mapMaximized)
            {
                OnMaximized();
                return;
            }

            if (_engine == null || _slot == null)
                Activate();

            if (!_slot!.TryBind(map))
                return;

            Aircraft? own = SceneSingleton<CombatHUD>.i != null
                ? SceneSingleton<CombatHUD>.i.aircraft
                : null;

            if (!_engine!.Tick(own) || _engine.Output == null)
                return;

            _slot.Show(_engine.Output);
        }

        internal static Map3dController? Find() => FindObjectOfType<Map3dController>();
    }
}

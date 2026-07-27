using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Map3d.Engine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map3d
{
    internal sealed class Map3dHost : MonoBehaviour
    {
        private static Map3dHost? _instance;
        private HarmonyLib.Harmony? _harmony;
        private ManualLogSource? _log;
        private bool _ready;
        private bool _scheduled;
        private Map3dController? _ctrl;

        internal static void Ensure(ManualLogSource log)
        {
            if (_instance != null)
                return;
            var go = new GameObject("Map3d.Host");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<Map3dHost>();
            _instance._log = log;
            SceneManager.sceneLoaded += _instance.OnLoaded;
            _instance.TryNow();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Shutdown();
        }

        private void OnLoaded(Scene s, LoadSceneMode _)
        {
            if (IsMenu(s.path))
            {
                if (_ready)
                {
                    Shutdown();
                    _ready = false;
                    _scheduled = false;
                }
                HeightMapCache.Instance.Invalidate();
                return;
            }
            if (!_ready)
                Schedule(s.path);
        }

        private void TryNow()
        {
            Scene s = SceneManager.GetActiveScene();
            if (!IsMenu(s.path))
                Schedule(s.path);
        }

        private void Schedule(string path)
        {
            if (_ready || _scheduled)
                return;
            _scheduled = true;
            StartCoroutine(Boot(path));
        }

        private IEnumerator Boot(string path)
        {
            yield return null;
            yield return null;
            if (_ready)
                yield break;
            _ready = true;
            _harmony ??= new HarmonyLib.Harmony(Map3dPlugin.PluginGuid);
            _harmony.PatchAll(typeof(Map3dPlugin).Assembly);
            int n = 0;
            foreach (MethodBase _ in _harmony.GetPatchedMethods())
                n++;
            _log?.LogInfo($"Map3d patches={n}");
            _ctrl = gameObject.AddComponent<Map3dController>();
            _ctrl.Activate();
            HeightMapCache.Instance.EnsureBaking();
            _log?.LogInfo($"Map3d tilt engine v{AppVersion.DisplayVersion} ready ({path})");
        }

        private void Shutdown()
        {
            if (_ctrl != null)
            {
                _ctrl.Deactivate();
                Destroy(_ctrl);
                _ctrl = null;
            }
            HeightMapCache.Instance.Invalidate();
        }

        private static bool IsMenu(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;
            return path.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("MultiplayerMenu", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("MissionsMenu", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Encyclopedia", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("MissionEditor", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnApplicationQuit()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Shutdown();
            _harmony?.UnpatchSelf();
            _harmony = null;
            _instance = null;
        }
    }
}

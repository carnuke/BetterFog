using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFog
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "carnuke.betterfog";
        public const string PluginName = "BetterFog";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log = null!;
        private static ConfigEntry<bool> _enableBetterFog = null!;
        private static ConfigEntry<float> _fogStartOverride = null!;
        private static ConfigEntry<float> _fogEndOverride = null!;

        // Cached reflection handles — populated once in Awake
        private static FieldInfo? _lgInstanceField;
        private static FieldInfo? _lgGeneratedField;
        private static FieldInfo? _envFogColorField;
        private static FieldInfo? _envFogStartField;
        private static FieldInfo? _envFogEndField;
        private static FieldInfo? _envMainCameraField;
        private static FieldInfo? _envSetupDoneField;
        private static FieldInfo? _rmInstanceField;
        private static FieldInfo? _rmLevelCurrentField;
        private static FieldInfo? _rmLevelMainMenuField;
        private static FieldInfo? _rmLevelLobbyMenuField;
        private static FieldInfo? _rmLevelSplashScreenField;

        // Extra units past FogEndDistance before geometry is clipped; raise if clip is too close
        private const float ClipPadding = 30f;

        private static Material?             _fogMaterial;
        private static CylindricalFogEffect? _fogEffect;
        private const float MenuFarClipPlane = 5000f;

        // Set each frame in FogLogic_Postfix; LateUpdate enforces it as belt-and-suspenders
        private static float _desiredFarClip = MenuFarClipPlane;
        private static int   _lastPostfixFrame = -9999;
        private static UnityEngine.Object? _cachedEnvInstance;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
            _enableBetterFog = Config.Bind(
                "Fog",
                "Enable Better Fog",
                true,
                "Use BetterFog's radial fog instead of REPO's original fog.");
            _fogStartOverride = Config.Bind(
                "Fog",
                "Start Distance",
                8f,
                "Fog start distance in world units. One level module is 15 units long. Default: 8.");
            _fogEndOverride = Config.Bind(
                "Fog",
                "End Distance",
                16f,
                "Fog end distance in world units. One level module is 15 units long. Default: 16.");

            CacheReflection();
            LoadFogBundle();

            var envType = AccessTools.TypeByName("EnvironmentDirector");
            if (envType == null) { Log.LogError("EnvironmentDirector type not found."); return; }

            var fogLogicMethod = AccessTools.Method(envType, "FogLogic");
            if (fogLogicMethod == null) { Log.LogError("EnvironmentDirector.FogLogic not found."); return; }

            new Harmony(PluginGuid).Patch(fogLogicMethod,
                postfix: new HarmonyMethod(typeof(Plugin), nameof(FogLogic_Postfix)));
            Log.LogInfo("Patched EnvironmentDirector.FogLogic.");
        }

        private static void CacheReflection()
        {
            var lgType  = AccessTools.TypeByName("LevelGenerator");
            var envType = AccessTools.TypeByName("EnvironmentDirector");
            if (lgType  != null) { _lgInstanceField  = AccessTools.Field(lgType,  "Instance"); _lgGeneratedField  = AccessTools.Field(lgType,  "Generated"); }
            if (envType != null) { _envFogColorField = AccessTools.Field(envType, "FogColor"); _envFogStartField = AccessTools.Field(envType, "FogStartDistance"); _envFogEndField = AccessTools.Field(envType, "FogEndDistance"); _envMainCameraField = AccessTools.Field(envType, "MainCamera"); _envSetupDoneField = AccessTools.Field(envType, "SetupDone"); }
            var rmType = AccessTools.TypeByName("RunManager");
            if (rmType != null) { _rmInstanceField = AccessTools.Field(rmType, "instance"); _rmLevelCurrentField = AccessTools.Field(rmType, "levelCurrent"); _rmLevelMainMenuField = AccessTools.Field(rmType, "levelMainMenu"); _rmLevelLobbyMenuField = AccessTools.Field(rmType, "levelLobbyMenu"); _rmLevelSplashScreenField = AccessTools.Field(rmType, "levelSplashScreen"); }
        }

        // Read MainCamera directly from the EnvironmentDirector instance we already have
        private static Camera? GetEnvCamera(object envInstance) =>
            (_envMainCameraField?.GetValue(envInstance) as Camera) ?? Camera.main;

        private static void LoadFogBundle()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "betterfog");
            if (!File.Exists(path)) { Log.LogError($"betterfog bundle not found at: {path}"); return; }

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) { Log.LogError("Failed to load betterfog bundle."); return; }

            var shader = bundle.LoadAsset<Shader>("CylindricalFog");
            if (shader == null) { Log.LogError("CylindricalFog shader not in bundle."); bundle.Unload(true); return; }

            _fogMaterial = new Material(shader) { name = "CylindricalFogMat" };
            bundle.Unload(false);
            Log.LogInfo("Cylindrical fog shader loaded.");
        }

        private static bool IsLevelGenerated()
        {
            if (_lgInstanceField == null || _lgGeneratedField == null) return false;
            var inst = _lgInstanceField.GetValue(null);
            return inst != null && (bool)(_lgGeneratedField.GetValue(inst) ?? false);
        }

        // True only when both the level is generated AND EnvironmentDirector has finished setup
        private static bool IsLevelActive(object envInstance)
        {
            if (!IsLevelGenerated()) return false;
            if (_envSetupDoneField == null) return true;
            return (bool)(_envSetupDoneField.GetValue(envInstance) ?? false);
        }

        // False when the current level is a known non-gameplay scene (menu, splash screen)
        private static bool IsGameplayLevel()
        {
            if (_rmInstanceField == null || _rmLevelCurrentField == null) return true;
            var rm = _rmInstanceField.GetValue(null);
            if (rm == null) return true;
            var current = _rmLevelCurrentField.GetValue(rm);
            if (current == null) return false;
            if (_rmLevelMainMenuField     != null && ReferenceEquals(current, _rmLevelMainMenuField.GetValue(rm)))     return false;
            if (_rmLevelLobbyMenuField    != null && ReferenceEquals(current, _rmLevelLobbyMenuField.GetValue(rm)))    return false;
            if (_rmLevelSplashScreenField != null && ReferenceEquals(current, _rmLevelSplashScreenField.GetValue(rm))) return false;
            return true;
        }

        // Fallback: FogLogic may not run every frame on the menu (SetupDone gate)
        private void Update()
        {
            // Use the cached env instance to apply the same guard as the postfix
            bool inLevel = _cachedEnvInstance != null && IsLevelActive(_cachedEnvInstance) && IsGameplayLevel();
            if (inLevel) return;
            if (_fogEffect != null) { Destroy(_fogEffect); _fogEffect = null; }
            RenderSettings.fog = true;
        }

        // LateUpdate runs after all Updates: enforce farClipPlane last so no game script overrides it
        private void LateUpdate()
        {
            bool postfixStale = (Time.frameCount - _lastPostfixFrame) > 5;
            bool inLevel = !postfixStale && _cachedEnvInstance != null && IsLevelActive(_cachedEnvInstance) && IsGameplayLevel();
            float target = inLevel ? _desiredFarClip : MenuFarClipPlane;
            foreach (var cam in Camera.allCameras)
                cam.farClipPlane = target;
        }

        private static void FogLogic_Postfix(object __instance)
        {
            try
            {
                _lastPostfixFrame  = Time.frameCount;
                _cachedEnvInstance = __instance as UnityEngine.Object;

                bool levelActive = IsLevelActive(__instance);
                bool inGame      = levelActive && IsGameplayLevel();

                if (!inGame)
                {
                    if (_fogEffect != null) { UnityEngine.Object.Destroy(_fogEffect); _fogEffect = null; }
                    RenderSettings.fog = true;
                    _desiredFarClip = MenuFarClipPlane;
                    // Apply directly — LateUpdate may not run in all BepInEx configurations
                    var menuCam = GetEnvCamera(__instance);
                    if (menuCam != null) menuCam.farClipPlane = MenuFarClipPlane;
                    return;
                }

                if (_fogMaterial == null) return;

                if (!_enableBetterFog.Value)
                {
                    if (_fogEffect != null) { UnityEngine.Object.Destroy(_fogEffect); _fogEffect = null; }
                    RenderSettings.fog = true;
                    return;
                }

                Color fogColor = _envFogColorField != null ? (Color)_envFogColorField.GetValue(__instance) : Color.gray;
                float fogStart = _fogStartOverride.Value;
                float fogEnd = _fogEndOverride.Value;
                fogEnd = Mathf.Max(fogEnd, fogStart + 0.01f);

                if (_fogEffect == null)
                {
                    var cam = GetEnvCamera(__instance);
                    if (cam != null) _fogEffect = CylindricalFogEffect.Attach(cam, _fogMaterial);
                }

                _fogMaterial.SetColor("_FogColor", fogColor);
                _fogMaterial.SetFloat("_FogStart", fogStart);
                _fogMaterial.SetFloat("_FogEnd",   fogEnd);

                // Cache params so OnPreRender can restore the globals Unity zeroed (fog=false)
                if (_fogEffect != null)
                {
                    _fogEffect.FogColorVal = fogColor;
                    _fogEffect.FogStartVal = fogStart;
                    _fogEffect.FogEndVal   = fogEnd;
                }

                _desiredFarClip = fogEnd + ClipPadding;
                // Apply directly — LateUpdate may not run in all BepInEx configurations
                var levelCam = GetEnvCamera(__instance);
                if (levelCam != null) levelCam.farClipPlane = fogEnd + ClipPadding;

                RenderSettings.fog = false;
            }
            catch (Exception ex)
            {
                Log.LogError($"FogLogic_Postfix: {ex}");
            }
        }

    }

    internal class CylindricalFogEffect : MonoBehaviour
    {
        static readonly int TempRT = Shader.PropertyToID("_CylFogTemp");
        CommandBuffer? _cmd;
        Material?      _mat;
        readonly Vector3[] _frustumCorners = new Vector3[4];
        // Cached per-frame fog values; OnPreRender restores globals that Unity zeroes when fog=false
        internal Color FogColorVal = Color.gray;
        internal float FogStartVal;
        internal float FogEndVal = 200f;

        internal static CylindricalFogEffect Attach(Camera cam, Material mat)
        {
            var fx = cam.gameObject.AddComponent<CylindricalFogEffect>();
            fx._mat = mat;
            fx.Setup(cam);
            return fx;
        }

        void Setup(Camera cam)
        {
            cam.depthTextureMode |= DepthTextureMode.Depth;
            // Commands are (re)recorded each frame in OnPreRender with current fog values
            _cmd = new CommandBuffer { name = "CylindricalFog" };
            cam.AddCommandBuffer(CameraEvent.BeforeImageEffects, _cmd);
        }

        void OnPreRender()
        {
            if (_mat == null || _cmd == null) return;
            var cam = GetComponent<Camera>();
            cam.CalculateFrustumCorners(
                new Rect(0f, 0f, 1f, 1f),
                1f,
                Camera.MonoOrStereoscopicEye.Mono,
                _frustumCorners);
            _mat.SetVector("_FrustumBottomLeft", _frustumCorners[0]);
            _mat.SetVector("_FrustumTopLeft", _frustumCorners[1]);
            _mat.SetVector("_FrustumTopRight", _frustumCorners[2]);
            _mat.SetVector("_FrustumBottomRight", _frustumCorners[3]);
            _mat.SetMatrix("_CylFogCameraToWorld", cam.cameraToWorldMatrix);
            Vector3 fogOrigin = PlayerAvatar.instance != null && PlayerAvatar.instance.playerTransform != null
                ? PlayerAvatar.instance.playerTransform.position
                : cam.transform.position;
            _mat.SetVector("_FogOrigin", fogOrigin);
            _cmd.Clear();
            _cmd.GetTemporaryRT(TempRT, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            _cmd.Blit(BuiltinRenderTextureType.CameraTarget, TempRT, _mat);
            _cmd.Blit(TempRT, BuiltinRenderTextureType.CameraTarget);
            _cmd.ReleaseTemporaryRT(TempRT);
        }

        void OnDestroy()
        {
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                if (_cmd != null) cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _cmd);
            }
            _cmd?.Dispose();
        }
    }
}

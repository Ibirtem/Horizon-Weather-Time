using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Resolves the time-of-day fog state and publishes it as six packed global
    /// vectors consumed by HorizonFogCommon.cginc (screen fog, skybox, clouds,
    /// particles). Owns the switch between the analytical height fog pipeline and
    /// the legacy built-in RenderSettings.fog path.
    /// </summary>
    [ExecuteInEditMode]
#if UDONSHARP
    public class FogManager : UdonSharpBehaviour
#else
    public class FogManager : MonoBehaviour
#endif
    {
        [Header("Scene References")]
        [Tooltip("Proxy volume that applies the atmospheric fog to opaque geometry.")]
        public HorizonFogVolume fogVolume;

        // =========================================================
        // CACHED SHADER PROPERTY IDS
        // =========================================================
        [System.NonSerialized] private int _Params0ID;
        [System.NonSerialized] private int _Params1ID;
        [System.NonSerialized] private int _HorizonColorID;
        [System.NonSerialized] private int _ZenithColorID;
        [System.NonSerialized] private int _SunColorID;
        [System.NonSerialized] private int _SunDirID;

        [System.NonSerialized] private bool _idsInitialized = false;

        // =========================================================
        // DIRTY-CHECK CACHE (value types only, no per-frame allocations)
        // =========================================================
        [System.NonSerialized] private Vector4 _params0;
        [System.NonSerialized] private Vector4 _params1;
        [System.NonSerialized] private Color _horizonColor;
        [System.NonSerialized] private Color _zenithColor;
        [System.NonSerialized] private Color _sunColor;
        [System.NonSerialized] private Vector4 _sunDir;

        [System.NonSerialized] private Vector4 _lastParams0;
        [System.NonSerialized] private Vector4 _lastParams1;
        [System.NonSerialized] private Color _lastHorizonColor;
        [System.NonSerialized] private Color _lastZenithColor;
        [System.NonSerialized] private Color _lastSunColor;
        [System.NonSerialized] private Vector4 _lastSunDir;

        [System.NonSerialized] private bool _hasCache = false;

        private const float VECTOR_EPSILON = 0.00001f;

        private void Awake()
        {
            InitializeShaderIDs();
        }

        private void OnEnable()
        {
            InitializeShaderIDs();
        }

        /// <summary>
        /// Caches all global shader property IDs exactly once. VRChat requires the
        /// "_Udon" prefix for script-published globals, so both APIs share the name.
        /// </summary>
        private void InitializeShaderIDs()
        {
#if UDONSHARP
            _Params0ID = VRCShader.PropertyToID("_UdonHorizonFogParams0");
            _Params1ID = VRCShader.PropertyToID("_UdonHorizonFogParams1");
            _HorizonColorID = VRCShader.PropertyToID("_UdonHorizonFogHorizonColor");
            _ZenithColorID = VRCShader.PropertyToID("_UdonHorizonFogZenithColor");
            _SunColorID = VRCShader.PropertyToID("_UdonHorizonFogSunColor");
            _SunDirID = VRCShader.PropertyToID("_UdonHorizonFogSunDir");
#else
            _Params0ID = Shader.PropertyToID("_UdonHorizonFogParams0");
            _Params1ID = Shader.PropertyToID("_UdonHorizonFogParams1");
            _HorizonColorID = Shader.PropertyToID("_UdonHorizonFogHorizonColor");
            _ZenithColorID = Shader.PropertyToID("_UdonHorizonFogZenithColor");
            _SunColorID = Shader.PropertyToID("_UdonHorizonFogSunColor");
            _SunDirID = Shader.PropertyToID("_UdonHorizonFogSunDir");
#endif
            _idsInitialized = true;
            _hasCache = false;
        }

        /// <summary>
        /// Applies the resolved fog profile for the current time of day.
        /// Disables the legacy Unity fog whenever the analytical pipeline is active,
        /// so standard materials never receive two layers of fog.
        /// </summary>
        /// <param name="fp">Resolved (possibly interpolated) fog profile snapshot.</param>
        /// <param name="sunDirToLight">Normalized direction from the scene TO the sun.</param>
        /// <param name="moonDirToLight">Normalized direction from the scene TO the moon.</param>
        /// <param name="sunDayFactor">Sun altitude fraction in [0, 1].</param>
        /// <param name="sunIntensityRatio">Current sun intensity relative to the profile intensity [0, 1+].</param>
        /// <param name="sunLightColor">Current color of the sun light.</param>
        /// <param name="moonLightColor">Current color of the moon light.</param>
        /// <returns>Current horizon fog color, used by the skybox/cloud fallback path.</returns>
        public Color UpdateFog(
            BakedProfileData fp,
            Vector3 sunDirToLight, Vector3 moonDirToLight,
            float sunDayFactor, float sunIntensityRatio,
            Color sunLightColor, Color moonLightColor)
        {
            if (!_idsInitialized) InitializeShaderIDs();

            if (fp == null || !fp.fogEnabled)
            {
                ApplyDisabledState();
                return Color.black;
            }

            if (fp.fogRenderMode == (int)HorizonFogMode.LegacyUnity)
            {
                return ApplyLegacyUnityFog(fp, sunDayFactor, sunIntensityRatio);
            }

            return ApplyAnalyticalFog(fp, sunDirToLight, moonDirToLight,
                sunDayFactor, sunIntensityRatio, sunLightColor, moonLightColor);
        }

        /// <summary>
        /// Turns every fog consumer off and disables the fog hull renderer.
        /// </summary>
        private void ApplyDisabledState()
        {
            RenderSettings.fog = false;
            if (fogVolume != null) fogVolume.SetActiveState(false);
            PublishHorizonDisabled();
        }

        /// <summary>
        /// Legacy path: built-in flat Unity fog, no height gradient.
        /// </summary>
        /// <param name="fp">Resolved fog profile snapshot.</param>
        /// <param name="sunDayFactor">Sun altitude fraction in [0, 1].</param>
        /// <param name="sunIntensityRatio">Current sun intensity relative to the profile intensity.</param>
        /// <returns>Flat legacy fog color.</returns>
        private Color ApplyLegacyUnityFog(BakedProfileData fp, float sunDayFactor, float sunIntensityRatio)
        {
            PublishHorizonDisabled();
            if (fogVolume != null) fogVolume.SetActiveState(false);

            float mix = Mathf.Clamp01(sunDayFactor * sunIntensityRatio);
            Color fogColor = Color.Lerp(fp.fogNightColor, fp.fogDayColor, mix);

            RenderSettings.fog = true;
            RenderSettings.fogMode = (FogMode)fp.fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fp.fogDensity;
            RenderSettings.fogStartDistance = fp.fogStartDistance;
            RenderSettings.fogEndDistance = fp.fogEndDistance;

            return fogColor;
        }

        /// <summary>
        /// Analytical path: disables built-in fog, enables the fog hull, and uploads color-space-safe globals.
        /// </summary>
        /// <returns>Current horizon fog color.</returns>
        private Color ApplyAnalyticalFog(
            BakedProfileData fp,
            Vector3 sunDirToLight, Vector3 moonDirToLight,
            float sunDayFactor, float sunIntensityRatio,
            Color sunLightColor, Color moonLightColor)
        {
            RenderSettings.fog = false;
            if (fogVolume != null) fogVolume.SetActiveState(true);

            float mix = Mathf.Clamp01(sunDayFactor * sunIntensityRatio);
            Color horizonCol = Color.Lerp(fp.fogNightColor, fp.fogDayColor, mix);
            Color zenithCol = Color.Lerp(fp.fogNightZenithColor, fp.fogDayZenithColor, mix);

            Vector3 lightDir = sunDayFactor > 0.05f ? sunDirToLight : moonDirToLight;
            float dirSqr = lightDir.sqrMagnitude;
            if (dirSqr > 0.000001f)
            {
                lightDir = lightDir * (1f / Mathf.Sqrt(dirSqr));
            }
            else
            {
                lightDir = Vector3.up;
            }

            Color inscatterTint = Color.Lerp(moonLightColor * 0.25f, sunLightColor, sunDayFactor);
            Color inscatterCol = fp.fogSunInscatterColor * inscatterTint;

            // Params0: baseDensity, heightDensity, heightFalloff, heightBaseOffset
            _params0.x = fp.fogDensity;
            _params0.y = Mathf.Max(0f, fp.fogHeightDensity);
            _params0.z = Mathf.Max(0.0001f, fp.fogHeightFalloff);
            _params0.w = fp.fogHeightBaseOffset;

            // Params1: startDistance, maxOpacity, skyHorizonHeight, skyBlendIntegrity
            _params1.x = Mathf.Max(0f, fp.fogStartDistance);
            _params1.y = Mathf.Clamp01(fp.fogMaxOpacity);
            _params1.z = Mathf.Clamp(fp.fogSkyHorizonHeight, 0.02f, 1f);
            _params1.w = Mathf.Clamp01(fp.fogSkyBlend);

            _horizonColor = new Color(horizonCol.r, horizonCol.g, horizonCol.b, 1f);
            _zenithColor = new Color(zenithCol.r, zenithCol.g, zenithCol.b, 1f);
            _sunColor = new Color(inscatterCol.r, inscatterCol.g, inscatterCol.b, Mathf.Clamp01(fp.fogSunInscatterIntensity));

            _sunDir.x = lightDir.x;
            _sunDir.y = lightDir.y;
            _sunDir.z = lightDir.z;
            _sunDir.w = Mathf.Max(1f, fp.fogSunInscatterPower);

            if (!_hasCache || VectorChanged(_lastParams0, _params0))
            {
                SetGlobalVector(_Params0ID, _params0);
                _lastParams0 = _params0;
            }

            if (!_hasCache || VectorChanged(_lastParams1, _params1))
            {
                SetGlobalVector(_Params1ID, _params1);
                _lastParams1 = _params1;
            }

            if (!_hasCache || ColorChanged(_lastHorizonColor, _horizonColor))
            {
                SetGlobalColor(_HorizonColorID, _horizonColor);
                _lastHorizonColor = _horizonColor;
            }

            if (!_hasCache || ColorChanged(_lastZenithColor, _zenithColor))
            {
                SetGlobalColor(_ZenithColorID, _zenithColor);
                _lastZenithColor = _zenithColor;
            }

            if (!_hasCache || ColorChanged(_lastSunColor, _sunColor))
            {
                SetGlobalColor(_SunColorID, _sunColor);
                _lastSunColor = _sunColor;
            }

            if (!_hasCache || VectorChanged(_lastSunDir, _sunDir))
            {
                SetGlobalVector(_SunDirID, _sunDir);
                _lastSunDir = _sunDir;
            }

            _hasCache = true;
            return horizonCol;
        }

        /// <summary>
        /// Publishes a zeroed horizon color so every shader falls back to its legacy path.
        /// </summary>
        private void PublishHorizonDisabled()
        {
            _horizonColor = new Color(0f, 0f, 0f, 0f);

            if (!_hasCache || ColorChanged(_lastHorizonColor, _horizonColor))
            {
                SetGlobalColor(_HorizonColorID, _horizonColor);
                _lastHorizonColor = _horizonColor;
            }

            _hasCache = true;
        }

        private bool ColorChanged(Color a, Color b)
        {
            if (Mathf.Abs(a.r - b.r) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.g - b.g) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.b - b.b) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.a - b.a) > VECTOR_EPSILON) return true;
            return false;
        }

        /// <summary>
        /// Uploads a global color so Unity applies sRGB-to-Linear conversion automatically when needed.
        /// </summary>
        private void SetGlobalColor(int propertyID, Color value)
        {
#if UDONSHARP
            VRCShader.SetGlobalColor(propertyID, value);
#else
            Shader.SetGlobalColor(propertyID, value);
#endif
        }

        /// <summary>
        /// Compares two packed vectors with a small epsilon to avoid redundant uploads.
        /// </summary>
        /// <param name="a">Previous cached value.</param>
        /// <param name="b">New value.</param>
        /// <returns>True when any component differs.</returns>
        private bool VectorChanged(Vector4 a, Vector4 b)
        {
            if (Mathf.Abs(a.x - b.x) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.y - b.y) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.z - b.z) > VECTOR_EPSILON) return true;
            if (Mathf.Abs(a.w - b.w) > VECTOR_EPSILON) return true;
            return false;
        }

        /// <summary>
        /// Uploads a global vector through the API available in the current runtime.
        /// </summary>
        /// <param name="propertyID">Cached property ID.</param>
        /// <param name="value">Packed vector value.</param>
        private void SetGlobalVector(int propertyID, Vector4 value)
        {
#if UDONSHARP
            VRCShader.SetGlobalVector(propertyID, value);
#else
            Shader.SetGlobalVector(propertyID, value);
#endif
        }
    }
}

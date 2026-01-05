using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    [ExecuteInEditMode]
#if UDONSHARP
    public class LightingManager : UdonSharpBehaviour
#else
    public class LightingManager : MonoBehaviour
#endif
    {
        [Header("Scene References")]
        [Tooltip("The Directional Light representing the Sun.")]
        [SerializeField] private Light sunLight;

        [Tooltip("The Directional Light representing the Moon.")]
        [SerializeField] private Light moonLight;

        [Header("Configuration")]
        [Tooltip("An AnimationCurve to control the sun's intensity throughout its cycle. X-axis is time (0-1), Y-axis is intensity (0-1).")]
        [SerializeField] private AnimationCurve sunIntensityCurve = new AnimationCurve(new Keyframe(0.23f, 0), new Keyframe(0.25f, 1), new Keyframe(0.75f, 1), new Keyframe(0.77f, 0));

        [Tooltip("An AnimationCurve to control the moon's intensity throughout its cycle.")]
        [SerializeField] private AnimationCurve moonIntensityCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 0));

        [HideInInspector] public Color MoonSkyboxColor;

        public Light SunLight => sunLight;
        public Light MoonLight => moonLight;
        public Transform SunTransform => sunLight != null ? sunLight.transform : null;
        public Transform MoonTransform => moonLight != null ? moonLight.transform : null;

        private float _ambientUpdateTimerPlayMode;
#if UNITY_EDITOR
        private double _ambientUpdateTimerEditorMode;
#endif
        private const float AMBIENT_UPDATE_INTERVAL = 0.25f;

        private void Awake()
        {
            FindOrCreateLights();
        }

        [ContextMenu("Find or Create Lights")]
        private void FindOrCreateLights()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (sunLight == null)
            {
                var existingSun = transform.Find("Sun Light");
                sunLight = existingSun != null ? existingSun.GetComponent<Light>() : CreateLightSource("Sun Light");
            }

            if (moonLight == null)
            {
                var existingMoon = transform.Find("Moon Light");
                moonLight = existingMoon != null ? existingMoon.GetComponent<Light>() : CreateLightSource("Moon Light");
            }
#endif
        }

        private Light CreateLightSource(string name)
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.renderMode = LightRenderMode.ForcePixel;
            
            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> <color=white>[LightingManager] Created new Light Source: '{name}'.</color>", this);
            return light;
#else
            return null;
#endif
        }

        public Light GetActiveLight()
        {
            if (sunLight == null || moonLight == null) return null;
            return sunLight.intensity > moonLight.intensity ? sunLight : moonLight;
        }

        public Color CalculateCurrentGlobalLight(
            float sunTime,
            float moonTime,
            Color sunHorizon, Color sunZenith, float sunIntens,
            Color moonCol, float moonIntens,
            Color dayAmbient, Color nightAmbient)
        {
            float sunIntensityFactor = sunIntensityCurve.Evaluate(sunTime);
            float moonIntensityFactor = moonIntensityCurve.Evaluate(moonTime);

            Color sunContribution = Color.Lerp(sunHorizon, sunZenith, sunIntensityFactor) * (sunIntensityFactor * sunIntens);
            Color moonContribution = moonCol * (moonIntensityFactor * moonIntens);
            Color ambientContribution = Color.Lerp(nightAmbient, dayAmbient, sunIntensityFactor);

            Color totalLight = sunContribution + moonContribution + ambientContribution;

            // Clamp to avoid absolute darkness
            totalLight.r = Mathf.Max(totalLight.r, 0.05f);
            totalLight.g = Mathf.Max(totalLight.g, 0.05f);
            totalLight.b = Mathf.Max(totalLight.b, 0.05f);

            return totalLight;
        }

        public void UpdateLighting(
            float sunTime,
            float moonTime,
            Color sunHorizon, Color sunZenith, float sunIntens,
            Color moonCol, float moonIntens,
            Color dayAmbient, Color nightAmbient)
        {
            if (sunLight == null || moonLight == null) return;

            // 1. Rotation
            float sunCycleAngle = sunTime * 360f;
            sunLight.transform.localRotation = Quaternion.Euler(new Vector3(sunCycleAngle - 90f, 170f, 0));

            float moonCycleAngle = moonTime * 360f;
            moonLight.transform.localRotation = Quaternion.Euler(new Vector3(moonCycleAngle - 90f, 170f, 0));

            float sunHeightVector = -sunLight.transform.forward.y;

            float geometricSunFactor = Mathf.Clamp01(sunHeightVector / 0.05f);

            // 2. Intensity
            float sunCurveVal = sunIntensityCurve.Evaluate(sunTime);
            float moonCurveVal = moonIntensityCurve.Evaluate(moonTime);

            // 3. Visual of the moon
            float sunHeightSimple = -Mathf.Cos(sunTime * Mathf.PI * 2f);
            float twilightTimer = Mathf.InverseLerp(0.15f, -0.05f, sunHeightSimple);

            Color finalMoonVisualColor = moonCol * 2.0f;
            float transitionCurve = Mathf.Pow(twilightTimer, 3.0f);
            float minDayAlpha = 0.16f;
            float maxNightAlpha = 1.0f;
            float finalAlpha = Mathf.Lerp(minDayAlpha, maxNightAlpha, transitionCurve);
            finalMoonVisualColor.a = finalAlpha;

            float moonLightPower = twilightTimer;

            // 4. Application
            sunLight.intensity = sunCurveVal * sunIntens * geometricSunFactor;

            sunLight.color = Color.Lerp(sunHorizon, sunZenith, sunCurveVal);

            moonLight.intensity = moonCurveVal * moonIntens * moonLightPower;

            MoonSkyboxColor = finalMoonVisualColor;
            moonLight.color = moonCol;

            // 5. Shadows and state
            sunLight.enabled = sunLight.intensity > 0.01f;
            moonLight.enabled = moonLight.intensity > 0.001f;

            sunLight.shadows = sunLight.enabled ? LightShadows.Soft : LightShadows.None;
            moonLight.shadows = (moonLight.enabled && !sunLight.enabled) ? LightShadows.Soft : LightShadows.None;

            // 6. Ambient
            bool canUpdateAmbient = false;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (UnityEditor.EditorApplication.timeSinceStartup > _ambientUpdateTimerEditorMode + AMBIENT_UPDATE_INTERVAL)
                {
                    _ambientUpdateTimerEditorMode = UnityEditor.EditorApplication.timeSinceStartup;
                    canUpdateAmbient = true;
                }
            }
            else
#endif
            {
                if (Time.time > _ambientUpdateTimerPlayMode)
                {
                    _ambientUpdateTimerPlayMode = Time.time + AMBIENT_UPDATE_INTERVAL;
                    canUpdateAmbient = true;
                }
            }

            if (canUpdateAmbient)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

                float sunHeightForAmbient = -sunLight.transform.forward.y;
                float ambientGeoFactor = Mathf.InverseLerp(-0.06f, 0.1f, sunHeightForAmbient);

                float finalAmbientMix = sunCurveVal * ambientGeoFactor;

                RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, finalAmbientMix);
            }
        }
    }
}
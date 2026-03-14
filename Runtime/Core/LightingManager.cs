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
        [Tooltip("Legacy curves are ignored in Astronomy mode. Intensity is now driven by physical altitude.")]
        [SerializeField] private AnimationCurve sunIntensityCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 0));
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

        private const float ALT_TRANSITION_MIN = -0.05f;
        private const float ALT_TRANSITION_MAX = 0.05f;

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

        /// <summary>
        /// Calculates a standalone global light color. Used by secondary systems (like particle effects)
        /// to match the environmental lighting without directly reading the Light components.
        /// </summary>
        public Color CalculateCurrentGlobalLight(
            float sunTime,
            float moonTime,
            Color sunHorizon, Color sunZenith, float sunIntens,
            Color moonCol, float moonIntens,
            float moonPhaseBrightness,
            Color daySky, Color nightSky)
        {
            float sunFactor = 0f;
            if (sunLight != null)
            {
                float sunHeight = -sunLight.transform.forward.y;
                float linearT = Mathf.InverseLerp(ALT_TRANSITION_MIN, ALT_TRANSITION_MAX, sunHeight);
                sunFactor = Mathf.SmoothStep(0f, 1f, linearT);
            }

            Color sunContribution = Color.Lerp(sunHorizon, sunZenith, sunFactor) * (sunFactor * sunIntens);
            Color moonContribution = moonCol * (1.0f - sunFactor) * moonIntens * moonPhaseBrightness;

            Color totalLight = sunContribution + moonContribution;

            totalLight.r = Mathf.Max(totalLight.r, 0.05f);
            totalLight.g = Mathf.Max(totalLight.g, 0.05f);
            totalLight.b = Mathf.Max(totalLight.b, 0.05f);

            return totalLight;
        }

        /// <summary>
        /// Applies the calculated astronomical rotations and intensity curves to the Sun and Moon lights.
        /// Updates the global ambient scene lighting based on the PHYSICAL ALTITUDE of the Sun/Moon.
        /// </summary>
        public void UpdateLighting(
            Quaternion sunRotation,
            Quaternion moonRotation,
            float sunTime,
            float moonTime,
            Color sunHorizon, Color sunZenith, float sunIntens,
            Color moonCol, float moonIntens,
            float moonPhaseBrightness,
            Color daySky, Color dayEq, Color dayGrnd,
            Color nightSky, Color nightEq, Color nightGrnd)
        {
            if (sunLight == null || moonLight == null) return;

            // 1. Apply Physical Rotation
            sunLight.transform.localRotation = sunRotation;
            moonLight.transform.localRotation = moonRotation;

            float sunAltitude = -sunLight.transform.forward.y;
            float moonAltitude = -moonLight.transform.forward.y;

            // 3. Calculate "Day Factor" based on Altitude
            float sunT = Mathf.InverseLerp(ALT_TRANSITION_MIN, ALT_TRANSITION_MAX, sunAltitude);
            float dayFactor = Mathf.SmoothStep(0f, 1f, sunT);

            float sunZenithFactor = Mathf.Clamp01(sunAltitude);
            sunZenithFactor = Mathf.Pow(sunZenithFactor, 0.5f);

            // 4. Sun Intensity
            sunLight.intensity = dayFactor * sunIntens;
            sunLight.color = Color.Lerp(sunHorizon, sunZenith, sunZenithFactor);

            // 5. Moon Intensity
            float moonT = Mathf.InverseLerp(ALT_TRANSITION_MIN, ALT_TRANSITION_MAX, moonAltitude);
            float moonHeightFactor = Mathf.SmoothStep(0f, 1f, moonT);
            float moonDayDimming = 1.0f - dayFactor;

            moonLight.intensity = moonHeightFactor * moonDayDimming * moonIntens * moonPhaseBrightness;
            moonLight.color = moonCol;

            Color finalMoonVisualColor = moonCol * 2.0f;
            finalMoonVisualColor.a = Mathf.Lerp(0.16f, 1.0f, moonDayDimming);
            MoonSkyboxColor = finalMoonVisualColor;

            // 6. Shadows and state
            sunLight.enabled = sunLight.intensity > 0.01f;
            moonLight.enabled = moonLight.intensity > 0.001f;

            sunLight.shadows = sunLight.enabled ? LightShadows.Soft : LightShadows.None;
            moonLight.shadows = (moonLight.enabled && !sunLight.enabled) ? LightShadows.Soft : LightShadows.None;

            // 7. Ambient (Trilight)
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
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

                RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, dayFactor);
                RenderSettings.ambientEquatorColor = Color.Lerp(nightEq, dayEq, dayFactor);
                RenderSettings.ambientGroundColor = Color.Lerp(nightGrnd, dayGrnd, dayFactor);
            }
        }
    }
}
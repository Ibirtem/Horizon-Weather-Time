using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Selects how the fog profile is rendered at runtime.
    /// </summary>
    public enum HorizonFogMode
    {
        /// <summary>Analytical height fog published as shader globals and applied by HorizonAtmosphericFog.</summary>
        AnalyticalAtmospheric = 0,

        /// <summary>Legacy built-in Unity RenderSettings.fog (flat color, no height gradient).</summary>
        LegacyUnity = 1
    }

    [CreateAssetMenu(fileName = "New Fog Profile", menuName = "Horizon/Profiles/Fog Profile")]
    public class FogProfile : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Enable or disable fog for this weather profile.")]
        public bool enabled = true;

        [Tooltip("AnalyticalAtmospheric uses the Horizon height fog pipeline. LegacyUnity uses built-in RenderSettings.fog.")]
        public HorizonFogMode renderMode = HorizonFogMode.AnalyticalAtmospheric;

        [Tooltip("The mode of the legacy Unity fog. Linear is great for hard cutoffs, Exponential for atmospheric haze.")]
        public FogMode fogMode = FogMode.ExponentialSquared;

        [Header("Colors")]
        [Tooltip("Horizon fog color during the day. Drives the color of the haze band at eye level.")]
        public Color dayColor = new Color(0.7f, 0.8f, 1f);

        [Tooltip("Horizon fog color during the night.")]
        public Color nightColor = new Color(0.05f, 0.05f, 0.15f);

        [Tooltip("Upper/zenith fog color during the day. Should be darker than the horizon under overcast weather.")]
        public Color dayZenithColor = new Color(0.32f, 0.38f, 0.46f);

        [Tooltip("Upper/zenith fog color during the night.")]
        public Color nightZenithColor = new Color(0.02f, 0.03f, 0.07f);

        [Header("Density")]
        [Tooltip("Uniform (height independent) extinction. Also used by the legacy Unity fog path.")]
        [Range(0f, 0.1f)]
        public float density = 0.002f;

        [Tooltip("Extra exponential height fog density at the fog base height.")]
        [Range(0f, 0.05f)]
        public float heightDensity = 0.004f;

        [Tooltip("How fast the height fog density decays with altitude (1 / meters).")]
        [Range(0.005f, 0.5f)]
        public float heightFalloff = 0.045f;

        [Tooltip("World-space Y where the exponential height fog is at full density.")]
        public float heightBaseOffset = 0f;

        [Tooltip("Maximum opacity the fog is allowed to reach, so distant geometry never becomes a flat wall.")]
        [Range(0f, 1f)]
        public float maxOpacity = 1f;

        [Tooltip("Fog-free distance around the viewer. Saves per-pixel work close to the camera.")]
        public float startDistance = 10f;

        [Tooltip("Ending distance of the legacy Unity fog (used only if mode is Linear).")]
        public float endDistance = 250f;

        [Header("Sky & Scattering")]
        [Tooltip("Normalized view height (0 = horizon, 1 = zenith) where the sky fog band fully fades out.")]
        [Range(0.02f, 1f)]
        public float skyHorizonHeight = 0.25f;

        [Tooltip("How strongly the fog blends into the skybox horizon.")]
        [Range(0f, 1f)]
        public float skyboxBlendIntegrity = 1.0f;

        [Tooltip("Tint of the forward (Mie-like) in-scattering halo around the sun/moon.")]
        public Color sunInscatterColor = new Color(0.85f, 0.85f, 0.8f);

        [Tooltip("Strength of the directional in-scattering halo.")]
        [Range(0f, 1f)]
        public float sunInscatterIntensity = 0.25f;

        [Tooltip("Angular tightness of the in-scattering halo. Higher values produce a smaller, brighter glow.")]
        [Range(1f, 32f)]
        public float sunInscatterPower = 6f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}

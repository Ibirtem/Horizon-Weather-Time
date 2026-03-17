using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Sky Profile", menuName = "Horizon/Profiles/Sky Profile")]
    public class SkyProfile : ScriptableObject
    {
        [Header("Atmosphere")]
        [Tooltip("Controls the overall turbidity (haziness) of the atmosphere.")]
        [Range(1f, 10f)]
        public float turbidity = 5.0f;

        [Tooltip("Controls the Rayleigh scattering effect (blue sky).")]
        [Range(0f, 5f)]
        public float rayleigh = 1f;

        [Tooltip("Controls the Mie scattering coefficient (haze/pollution).")]
        [Range(0f, 0.1f)]
        public float mieCoefficient = 0.005f;

        [Tooltip("Controls the directionality of Mie scattering.")]
        [Range(0f, 1f)]
        public float mieDirectionalG = 0.8f;

        [Tooltip("Overall brightness of the skybox.")]
        [Range(0f, 50f)]
        public float exposure = 15f;

        [Header("Deep Space")]
        [Tooltip("Main Star Map (Stars only). Celestial/Equatorial Coordinates (EXR preferred).")]
        public Cubemap starsCubemap;

        [Tooltip("Milky Way / Galactic Dust only. Additive blend.")]
        public Cubemap milkyWayCubemap;

        [Header("Space Alignment & Intensity")]
        [Tooltip("Static tilt of the celestial sphere (X=Tilt, Y=Initial, Z=Roll).")]
        public Vector3 starfieldAlignment = Vector3.zero;

        [Tooltip("Speed of rotation over time (Y-axis spin).")]
        [Range(-2f, 2f)]
        public float starsRotationSpeed = 0.5f;

        [Range(0f, 5f)]
        public float starsIntensity = 0.04f;

        [Range(0f, 5f)]
        public float milkyWayIntensity = 0.005f;

        [Header("Stars Twinkle")]
        [Range(1f, 300f)]
        public float twinkleScale = 150f;

        [Range(0.1f, 20f)]
        public float twinkleSharpness = 4f;

        [Range(0f, 5.0f)]
        public float twinkleSpeed = 0.06f;

        [Range(0f, 2f)]
        public float twinkleStrength = 0.8f;

        [Header("Night Sky (Airglow)")]
        [Tooltip("Intensity of atmospheric chemiluminescence. Earth default ≈ 0.0004.")]
        [Range(0f, 0.01f)]
        public float airglowIntensity = 0.0004f;

        [Tooltip("Base emission color (linear). Green O-line dominates on Earth.")]
        public Color airglowColor = new Color(0.4f, 0.6f, 0.3f, 1f);

        [Tooltip("Altitude of the emission layer in km. Earth ≈ 90 km.")]
        [Range(30f, 300f)]
        public float airglowHeight = 90f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
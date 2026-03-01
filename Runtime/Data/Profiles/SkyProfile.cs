using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Sky Profile", menuName = "Horizon/Profiles/Sky Profile")]
    public class SkyProfile : ScriptableObject
    {
        [Header("Atmosphere")]
        [Tooltip("Controls the overall turbidity (haziness) of the atmosphere.")]
        [Range(1f, 10f)]
        public float turbidity = 2.5f;

        [Tooltip("Controls the Rayleigh scattering effect (blue sky).")]
        [Range(0f, 5f)]
        public float rayleigh = 1f;

        [Tooltip("Controls the Mie scattering coefficient (haze/pollution).")]
        [Range(0f, 0.1f)]
        public float mieCoefficient = 0.005f;

        [Tooltip("Controls the directionality of Mie scattering.")]
        [Range(0f, 1f)]
        public float mieDirectionalG = 0.76f;

        [Tooltip("Overall brightness of the skybox.")]
        [Range(0f, 50f)]
        public float exposure = 15f;

        [Header("Deep Space")]
        [Tooltip("Main Star Map (Stars only). Celestial/Equatorial Coordinates (EXR preferred).")]
        public Texture starsTexture;

        [Tooltip("Milky Way / Galactic Dust only. Additive blend.")]
        public Texture milkyWayTexture;

        [Header("Space Alignment & Intensity")]
        [Tooltip("Static tilt of the celestial sphere (X=Tilt, Y=Initial, Z=Roll).")]
        public Vector3 starfieldAlignment = Vector3.zero;

        [Tooltip("Speed of rotation over time (Y-axis spin).")]
        [Range(-2f, 2f)]
        public float starsRotationSpeed = 0.5f;

        [Range(0f, 5f)]
        public float starsIntensity = 0.02f;

        [Range(0f, 5f)]
        public float milkyWayIntensity = 0.002f;

        [Header("Stars Twinkle")]
        [Range(1f, 300f)]
        public float twinkleScale = 150f;

        [Range(1, 5)]
        public int twinkleDetail = 3;

        [Range(0.1f, 20f)]
        public float twinkleSharpness = 5f;

        [Range(0f, 0.05f)]
        public float twinkleSpeed = 0.004f;

        [Range(0f, 2f)]
        public float twinkleStrength = 0.8f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
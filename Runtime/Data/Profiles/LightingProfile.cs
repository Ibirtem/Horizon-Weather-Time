using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Lighting Profile", menuName = "Horizon/Profiles/Lighting Profile")]
    public class LightingProfile : ScriptableObject
    {
        [Header("Sun Settings")]
        [Tooltip("Color of the sun at its highest point.")]
        [ColorUsage(true, true)]
        public Color sunColorZenith = Color.white;

        [Tooltip("Color of the sun at the horizon (sunrise/sunset).")]
        [ColorUsage(true, true)]
        public Color sunColorHorizon = new Color(1f, 0.7f, 0.4f);

        [Tooltip("Maximum intensity of the sun's directional light.")]
        [Range(0f, 8f)]
        public float sunIntensity = 1.0f;

        [Header("Moon Settings")]
        [Tooltip("Color of the moon's directional light.")]
        [ColorUsage(true, true)]
        public Color moonColor = new Color(0.8f, 0.9f, 1f);

        [Tooltip("Maximum intensity of the moon's directional light.")]
        [Range(0f, 2f)]
        public float moonIntensity = 0.04f;

        [Header("Ambient Settings - Day (Trilight)")]
        [Tooltip("Light coming from the sky (top down).")]
        [ColorUsage(true, true)]
        public Color daySkyColor = new Color(0.4f, 0.5f, 0.6f);

        [Tooltip("Light coming from the horizon (sides).")]
        [ColorUsage(true, true)]
        public Color dayEquatorColor = new Color(0.3f, 0.35f, 0.4f);

        [Tooltip("Light bouncing off the ground (bottom up).")]
        [ColorUsage(true, true)]
        public Color dayGroundColor = new Color(0.2f, 0.2f, 0.2f);

        [Header("Ambient Settings - Night (Trilight)")]
        [Tooltip("Light coming from the moon/stars (top down).")]
        [ColorUsage(true, true)]
        public Color nightSkyColor = new Color(0.05f, 0.05f, 0.1f);

        [Tooltip("Light coming from the horizon (sides).")]
        [ColorUsage(true, true)]
        public Color nightEquatorColor = new Color(0.02f, 0.02f, 0.05f);

        [Tooltip("Light bouncing off the ground (bottom up).")]
        [ColorUsage(true, true)]
        public Color nightGroundColor = new Color(0.01f, 0.01f, 0.02f);

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
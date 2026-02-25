using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Fog Profile", menuName = "Horizon/Profiles/Fog Profile")]
    public class FogProfile : ScriptableObject
    {
        [Tooltip("Enable or disable fog for this weather profile.")]
        public bool enabled = true;

        [Tooltip("The mode of the fog. Linear is great for hard cutoffs, Exponential for atmospheric haze.")]
        public FogMode fogMode = FogMode.ExponentialSquared;

        [Tooltip("Color of the fog during the day.")]
        public Color dayColor = new Color(0.7f, 0.8f, 1f);

        [Tooltip("Color of the fog during the night.")]
        public Color nightColor = new Color(0.05f, 0.05f, 0.15f);

        [Tooltip("Density of the fog (used if mode is Exponential or ExponentialSquared).")]
        [Range(0f, 0.1f)]
        public float density = 0.002f;

        [Tooltip("Starting distance of the fog (used if mode is Linear).")]
        public float startDistance = 10f;

        [Tooltip("Ending distance of the fog (used if mode is Linear).")]
        public float endDistance = 250f;

        [Tooltip("How strongly the fog blends into the skybox horizon.")]
        [Range(0f, 1f)]
        public float skyboxBlendIntegrity = 1.0f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Cloud Profile", menuName = "Horizon/Profiles/Cloud Profile")]
    public class CloudProfile : ScriptableObject
    {
        [Header("Macro Structure & Optimization")]
        [Tooltip("2D Weather Map. R = Coverage, G = Cloud Type/Height.")]
        public Texture2D weatherMapTexture;

        [Tooltip("Blue noise texture for dithering volumetric raymarching.")]
        public Texture2D blueNoiseTexture;

        [Header("Shape & Coverage")]
        public bool enabled = true;
        public Texture3D cloudNoiseTexture;

        [Tooltip("How high the cloud layer appears. Affects perspective.")]
        [Range(1f, 10f)]
        public float altitude = 3.0f;

        [Tooltip("Overall scale of the cloud patterns. Higher = Smaller clouds.")]
        [Range(0.1f, 10f)]
        public float scale = 3.0f;

        [Tooltip("How much of the sky is covered by clouds.")]
        [Range(0f, 1f)]
        public float coverage = 0.55f;

        [Tooltip("The density or 'thickness' of the clouds.")]
        [Range(0.1f, 5f)]
        public float density = 1.2f;

        [Header("Detail & Animation")]
        [Tooltip("How much the detail layer 'erodes' the base shape.")]
        [Range(0f, 1f)]
        public float detailAmount = 0.5f;

        [Tooltip("Controls the amount of fine, wispy noise.")]
        [Range(0f, 1f)]
        public float wispiness = 0.3f;

        [Tooltip("Wind speed and direction.")]
        public Vector2 windSpeed = new Vector2(0.002f, 0.001f);

        [Header("Lighting")]
        [ColorUsage(true, true)]
        public Color baseColor = new Color(0.95f, 0.95f, 0.95f);

        [ColorUsage(true, true)]
        public Color shadowColor = new Color(0.35f, 0.4f, 0.5f);

        [Tooltip("How much light is scattered through the cloud (makes them brighter).")]
        [Range(0f, 5f)]
        public float lightScattering = 2.0f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
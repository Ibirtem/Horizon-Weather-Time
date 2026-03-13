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

        [Tooltip("2D Curl noise (RG). Distorts cloud noise UV to simulate atmospheric turbulence.")]
        public Texture2D curlNoiseTexture;

        [Header("Shape & Coverage")]
        public bool enabled = true;
        public Texture3D cloudNoiseTexture;

        [Tooltip("How high the cloud layer appears. Affects perspective.")]
        [Range(1f, 10f)]
        public float altitude = 2.0f;

        [Tooltip("Overall scale of the cloud patterns. Higher = Smaller clouds.")]
        [Range(0.1f, 10f)]
        public float scale = 1.0f;

        [Tooltip("How much of the sky is covered by clouds.")]
        [Range(0f, 1f)]
        public float coverage = 0.6f;

        [Tooltip("The density or 'thickness' of the clouds.")]
        [Range(0.1f, 5f)]
        public float density = 1.4f;

        [Header("Detail & Animation")]
        [Tooltip("How much the detail layer 'erodes' the base shape.")]
        [Range(0f, 1f)]
        public float detailAmount = 0.4f;

        [Tooltip("Controls the amount of fine, wispy noise.")]
        [Range(0f, 1f)]
        public float wispiness = 0.1f;

        [Tooltip("Wind speed and direction.")]
        public Vector2 windSpeed = new Vector2(0.002f, 0.001f);

        [Header("Lighting")]
        [ColorUsage(true, true)]
        public Color baseColor = new Color(0.95f, 0.95f, 0.95f);

        [ColorUsage(true, true)]
        public Color shadowColor = new Color(0.35f, 0.4f, 0.5f);

        [Tooltip("How much light is scattered through the cloud (makes them brighter).")]
        [Range(0f, 5f)]
        public float lightScattering = 1.5f;

        [Header("Cirrus Clouds (2D Layer)")]
        [Tooltip("The 2D noise texture used for cirrus clouds.")]
        public Texture2D cirrusNoiseTexture;

        [Tooltip("How much of the sky is covered by cirrus clouds.")]
        [Range(0f, 1f)]
        public float cirrusCoverage = 0.67f;

        [Tooltip("The overall opacity/visibility of the cirrus layer.")]
        [Range(0f, 1f)]
        public float cirrusOpacity = 0.7f;

        [Tooltip("Scale of the cirrus pattern.")]
        [Range(0.1f, 5f)]
        public float cirrusScale = 0.5f;

        [Tooltip("Wind speed and direction specifically for high-altitude cirrus clouds.")]
        public Vector2 cirrusWindSpeed = new Vector2(0.002f, 0.001f);

        [Tooltip("Base tint for the cirrus clouds. Usually left white. Sun lighting is handled automatically.")]
        [ColorUsage(false, true)]
        public Color cirrusTint = Color.white;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}
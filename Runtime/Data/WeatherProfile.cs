using System;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Weather Profile", menuName = "Horizon/Weather Profile")]
    public class WeatherProfile : ScriptableObject
    {
        public static event Action OnProfileDataChanged;

        [Header("General Settings")]
        public string profileName = "New Weather";

        [HideInInspector] public GameObject udon_WeatherEffectPrefab;

        private void OnValidate()
        {
            if (effectsSettings != null)
            {
                udon_WeatherEffectPrefab = effectsSettings.weatherEffectPrefab;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += NotifyChange;
#endif
        }

#if UNITY_EDITOR
        private void NotifyChange()
        {
            OnProfileDataChanged?.Invoke();
        }
#endif

        [Header("Lighting")]
        public LightSettings lightSettings = new LightSettings();

        [Header("Sky")]
        public SkySettings skySettings = new SkySettings();

        [Header("Clouds")]
        public CloudSettings cloudSettings = new CloudSettings();

        [Header("Moon")]
        public MoonSettings moonSettings = new MoonSettings();

        [Header("Fog")]
        public FogSettings fogSettings = new FogSettings();

        [Header("Effects")]
        public EffectsSettings effectsSettings = new EffectsSettings();
    }

    [Serializable]
    public class LightSettings
    {
        [Tooltip("Color of the sun at its highest point.")]
        [ColorUsage(true, true)]
        public Color sunColorZenith = Color.white;

        [Tooltip("Color of the sun at the horizon (sunrise/sunset).")]
        [ColorUsage(true, true)]
        public Color sunColorHorizon = new Color(1f, 0.7f, 0.4f);

        [Tooltip("Color of the moon.")]
        [ColorUsage(true, true)]
        public Color moonColor = new Color(0.8f, 0.9f, 1f);

        [Tooltip("Maximum intensity of the sun's directional light.")]
        [Range(0f, 8f)]
        public float sunIntensity = 1.0f;

        [Tooltip("Maximum intensity of the moon's directional light.")]
        [Range(0f, 2f)]
        public float moonIntensity = 0.04f;

        [Tooltip("Color of the ambient light during the day.")]
        public Color dayAmbientColor = new Color(0.4f, 0.5f, 0.6f);

        [Tooltip("Color of the ambient light during the night.")]
        public Color nightAmbientColor = new Color(0.05f, 0.05f, 0.1f);
    }

    [Serializable]
    public class SkySettings
    {
        [Tooltip("Controls the overall turbidity (haziness) of the atmosphere.")]
        [Range(1f, 10f)]
        public float turbidity = 5f;

        [Tooltip("Controls the Rayleigh scattering effect (blue sky).")]
        [Range(0f, 5f)]
        public float rayleigh = 1f;

        [Tooltip("Controls the Mie scattering coefficient (haze/pollution).")]
        [Range(0f, 0.1f)]
        public float mieCoefficient = 0.005f;

        [Tooltip("Controls the directionality of Mie scattering.")]
        [Range(0f, 1f)]
        public float mieDirectionalG = 0.8f;

        [Tooltip("Overall brightness of the skybox. Higher values are brighter.")]
        [Range(0f, 1f)]
        public float exposure = 0.3f;

        [Header("Stars")]
        public StarsSettings starsSettings = new StarsSettings();
    }

    [Serializable]
    public class CloudSettings
    {
        [Header("Shape & Coverage")]
        public bool enabled = true;
        public Texture2D cloudNoiseTexture;

        [Tooltip("How high the cloud layer appears. Affects perspective.")]
        [Range(1f, 10f)]
        public float altitude = 4.0f;

        [Tooltip("Overall scale of the cloud patterns. Higher = Smaller clouds.")]
        [Range(0.1f, 10f)]
        public float scale = 3.5f;

        [Tooltip("How much of the sky is covered by clouds.")]
        [Range(0f, 1f)]
        public float coverage = 0.5f;

        [Tooltip("The density or 'thickness' of the clouds.")]
        [Range(0.1f, 5f)]
        public float density = 1.0f;

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
        public Color shadowColor = new Color(0.4f, 0.45f, 0.5f);

        [Tooltip("How much light is scattered through the cloud (makes them brighter).")]
        [Range(0f, 5f)]
        public float lightScattering = 2.0f;
    }

    [Serializable]
    public class StarsSettings
    {
        [Tooltip("The texture used for the starfield. Should be in a longitude-latitude format.")]
        public Texture starsTexture;

        [Tooltip("The rotation speed of the starfield, relative to the day/night cycle.")]
        public float starsRotationSpeed = 0.5f;

        [Header("Twinkling Effect")]
        [Tooltip("The scale of the noise pattern. Larger values mean smaller, more numerous twinkle areas.")]
        [Range(1f, 300f)]
        public float twinkleScale = 150f;

        [Tooltip("Controls the 'crispness' of the twinkle effect. Higher values add more fine detail to the noise pattern.")]
        [Range(1, 5)]
        public int twinkleDetail = 3;

        [Tooltip("Controls the contrast of the twinkle noise. Higher values create sharper, more defined twinkling areas.")]
        [Range(0.1f, 20f)]
        public float twinkleSharpness = 5f;

        [Tooltip("The speed at which the twinkle effect evolves over time.")]
        [Range(0f, 2f)]
        public float twinkleSpeed = 0.7f;

        [Tooltip("The intensity of the twinkle effect. Values > 1.0 will cause stars to fade out completely, creating a stronger effect.")]
        [Range(0f, 2f)]
        public float twinkleStrength = 0.8f;
    }

    [Serializable]
    public class FogSettings
    {
        public bool enabled = true;

        [Tooltip("Color of the fog during the day.")]
        public Color dayColor = new Color(0.7f, 0.8f, 1f);

        [Tooltip("Color of the fog during the night.")]
        public Color nightColor = new Color(0.05f, 0.05f, 0.15f);

        [Tooltip("Density of the fog (for Exponential fog mode).")]
        [Range(0f, 0.1f)]
        public float density = 0.002f;
    }

    [Serializable]
    public class MoonSettings
    {
        [Tooltip("Texture for the moon disk (Equirectangular 2:1 projection supported).")]
        public Texture moonTexture;

        [Tooltip("Size of the moon in the sky.")]
        [Range(0.005f, 0.1f)]
        public float moonSize = 0.02f;

        [Tooltip("Visual tint of the moon disk.")]
        public Color moonColor = Color.white;
    }

    [Serializable]
    public class EffectsSettings
    {
        [Tooltip("The particle system prefab to instantiate for this weather (e.g., rain, snow). Can be null.")]
        public GameObject weatherEffectPrefab;

        [Tooltip("How high above the player the weather particles should start spawning from.")]
        [Range(5f, 50f)]
        public float spawnHeightOffset = 15f;

        [Tooltip("The maximum distance the raycast will check upwards for a ceiling. Should be greater than the spawn height.")]
        [Range(10f, 200f)]
        public float ceilingCheckDistance = 100f;
    }
}
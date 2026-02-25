using System;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// A composite preset that holds references to independent weather state profiles.
    /// Acts as a director's "Scene Preset" rather than a monolithic data container.
    /// </summary>
    [CreateAssetMenu(fileName = "New Weather Preset", menuName = "Horizon/Weather Preset")]
    public class WeatherProfile : ScriptableObject
    {
#if UNITY_EDITOR
        /// <summary>
        /// Triggered when any profile data changes.
        /// The argument is the specific WeatherProfile that changed, or null if a sub-module (like LightingProfile) changed.
        /// <para><b>WARNING:</b> Subscribers MUST unsubscribe (e.g., in OnDisable) to avoid memory leaks.</para>
        /// </summary>
        public static event Action<WeatherProfile> OnProfileDataChanged;
#endif

        [Header("General Settings")]
        public string profileName = "New Weather Preset";

        [HideInInspector]
        public GameObject udon_WeatherEffectPrefab;

        [Header("Decomposed Sub-Profiles")]
        [Tooltip("Controls sun, moon, and ambient lighting colors.")]
        public LightingProfile lightingProfile;

        [Tooltip("Controls atmospheric scattering and starfield.")]
        public SkyProfile skyProfile;

        [Tooltip("Controls volumetric clouds and wind.")]
        public CloudProfile cloudProfile;

        [Tooltip("Controls the moon texture, size, and tint.")]
        public MoonProfile moonProfile;

        [Tooltip("Controls horizon and global fog integration.")]
        public FogProfile fogProfile;

        [Tooltip("Controls particle systems like rain or snow.")]
        public EffectsProfile effectsProfile;

        private void OnValidate()
        {
            if (effectsProfile != null) udon_WeatherEffectPrefab = effectsProfile.weatherEffectPrefab;
            else udon_WeatherEffectPrefab = null;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= RequestEditorUpdate;
            UnityEditor.EditorApplication.delayCall += RequestEditorUpdate;
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Instance method: Triggers update specifically for THIS profile.
        /// </summary>
        public void RequestEditorUpdate()
        {
            OnProfileDataChanged?.Invoke(this);
        }

        /// <summary>
        /// Static Bridge: Triggers update for generic changes (sub-modules like Lighting/Sky).
        /// We pass null because sub-modules don't know their parent WeatherProfile.
        /// </summary>
        public static void InvokeGlobalUpdate()
        {
            OnProfileDataChanged?.Invoke(null);
        }
#endif
    }
}
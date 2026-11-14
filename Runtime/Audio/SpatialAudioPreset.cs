using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Reusable configuration for SpatialAudioEmitter components.
    ///
    /// Presets let you author a spatial behavior once (e.g. "rain on a metal roof",
    /// "crowd murmur", "machine hum") and apply it to any emitter in any system.
    /// </summary>
    [CreateAssetMenu(fileName = "New Spatial Audio Preset", menuName = "Horizon/Audio/Spatial Audio Preset")]
    public class SpatialAudioPreset : ScriptableObject
    {
        [Header("Audio")]
        public AudioClip clip;
        public bool loop = true;

        [Range(0f, 1f)]
        public float volume = 1f;

        [Range(0.25f, 2f)]
        public float pitch = 1f;

        [Header("Placement")]
        public SpatialAudioPlacementMode placement = SpatialAudioPlacementMode.OrbitListener;
        public Vector3 offset = Vector3.zero;

        [Min(0f)]
        public float orbitRadius = 2.5f;

        public float verticalOffset = 1.4f;
        public bool useTransformDirection = true;
        public bool useOpeningDepthDistance = true;
        public bool useSystemCeilingHeight = true;

        [Header("Acoustics")]
        public SpatialAudioCondition condition = SpatialAudioCondition.Always;

        [Range(0f, 1f)]
        public float minimumGain = 0f;

        [Range(0.1f, 4f)]
        public float responseSharpness = 1f;

        [Range(0f, 30f)]
        public float volumeSmoothing = 8f;

        [Header("Spatialization")]
        [Tooltip("Apply distance attenuation from the listener to the emitter.")]
        public bool useDistanceAttenuation = true;

        [Range(0f, 1f)]
        public float spatialBlend = 1f;

        [Range(-1f, 1f)]
        public float panStereo = 0f;

        [Min(0f)]
        public float minDistance = 1f;

        [Min(0.01f)]
        public float maxDistance = 16f;

        public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

        [Range(0f, 360f)]
        public float spread = 80f;

        [Range(0f, 5f)]
        public float dopplerLevel = 0f;

        /// <summary>
        /// Copies every preset field onto an emitter.
        /// </summary>
        public void ApplyTo(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return;

            emitter.clip = clip;
            emitter.loop = loop;
            emitter.volume = volume;
            emitter.pitch = pitch;

            emitter.placement = placement;
            emitter.offset = offset;
            emitter.orbitRadius = orbitRadius;
            emitter.verticalOffset = verticalOffset;
            emitter.useTransformDirection = useTransformDirection;
            emitter.useOpeningDepthDistance = useOpeningDepthDistance;
            emitter.useSystemCeilingHeight = useSystemCeilingHeight;

            emitter.condition = condition;
            emitter.minimumGain = minimumGain;
            emitter.responseSharpness = responseSharpness;
            emitter.volumeSmoothing = volumeSmoothing;

            emitter.spatialBlend = spatialBlend;
            emitter.panStereo = panStereo;
            emitter.useDistanceAttenuation = useDistanceAttenuation;
            emitter.minDistance = minDistance;
            emitter.maxDistance = maxDistance;
            emitter.rolloffMode = rolloffMode;
            emitter.spread = spread;
            emitter.dopplerLevel = dopplerLevel;
        }
    }
}

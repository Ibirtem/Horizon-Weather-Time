using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// A single positional audio emitter driven by a SpatialAudioSystem.
    ///
    /// Emitters are intentionally generic: they can represent weather stems, insects,
    /// machinery, voices, music, or any other sound that should react to the listener's
    /// acoustic environment. The owning system decides when and how loud the emitter plays.
    /// </summary>
    [AddComponentMenu("Horizon/Spatial Audio Emitter")]
#if UDONSHARP
    public class SpatialAudioEmitter : UdonSharpBehaviour
#else
    public class SpatialAudioEmitter : MonoBehaviour
#endif
    {
        [Header("Audio")]
        [Tooltip("Looping clip played by this emitter.")]
        public AudioClip clip;

        [Tooltip("Optional explicit AudioSource. Leave empty to use the AudioSource on this object, then a child AudioSource.")]
        public AudioSource sourceOverride;

        [Tooltip("Keep the clip looping. Disable for one-shot emitters.")]
        public bool loop = true;

        [Range(0f, 1f)]
        [Tooltip("Base loudness before the acoustic environment is applied.")]
        public float volume = 1f;

        [Range(0.25f, 2f)]
        [Tooltip("Playback pitch.")]
        public float pitch = 1f;

        [Header("Placement")]
        [Tooltip("How this emitter is positioned relative to the listener.")]
        public SpatialAudioPlacementMode placement = SpatialAudioPlacementMode.OrbitListener;

        [Tooltip("Offset used by ListenerRelative placement, or the fallback direction for Orbit placement.")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Horizontal orbit radius in meters.")]
        [Min(0f)]
        public float orbitRadius = 2.5f;

        [Tooltip("Vertical offset above the listener for orbit, overhead, and opening emitters.")]
        public float verticalOffset = 1.4f;

        [Tooltip("When enabled, the emitter's own Y rotation defines the orbit direction. Otherwise the offset vector is used.")]
        public bool useTransformDirection = true;

        [Tooltip("For Opening Direction placement: use the system's estimated distance to the opening instead of the fixed orbit radius. This makes the source get louder as you approach the doorway.")]
        public bool useOpeningDepthDistance = true;

        [Tooltip("For Above Listener placement: use the probed ceiling height instead of the fixed vertical offset. Higher ceilings become farther and quieter.")]
        public bool useSystemCeilingHeight = true;

        [Header("Acoustics")]
        [Tooltip("Environment condition that gates this emitter.")]
        public SpatialAudioCondition condition = SpatialAudioCondition.Always;

        [Range(0f, 1f)]
        [Tooltip("Residual gain when the environmental condition is not met. 0 = fully silent.")]
        public float minimumGain = 0f;

        [Range(0.1f, 4f)]
        [Tooltip("Sharpens the environmental response. 1 = linear, higher = more binary.")]
        public float responseSharpness = 1f;

        [Range(0f, 30f)]
        [Tooltip("How quickly the emitter's loudness follows environment changes.")]
        public float volumeSmoothing = 8f;

        [Header("Spatialization")]
        [Tooltip("Apply distance attenuation from the listener to the resolved emitter position. Disable for 2D beds.")]
        public bool useDistanceAttenuation = true;

        [Range(0f, 1f)]
        [Tooltip("0 = 2D stereo bed, 1 = fully spatialized 3D source.")]
        public float spatialBlend = 1f;

        [Range(-1f, 1f)]
        [Tooltip("Stereo pan for 2D beds. Useful for splitting a layer into Left/Right emitters.")]
        public float panStereo = 0f;

        [Min(0f)]
        [Tooltip("Distance at which the source starts attenuating. This drives the spatial system's distance model; the raw AudioSource curve is flattened to avoid double attenuation.")]
        public float minDistance = 1f;

        [Min(0.01f)]
        [Tooltip("Distance at which the source becomes inaudible. This drives the spatial system's distance model; the raw AudioSource curve is flattened to avoid double attenuation.")]
        public float maxDistance = 16f;

        [Tooltip("Distance curve used by the spatial system. Logarithmic falls off faster than Linear.")]
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

        [Range(0f, 360f)]
        [Tooltip("Stereo spread for spatialized sources.")]
        public float spread = 80f;

        [Range(0f, 5f)]
        [Tooltip("Doppler effect strength. Usually 0 for ambience.")]
        public float dopplerLevel = 0f;

        [Tooltip("Optional preset asset. Use the Apply Preset button to copy its settings.")]
        [HideInInspector] public UnityEngine.Object presetAsset;

        // ---------------------------------------------------------------
        // Runtime state
        // ---------------------------------------------------------------

        private bool _externallyDriven = false;
        private AudioClip _externalClip = null;
        private float _externalVolume = 0f;
        private float _externalPitch = 1f;

        private AudioSource _source;
        private float _currentVolume = 0f;
        private float _targetVolume = 0f;
        private float _conditionFactor = 0f;
        private float _distanceGain = 1f;
        private float _resolvedDistance = 0f;
        private Vector3 _resolvedPosition = Vector3.zero;

        // ---------------------------------------------------------------
        // Public read-only state (used by inspectors and visualizers)
        // ---------------------------------------------------------------

        public bool IsExternallyDriven { get { return _externallyDriven; } }
        public float CurrentVolume { get { return _currentVolume; } }
        public float TargetVolume { get { return _targetVolume; } }
        public float ConditionFactor { get { return _conditionFactor; } }
        public float DistanceGain { get { return _distanceGain; } }
        public float ResolvedDistance { get { return _resolvedDistance; } }
        public Vector3 ResolvedPosition { get { return _resolvedPosition; } }

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        private void OnEnable()
        {
            EnsureSource();
            ConfigureSource();
        }

        private void OnDisable()
        {
            StopPlayback();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            AudioSource source = sourceOverride;
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = GetComponentInChildren<AudioSource>(true);

            if (source != null)
            {
                if (sourceOverride == null) sourceOverride = source;
                _source = source;
                ConfigureSource();
            }
        }
#endif

        /// <summary>
        /// Locates the AudioSource used by this emitter. In the editor a missing source is
        /// created automatically so the component is pleasant to drop on an object.
        /// </summary>
        public void EnsureSource()
        {
            if (_source != null) return;

            if (sourceOverride != null)
            {
                _source = sourceOverride;
                return;
            }

            _source = GetComponent<AudioSource>();

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (_source == null && !Application.isPlaying)
            {
                // Support a legacy/child AudioSource layout. The serialized override then
                // keeps the runtime path valid without searching at runtime.
                _source = GetComponentInChildren<AudioSource>(true);
                if (_source != null)
                {
                    sourceOverride = _source;
                    return;
                }
            }

            if (_source == null && !Application.isPlaying)
            {
                _source = gameObject.AddComponent<AudioSource>();
                sourceOverride = _source;
            }
#endif
        }

        /// <summary>
        /// Copies the emitter's spatial settings to its AudioSource.
        /// </summary>
        public void ConfigureSource()
        {
            EnsureSource();
            if (_source == null) return;

            _source.playOnAwake = false;
            _source.loop = loop;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            _source.mute = false;
            _source.dopplerLevel = dopplerLevel;
            _source.spatialBlend = spatialBlend;
            _source.spread = spread;
            _source.panStereo = panStereo;

            // The system owns distance attenuation. Flatten the raw AudioSource curve so
            // Unity/VRChat do not apply a second, offset-dependent attenuation.
            _source.rolloffMode = AudioRolloffMode.Linear;
            if (spatialBlend > 0.001f)
            {
                _source.minDistance = 1000f;
                _source.maxDistance = 1001f;
            }
            else
            {
                _source.minDistance = 0f;
                _source.maxDistance = 1f;
            }

            _source.spatialize = spatialBlend > 0.001f;
#endif
        }

        // ---------------------------------------------------------------
        // External driver API (used by weather and other systems)
        // ---------------------------------------------------------------

        /// <summary>
        /// Puts the emitter under external control. While externally driven, the base
        /// volume, pitch, and clip come from the driver instead of the emitter fields.
        /// </summary>
        public void SetExternalDrive(AudioClip driveClip, float driveVolume, float drivePitch)
        {
            _externallyDriven = true;
            _externalClip = driveClip;
            _externalVolume = Mathf.Clamp01(driveVolume);
            _externalPitch = drivePitch;
        }

        /// <summary>
        /// Returns the emitter to its own clip/volume/condition settings.
        /// </summary>
        public void ClearExternalDrive()
        {
            _externallyDriven = false;
            _externalClip = null;
            _externalVolume = 0f;
            _externalPitch = 1f;
        }

        // ---------------------------------------------------------------
        // System tick
        // ---------------------------------------------------------------

        /// <summary>
        /// Updates placement, gain, and playback for one frame.
        ///
        /// @param dt Frame delta time.
        /// @param listenerPosition Current listener/head position.
        /// @param masterVolume System master gain.
        /// @param allowAudio When false, only visual state is updated and no sound is started.
        /// @param moveTransform When true, 3D emitters move their transform to the resolved position.
        /// @param conditionFactor Environment match in 0..1 supplied by the owning system.
        /// @param openingDirection Smoothed direction of the strongest opening.
        /// @param openingDepth Estimated distance from the listener to the nearest opening edge.
        /// @param ceilingHeight Probed ceiling height above the listener, or 0 when there is no ceiling.
        /// </summary>
        public void SystemTick(
            float dt, Vector3 listenerPosition, float masterVolume,
            bool allowAudio, bool moveTransform,
            float conditionFactor, Vector3 openingDirection, float openingDepth, float ceilingHeight)
        {
            if (_source == null) EnsureSource();

            _conditionFactor = Mathf.Clamp01(conditionFactor);
            _resolvedPosition = ResolvePosition(listenerPosition, openingDirection, openingDepth, ceilingHeight);
            _resolvedDistance = (listenerPosition - _resolvedPosition).magnitude;

            bool is3D = spatialBlend > 0.01f;
            _distanceGain = (useDistanceAttenuation && is3D) ? EvaluateDistanceGain(_resolvedDistance) : 1f;

            if (moveTransform && spatialBlend > 0.001f && placement != SpatialAudioPlacementMode.FixedWorld)
            {
                if ((_resolvedPosition - transform.position).sqrMagnitude > 0.0001f)
                {
                    transform.position = _resolvedPosition;
                }
            }

            float environmentGain = 1f;
            if (!_externallyDriven)
            {
                float shaped = Mathf.Approximately(responseSharpness, 1f)
                    ? _conditionFactor
                    : Mathf.Pow(_conditionFactor, Mathf.Max(0.01f, responseSharpness));
                environmentGain = Mathf.Lerp(minimumGain, 1f, shaped);
            }

            float baseVolume = _externallyDriven ? _externalVolume : volume;
            float target = Mathf.Clamp01(baseVolume * environmentGain * _distanceGain * masterVolume);
            _targetVolume = target;

            if (allowAudio)
            {
                float smoothing = volumeSmoothing <= 0.01f ? 1f : 1f - Mathf.Exp(-volumeSmoothing * dt);
                _currentVolume = Mathf.Lerp(_currentVolume, target, smoothing);
            }
            else
            {
                _currentVolume = target;
            }

            AudioClip playClip = _externallyDriven ? _externalClip : clip;
            float playPitch = _externallyDriven ? _externalPitch : pitch;

            if (allowAudio)
            {
                ApplySource(playClip, _currentVolume, playPitch);
            }
            else
            {
                StopPlayback();
            }
        }

        /// <summary>
        /// Distance attenuation used by the spatial system.
        /// The AudioSource curve itself is flattened in the editor so this is the only
        /// distance attenuation applied. That keeps the mix identical in Unity and VRChat
        /// and makes the inspector meters reflect what is actually heard.
        /// </summary>
        private float EvaluateDistanceGain(float distance)
        {
            float min = Mathf.Max(0.01f, minDistance);
            float max = Mathf.Max(min + 0.01f, maxDistance);
            float d = Mathf.Max(0f, distance);

            if (rolloffMode == AudioRolloffMode.Logarithmic)
            {
                return Mathf.Clamp01(min / Mathf.Max(min, d));
            }

            // Linear (and Custom fallback): full volume at minDistance, silent at maxDistance.
            return Mathf.Clamp01((max - d) / (max - min));
        }

        /// <summary>
        /// Resolves the emitter's world-space position for the current placement mode.
        /// </summary>
        private Vector3 ResolvePosition(Vector3 listenerPosition, Vector3 openingDirection, float openingDepth, float ceilingHeight)
        {
            switch (placement)
            {
                case SpatialAudioPlacementMode.FixedWorld:
                    return transform.position;

                case SpatialAudioPlacementMode.ListenerRelative:
                    return listenerPosition + offset;

                case SpatialAudioPlacementMode.AboveListener:
                {
                    float height = verticalOffset;
                    if (useSystemCeilingHeight && ceilingHeight > 0.01f)
                    {
                        height = Mathf.Max(0.2f, ceilingHeight);
                    }
                    return listenerPosition + new Vector3(offset.x, height, offset.z);
                }

                case SpatialAudioPlacementMode.OpeningDirection:
                {
                    Vector3 dir = openingDirection;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    dir = dir.normalized;

                    float radius = orbitRadius;
                    if (useOpeningDepthDistance)
                    {
                        radius = Mathf.Max(orbitRadius, openingDepth);
                    }

                    return listenerPosition + dir * radius + new Vector3(0f, verticalOffset, 0f);
                }

                default:
                {
                    Vector3 dir = useTransformDirection ? transform.forward : offset;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    dir = dir.normalized;
                    return listenerPosition + dir * orbitRadius + new Vector3(0f, verticalOffset, 0f);
                }
            }
        }

        // ---------------------------------------------------------------
        // Playback helpers
        // ---------------------------------------------------------------

        private void ApplySource(AudioClip playClip, float playVolume, float playPitch)
        {
            if (_source == null) return;

            if (playClip == null)
            {
                StopPlayback();
                return;
            }

            if (_source.clip != playClip)
            {
                _source.clip = playClip;
                _source.Stop();
            }

            float clampedVolume = Mathf.Clamp01(playVolume);

            // A tiny non-zero floor keeps looped sources alive in systems that cull zero-volume voices.
            float targetVol = loop ? Mathf.Max(0.0001f, clampedVolume) : clampedVolume;
            if (Mathf.Abs(_source.volume - targetVol) > 0.0002f) _source.volume = targetVol;
            if (Mathf.Abs(_source.pitch - playPitch) > 0.0005f) _source.pitch = playPitch;

            if (loop)
            {
                // Keep looping ambience running at zero volume instead of stopping and
                // restarting it. This prevents audible cuts when a condition (roof,
                // opening, distance) crosses the silence threshold for a moment.
                if (!_source.isPlaying) _source.Play();
            }
            else if (playVolume > 0.001f)
            {
                if (!_source.isPlaying) _source.Play();
            }
            else
            {
                StopPlayback();
            }
        }

        /// <summary>
        /// Stops playback and silences the source without destroying its clip assignment.
        /// </summary>
        public void StopPlayback()
        {
            if (_source == null) return;

            if (_source.isPlaying) _source.Stop();
            _source.volume = 0f;
        }
    }
}

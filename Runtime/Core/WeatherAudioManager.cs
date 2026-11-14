using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Logical role of a weather-driven spatial emitter.
    /// The role is data, not a GameObject name, so the rig can be renamed, reordered,
    /// or rebuilt without breaking the weather mix.
    /// </summary>
    public enum WeatherAudioStemType
    {
        RainExterior = 0,
        RainInterior = 1,
        RainOpening = 2,
        RainRoof = 3,
        WindBase = 4,
        WindGust = 5
    }

    /// <summary>
    /// Weather audio adapter.
    ///
    /// Weather owns only its mix data and the stem-to-emitter mapping. Spatialization,
    /// distance attenuation, acoustic probing, and playback belong to SpatialAudioSystem.
    /// Emitters are bound through stemEmitters + stemTypes, never through child names.
    /// </summary>
    [ExecuteInEditMode]
#if UDONSHARP
    public class WeatherAudioManager : UdonSharpBehaviour
#else
    public class WeatherAudioManager : MonoBehaviour
#endif
    {
        [Header("Spatial Audio Backend (Auto-Assigned)")]
        [Tooltip("The generic spatial audio system that positions and plays the weather emitters.")]
        public SpatialAudioSystem audioSystem;

        [Header("Weather Stem Bindings (Auto-Assigned)")]
        [Tooltip("Emitters driven by weather. Order matches stemTypes. Child object names are irrelevant.")]
        public SpatialAudioEmitter[] stemEmitters;

        [Tooltip("Logical role for each entry in stemEmitters. Stored as ints for Udon compatibility.")]
        public int[] stemTypes;

        [Header("Doorway Blend")]
        [Range(0f, 1f)]
        [Tooltip("How much of the outdoor rain bed bleeds in at the nearest opening, on top of the base bleed.")]
        public float openingRainBleed = 0.72f;

        [Range(0f, 1f)]
        [Tooltip("How strongly the directional opening emitter ramps up as you approach the doorway.")]
        public float openingDirectionalGain = 0.85f;

        [Header("Wind Level")]
        [Tooltip("Use the wind volume from the active AudioProfile. Disable to keep wind identical across all weather profiles.")]
        public bool useProfileWindVolume = false;

        [Range(0f, 1f)]
        [Tooltip("Shared wind bed level used when Use Profile Wind Volume is disabled.")]
        public float windVolume = 0.18f;

        [Tooltip("Use the gust strength from the active AudioProfile. Disable to keep gust dynamics identical across profiles.")]
        public bool useProfileWindGustStrength = false;

        [Range(0f, 1f)]
        [Tooltip("Shared gust strength used when Use Profile Wind Gust Strength is disabled.")]
        public float windGustStrength = 0.35f;

        // Cached weather parameters from the active AudioProfile.
        private bool _audioEnabled = false;
        private float _masterVolume = 1f;
        private float _rainVolume = 0f;
        private float _rainInteriorScale = 0.45f;
        private float _rainRoofScale = 0.65f;
        private float _rainPitch = 1f;
        private float _windVolume = 0f;
        private float _windGustStrength = 0.35f;
        private float _windGustSpeed = 0.25f;
        private float _windInteriorScale = 0.25f;
        private float _windPitch = 1f;

        private AudioClip _clipRainExtStereo;
        private AudioClip _clipRainExtMono;
        private AudioClip _clipRainIntStereo;
        private AudioClip _clipRainRoofMono;
        private AudioClip _clipWindBaseStereo;
        private AudioClip _clipWindGustStereo;

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            SilenceAll();
        }

        /**
         * Resolves the spatial backend and the stem arrays. The runtime never searches
         * for emitters by child name; the editor owns the migration and binding.
         */
        private void ResolveReferences()
        {
            if (audioSystem == null) audioSystem = GetComponent<SpatialAudioSystem>();

            if (stemEmitters == null || stemEmitters.Length == 0 ||
                stemTypes == null || stemTypes.Length != stemEmitters.Length)
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                // In edit mode the inspector migration owns the binding. Do not guess
                // roles from hierarchy order and poison the serialized arrays.
                if (!Application.isPlaying) return;
#endif
                ResolveStemFallback();
            }
        }

        /**
         * Last-resort discovery for old scenes without serialized stem arrays.
         * Roles are assigned in hierarchy order; the editor migration does a proper bind.
         */
        private void ResolveStemFallback()
        {
            int childCount = transform.childCount;
            SpatialAudioEmitter[] found = new SpatialAudioEmitter[childCount];
            int count = 0;

            for (int i = 0; i < childCount; i++)
            {
                SpatialAudioEmitter emitter = transform.GetChild(i).GetComponent<SpatialAudioEmitter>();
                if (emitter != null)
                {
                    found[count] = emitter;
                    count++;
                }
            }

            if (count == 0)
            {
                stemEmitters = null;
                stemTypes = null;
                return;
            }

            SpatialAudioEmitter[] trimmed = new SpatialAudioEmitter[count];
            int[] types = new int[count];

            for (int i = 0; i < count; i++)
            {
                trimmed[i] = found[i];

                // Legacy rigs were created in this hierarchy order:
                // RainExterior, RainInterior, WindBase, WindGust, RainOpening, RainRoof.
                // The editor migration binds by role; this is only a last-resort fallback.
                if (i == 0) types[i] = (int)WeatherAudioStemType.RainExterior;
                else if (i == 1) types[i] = (int)WeatherAudioStemType.RainInterior;
                else if (i == 2) types[i] = (int)WeatherAudioStemType.WindBase;
                else if (i == 3) types[i] = (int)WeatherAudioStemType.WindGust;
                else if (i == 4) types[i] = (int)WeatherAudioStemType.RainOpening;
                else if (i == 5) types[i] = (int)WeatherAudioStemType.RainRoof;
                else types[i] = (int)WeatherAudioStemType.RainExterior;
            }

            stemEmitters = trimmed;
            stemTypes = types;
        }

        /**
         * Updates the listener reference tracked by the spatial audio system.
         *
         * @param headPosition World-space position of the player head or active camera.
         */
        public void UpdateAudioPosition(Vector3 headPosition)
        {
            if (audioSystem != null)
            {
                audioSystem.SetListenerPosition(headPosition);
            }
        }

        /**
         * Receives the active weather audio mix and drives the bound stem emitters.
         * Spatial positions, acoustic gating, distance attenuation, and playback are
         * handled by SpatialAudioSystem.
         */
        public void UpdateAudio(
            bool isEnabled, float masterVol,
            AudioClip rainExtStereo, AudioClip rainExtMono, AudioClip rainIntStereo, AudioClip rainRoofMono,
            AudioClip windBaseStereo, AudioClip windGustStereo,
            float rainVol, float rainIntScale, float rainRoofScale, float rainPitch,
            float windVol, float windGustStr, float windGustSpd, float windIntScale, float windPitch)
        {
            _audioEnabled = isEnabled;
            _masterVolume = masterVol;

            _clipRainExtStereo = rainExtStereo;
            _clipRainExtMono = rainExtMono;
            _clipRainIntStereo = rainIntStereo;
            _clipRainRoofMono = rainRoofMono;
            _clipWindBaseStereo = windBaseStereo;
            _clipWindGustStereo = windGustStereo;

            _rainVolume = rainVol;
            _rainInteriorScale = rainIntScale;
            _rainRoofScale = rainRoofScale;
            _rainPitch = rainPitch;

            _windVolume = windVol;
            _windGustStrength = windGustStr;
            _windGustSpeed = windGustSpd;
            _windInteriorScale = windIntScale;
            _windPitch = windPitch;

            if (!_audioEnabled || _masterVolume <= 0.001f)
            {
                SilenceAll();
                return;
            }

            SolveWeatherMix();
        }

        /**
         * Converts the generic acoustic environment into stem gains. Weather-specific
         * taste (rain vs wind balance, gust curves, interior scaling) stays here; the
         * reusable spatial engine stays in SpatialAudioSystem.
         */
        private void SolveWeatherMix()
        {
            if (stemEmitters == null || stemEmitters.Length == 0 ||
                stemTypes == null || stemTypes.Length != stemEmitters.Length)
            {
                ResolveReferences();
            }

            if (stemEmitters == null || stemEmitters.Length == 0) return;

            float envSky = audioSystem != null ? audioSystem.SkyExposure : 1f;
            float envSides = audioSystem != null ? audioSystem.LateralOpenness : 1f;
            float envWall = audioSystem != null ? audioSystem.ExteriorWallExposure : 1f;
            float envRoof = audioSystem != null ? audioSystem.RoofExposure : 0f;
            float envOpening = audioSystem != null ? audioSystem.OpeningStrength : 0f;
            float envProximity = audioSystem != null ? audioSystem.OpeningProximity : 0f;
            float minIndoor = audioSystem != null ? audioSystem.minimumIndoorGain : 0.05f;

            float wallTransmission = Mathf.Lerp(minIndoor, 1f, Mathf.Max(envWall, envSides));

            // --- Rain stem gains ---
            float baseRain = _rainVolume * _masterVolume;

            // Outdoor rain bleeds in as the listener approaches an opening, before the
            // sky probe flips to "outside". This is what removes the doorway volume blast.
            float openingBleed = 0.08f + Mathf.Clamp01(openingRainBleed) * Mathf.Clamp01(envProximity);
            float outdoorBleed = envOpening * (1f - envSky) * openingBleed;
            float ext2DFactor = Mathf.Clamp01(envSky + outdoorBleed);
            float ext2DVol = baseRain * ext2DFactor;

            // Directional opening source ramps with proximity and sits at the estimated
            // opening distance, so it gets closer/louder as you approach the doorway.
            float opening3DFactor = Mathf.Clamp01(envOpening * (1f - envSky) * (0.15f + Mathf.Clamp01(openingDirectionalGain) * Mathf.Clamp01(envProximity)));
            float opening3DVol = baseRain * opening3DFactor * 0.9f;

            // 2D muffled interior rain crossfades out as the outdoor component takes over.
            float enclosureFactor = 1f - ext2DFactor;
            float int2DVol = baseRain * enclosureFactor * _rainInteriorScale * wallTransmission;

            // 3D overhead roof patter activates strictly for single-story roofs.
            float roof3DVol = baseRain * envRoof * _rainRoofScale;

            // --- Wind stem gains + multi-sine organic gusts ---
            float t = Time.time * _windGustSpeed;
            float gustWave = 0.5f
                + 0.28f * Mathf.Sin(t * 1.13f)
                + 0.14f * Mathf.Sin(t * 2.71f + 1.3f)
                + 0.08f * Mathf.Sin(t * 4.19f + 2.7f);
            gustWave = Mathf.Clamp01(gustWave);

            float windOutdoor = Mathf.Max(envSky, Mathf.Max(envSides * 0.7f, envOpening * Mathf.Clamp01(envProximity)));
            float windExposure = Mathf.Lerp(_windInteriorScale * wallTransmission, 1f, windOutdoor);
            float windLevel = useProfileWindVolume ? _windVolume : (windVolume > 0.00001f ? windVolume : 0.18f);
            float sharedGustStrength = windGustStrength > 0.00001f ? windGustStrength : 0.35f;
            float baseWind = windLevel * _masterVolume * windExposure;
            float gustStrength = useProfileWindGustStrength ? _windGustStrength : sharedGustStrength;
            float windBaseVol = baseWind * (1f - gustStrength * 0.25f + gustWave * gustStrength * 0.35f);
            float windGustVol = baseWind * gustStrength * gustWave;
            float gustPitch = _windPitch * Mathf.Lerp(0.95f, 1.08f, gustWave);

            for (int i = 0; i < stemEmitters.Length; i++)
            {
                SpatialAudioEmitter emitter = stemEmitters[i];
                if (emitter == null) continue;

                int rawType = i < stemTypes.Length ? stemTypes[i] : 0;
                WeatherAudioStemType type = (WeatherAudioStemType)Mathf.Clamp(rawType, 0, 5);

                AudioClip clip = null;
                float volume = 0f;
                float pitch = _rainPitch;

                switch (type)
                {
                    case WeatherAudioStemType.RainExterior:
                        clip = _clipRainExtStereo;
                        volume = ext2DVol;
                        pitch = _rainPitch;
                        break;

                    case WeatherAudioStemType.RainInterior:
                        clip = _clipRainIntStereo;
                        volume = int2DVol;
                        pitch = _rainPitch;
                        break;

                    case WeatherAudioStemType.RainOpening:
                        clip = _clipRainExtMono;
                        volume = opening3DVol;
                        pitch = _rainPitch;
                        break;

                    case WeatherAudioStemType.RainRoof:
                        clip = _clipRainRoofMono;
                        volume = roof3DVol;
                        pitch = _rainPitch;
                        break;

                    case WeatherAudioStemType.WindBase:
                        clip = _clipWindBaseStereo;
                        volume = windBaseVol;
                        pitch = _windPitch;
                        break;

                    case WeatherAudioStemType.WindGust:
                        clip = _clipWindGustStereo;
                        volume = windGustVol;
                        pitch = gustPitch;
                        break;
                }

                DriveEmitter(emitter, clip, volume, pitch);
            }
        }

        private void DriveEmitter(SpatialAudioEmitter emitter, AudioClip clip, float volume, float pitch)
        {
            if (emitter == null) return;
            emitter.SetExternalDrive(clip, volume, pitch);
        }

        private void SilenceAll()
        {
            if (stemEmitters == null) return;

            for (int i = 0; i < stemEmitters.Length; i++)
            {
                DriveEmitter(stemEmitters[i], null, 0f, 1f);
            }
        }
    }
}

using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    [ExecuteInEditMode]
#if UDONSHARP
    public class WeatherAudioManager : UdonSharpBehaviour
#else
    public class WeatherAudioManager : MonoBehaviour
#endif
    {
        [Header("2D Stereo Sources (SpatialBlend = 0)")]
        public AudioSource rainExterior2D;
        public AudioSource rainInterior2D;
        public AudioSource windBase2D;
        public AudioSource windGust2D;

        [Header("3D Directional Mono Sources (SpatialBlend = 1)")]
        public AudioSource rainOpening3D;
        public AudioSource rainRoof3D;

        [Header("Acoustic Probe Configuration")]
        [Tooltip("Maximum distance for upward ceiling and sky-down roof probes.")]
        public float verticalProbeDistance = 60.0f;

        [Tooltip("Maximum horizontal distance to check for walls and open doorways.")]
        public float lateralProbeDistance = 18.0f;

        [Tooltip("Distance at which the directional opening 3D emitter orbits the listener head.")]
        public float openingEmitterRadius = 2.5f;

        [Tooltip("Max vertical thickness (m) between ceiling and top roof to count as a single-story roof.")]
        public float singleRoofMaxThickness = 1.8f;

        // Layer mask excluding IgnoreRaycast(2), UI(5), Player(9), PlayerLocal(10), MirrorReflection(18)
        private int _raycastMask = ~((1 << 2) | (1 << 5) | (1 << 9) | (1 << 10) | (1 << 18));

        // Round-robin state (14 probes total, 2 executed per frame)
        // 0: Zenith (+Y), 1..4: 45-deg sky cone, 5: Sky-down multi-story check,
        // 6..9: Cardinal wall probes (N,E,S,W), 10..13: Outside-wall sky-down probes (N,E,S,W)
        private const int TOTAL_PROBES = 14;
        private int _probeCursor = 0;

        private float _zenithOpen = 1f;
        private bool _hasCeiling = false;
        private float _ceilingY = 0f;
        private bool _hasTopRoof = false;
        private float _topRoofY = 0f;

        private float[] _coneOpen;
        private float[] _sideOpen;
        private float[] _sideWallDist;
        private float[] _outsideSkyExposed;

        private Vector3[] _coneDirs;
        private Vector3[] _cardinalDirs;

        // Smoothed acoustic state
        private float _smoothDirectSky = 1f;
        private float _smoothOpenSides = 1f;
        private float _smoothExteriorWall = 1f;
        private float _smoothRoofExposed = 0f;
        private float _smoothOpeningAsymmetry = 0f;
        private Vector3 _smoothOpenDir = Vector3.forward;

        // Cached audio profile parameters
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
        private float _minBunkerScale = 0.05f;
        private float _acousticSmoothing = 3.5f;

        private AudioClip _clipRainExtStereo;
        private AudioClip _clipRainExtMono;
        private AudioClip _clipRainIntStereo;
        private AudioClip _clipRainRoofMono;
        private AudioClip _clipWindBaseStereo;
        private AudioClip _clipWindGustStereo;

        private Vector3 _listenerPos;
        private bool _initialized = false;

        private void OnEnable()
        {
            InitializeProbes();
            ResolveAudioSources();
        }

        private void Start()
        {
            InitializeProbes();
            ResolveAudioSources();
        }

        /**
         * Resolves child AudioSource components by name if serialized references were cleared.
         */
        private void ResolveAudioSources()
        {
            if (rainExterior2D == null) rainExterior2D = FindChildAudioSource("Audio_Rain_Exterior_2D");
            if (rainInterior2D == null) rainInterior2D = FindChildAudioSource("Audio_Rain_Interior_2D");
            if (windBase2D == null)     windBase2D = FindChildAudioSource("Audio_Wind_Base_2D");
            if (windGust2D == null)     windGust2D = FindChildAudioSource("Audio_Wind_Gust_2D");
            if (rainOpening3D == null)  rainOpening3D = FindChildAudioSource("Audio_Rain_Opening_3D");
            if (rainRoof3D == null)     rainRoof3D = FindChildAudioSource("Audio_Rain_Roof_3D");
        }

        private AudioSource FindChildAudioSource(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null) return null;
            return child.GetComponent<AudioSource>();
        }

        /**
         * Pre-allocates probe direction vectors and state arrays to guarantee zero GC allocations per frame.
         */
        private void InitializeProbes()
        {
            if (_initialized) return;

            _coneOpen = new float[4] { 1f, 1f, 1f, 1f };
            _sideOpen = new float[4] { 1f, 1f, 1f, 1f };
            _sideWallDist = new float[4] { lateralProbeDistance, lateralProbeDistance, lateralProbeDistance, lateralProbeDistance };
            _outsideSkyExposed = new float[4] { 1f, 1f, 1f, 1f };

            _cardinalDirs = new Vector3[4]
            {
                Vector3.forward,
                Vector3.right,
                Vector3.back,
                Vector3.left
            };

            const float invSqrt2 = 0.70710678f;
            _coneDirs = new Vector3[4]
            {
                new Vector3(0f, invSqrt2, invSqrt2),
                new Vector3(invSqrt2, invSqrt2, 0f),
                new Vector3(0f, invSqrt2, -invSqrt2),
                new Vector3(-invSqrt2, invSqrt2, 0f)
            };

            _listenerPos = transform.position;
            _initialized = true;
        }

        /**
         * Updates the listener reference position tracked by acoustic probes and 3D emitters.
         *
         * @param headPosition World-space position of the player head or active camera.
         */
        public void UpdateAudioPosition(Vector3 headPosition)
        {
            _listenerPos = headPosition;
        }

        /**
         * Dispatches current audio profile settings and clips from WeatherTimeSystem.
         * Silences all sources automatically while in Edit Mode.
         */
        public void UpdateAudio(
            bool isEnabled, float masterVol,
            AudioClip rainExtStereo, AudioClip rainExtMono, AudioClip rainIntStereo, AudioClip rainRoofMono,
            AudioClip windBaseStereo, AudioClip windGustStereo,
            float rainVol, float rainIntScale, float rainRoofScale, float rainPitch,
            float windVol, float windGustStr, float windGustSpd, float windIntScale, float windPitch,
            float minBunkerScale, float smoothing)
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

            _minBunkerScale = minBunkerScale;
            _acousticSmoothing = smoothing;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                StopAllSources();
                return;
            }
#endif
        }

        private void Update()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            if (!_initialized) InitializeProbes();

            if (!_audioEnabled || _masterVolume <= 0.001f)
            {
                StopAllSources();
                return;
            }

            StepAcousticProbes(2);
            EvaluateAndApplyMix(Time.deltaTime);
        }

        /**
         * Executes a fixed budget of round-robin raycasts per frame to evaluate sky exposure,
         * multi-story roof thickness, lateral openings, and exterior wall proximity.
         *
         * @param raysPerFrame Number of raycasts to advance this frame.
         */
        private void StepAcousticProbes(int raysPerFrame)
        {
            Vector3 origin = _listenerPos + new Vector3(0f, 0.1f, 0f);
            RaycastHit hit;

            for (int r = 0; r < raysPerFrame; r++)
            {
                int idx = _probeCursor;
                _probeCursor = (_probeCursor + 1) % TOTAL_PROBES;

                if (idx == 0)
                {
                    if (Physics.Raycast(origin, Vector3.up, out hit, verticalProbeDistance, _raycastMask, QueryTriggerInteraction.Ignore))
                    {
                        _zenithOpen = 0f;
                        _hasCeiling = true;
                        _ceilingY = hit.point.y;
                    }
                    else
                    {
                        _zenithOpen = 1f;
                        _hasCeiling = false;
                        _ceilingY = origin.y + verticalProbeDistance;
                    }
                }
                else if (idx >= 1 && idx <= 4)
                {
                    int c = idx - 1;
                    bool blocked = Physics.Raycast(origin, _coneDirs[c], verticalProbeDistance, _raycastMask, QueryTriggerInteraction.Ignore);
                    _coneOpen[c] = blocked ? 0f : 1f;
                }
                else if (idx == 5)
                {
                    Vector3 skyOrigin = new Vector3(origin.x, origin.y + verticalProbeDistance, origin.z);
                    if (Physics.Raycast(skyOrigin, Vector3.down, out hit, verticalProbeDistance + 5f, _raycastMask, QueryTriggerInteraction.Ignore))
                    {
                        _hasTopRoof = true;
                        _topRoofY = hit.point.y;
                    }
                    else
                    {
                        _hasTopRoof = false;
                        _topRoofY = origin.y;
                    }
                }
                else if (idx >= 6 && idx <= 9)
                {
                    int k = idx - 6;
                    if (Physics.Raycast(origin, _cardinalDirs[k], out hit, lateralProbeDistance, _raycastMask, QueryTriggerInteraction.Ignore))
                    {
                        _sideOpen[k] = 0f;
                        _sideWallDist[k] = hit.distance;
                    }
                    else
                    {
                        _sideOpen[k] = 1f;
                        _sideWallDist[k] = lateralProbeDistance;
                    }
                }
                else
                {
                    int k = idx - 10;
                    if (_sideOpen[k] > 0.5f)
                    {
                        _outsideSkyExposed[k] = 1f;
                    }
                    else
                    {
                        float probeDist = Mathf.Min(_sideWallDist[k] + 2.0f, lateralProbeDistance);
                        Vector3 outsideSkyOrigin = origin + _cardinalDirs[k] * probeDist + new Vector3(0f, verticalProbeDistance, 0f);

                        if (Physics.Raycast(outsideSkyOrigin, Vector3.down, out hit, verticalProbeDistance + 5f, _raycastMask, QueryTriggerInteraction.Ignore))
                        {
                            // If sky-down hit outside the wall lands at or below our current ceiling, the wall faces open exterior sky
                            _outsideSkyExposed[k] = (hit.point.y <= _ceilingY + 0.5f) ? 1f : 0f;
                        }
                        else
                        {
                            _outsideSkyExposed[k] = 1f;
                        }
                    }
                }
            }
        }

        /**
         * Smoothly integrates probe results and crossfades 2D stereo beds and 3D spatialized emitters.
         *
         * @param dt Frame delta time in seconds.
         */
        private void EvaluateAndApplyMix(float dt)
        {
            float coneAvg = (_coneOpen[0] + _coneOpen[1] + _coneOpen[2] + _coneOpen[3]) * 0.25f;
            float targetDirectSky = _zenithOpen * 0.5f + coneAvg * 0.5f;

            float openSides = (_sideOpen[0] + _sideOpen[1] + _sideOpen[2] + _sideOpen[3]) * 0.25f;
            float extWallMax = Mathf.Max(
                Mathf.Max(_outsideSkyExposed[0], _outsideSkyExposed[1]),
                Mathf.Max(_outsideSkyExposed[2], _outsideSkyExposed[3])
            );

            bool singleRoofAbove = _hasCeiling && _hasTopRoof && ((_topRoofY - _ceilingY) <= singleRoofMaxThickness);
            float targetRoofExposed = singleRoofAbove ? 1f : 0f;

            Vector3 openVector = Vector3.zero;
            for (int k = 0; k < 4; k++)
            {
                if (_sideOpen[k] > 0.5f) openVector += _cardinalDirs[k];
            }
            float asymMag = openVector.magnitude;
            float targetAsymmetry = _hasCeiling ? Mathf.Clamp01(asymMag) : 0f;

            float lerpT = 1f - Mathf.Exp(-_acousticSmoothing * dt);
            _smoothDirectSky = Mathf.Lerp(_smoothDirectSky, targetDirectSky, lerpT);
            _smoothOpenSides = Mathf.Lerp(_smoothOpenSides, openSides, lerpT);
            _smoothExteriorWall = Mathf.Lerp(_smoothExteriorWall, extWallMax, lerpT);
            _smoothRoofExposed = Mathf.Lerp(_smoothRoofExposed, targetRoofExposed, lerpT);
            _smoothOpeningAsymmetry = Mathf.Lerp(_smoothOpeningAsymmetry, targetAsymmetry, lerpT);

            if (asymMag > 0.01f)
            {
                Vector3 normDir = openVector / asymMag;
                _smoothOpenDir = Vector3.Slerp(_smoothOpenDir, normDir, lerpT).normalized;
            }

            // --- Rain stem gains ---
            float baseRain = _rainVolume * _masterVolume;

            // Full 2D stereo exterior rain dominates outdoors or in wide-open spaces
            float ext2DFactor = Mathf.Clamp01(_smoothDirectSky * 0.85f + _smoothOpenSides * (1f - (_hasCeiling ? 0.65f : 0f)));
            float ext2DVol = baseRain * ext2DFactor;

            // Directional 3D opening emitter activates under roofs when one side opens to outside air
            float opening3DFactor = Mathf.Clamp01(_smoothOpeningAsymmetry * (1f - _smoothDirectSky));
            float opening3DVol = baseRain * opening3DFactor * 0.9f;

            // 2D muffled interior rain scales down smoothly if buried deep inside multi-room bunkers
            float enclosureFactor = 1f - ext2DFactor;
            float wallTransmission = Mathf.Lerp(_minBunkerScale, 1f, Mathf.Max(_smoothExteriorWall, _smoothOpenSides));
            float int2DVol = baseRain * enclosureFactor * _rainInteriorScale * wallTransmission;

            // 3D overhead roof patter activates strictly for single-story roofs
            float roof3DVol = baseRain * _smoothRoofExposed * _rainRoofScale;

            // --- Wind stem gains + multi-sine organic gusts ---
            float t = Time.time * _windGustSpeed;
            float gustWave = 0.5f
                + 0.28f * Mathf.Sin(t * 1.13f)
                + 0.14f * Mathf.Sin(t * 2.71f + 1.3f)
                + 0.08f * Mathf.Sin(t * 4.19f + 2.7f);
            gustWave = Mathf.Clamp01(gustWave);

            float windExposure = Mathf.Lerp(_windInteriorScale * wallTransmission, 1f, Mathf.Max(_smoothDirectSky, _smoothOpenSides * 0.7f));
            float baseWind = _windVolume * _masterVolume * windExposure;
            float windBaseVol = baseWind * (1f - _windGustStrength * 0.25f + gustWave * _windGustStrength * 0.35f);
            float windGustVol = baseWind * _windGustStrength * gustWave;

            // Position 3D emitters relative to head without rotating with head orientation
            if (rainOpening3D != null)
            {
                rainOpening3D.transform.position = _listenerPos + _smoothOpenDir * openingEmitterRadius;
            }
            if (rainRoof3D != null)
            {
                rainRoof3D.transform.position = _listenerPos + new Vector3(0f, 1.4f, 0f);
            }

            ApplySource(rainExterior2D, _clipRainExtStereo, ext2DVol, _rainPitch);
            ApplySource(rainInterior2D, _clipRainIntStereo, int2DVol, _rainPitch);
            ApplySource(rainOpening3D, _clipRainExtMono, opening3DVol, _rainPitch);
            ApplySource(rainRoof3D, _clipRainRoofMono, roof3DVol, _rainPitch);
            ApplySource(windBase2D, _clipWindBaseStereo, windBaseVol, _windPitch);
            ApplySource(windGust2D, _clipWindGustStereo, windGustVol, _windPitch * Mathf.Lerp(0.95f, 1.08f, gustWave));
        }

        private void ApplySource(AudioSource src, AudioClip clip, float volume, float pitch)
        {
            if (src == null) return;

            if (clip == null || volume <= 0.001f)
            {
                if (src.isPlaying) src.Stop();
                src.volume = 0f;
                return;
            }

            if (src.clip != clip)
            {
                src.clip = clip;
            }

            src.volume = volume;
            src.pitch = pitch;

            if (!src.isPlaying)
            {
                src.Play();
            }
        }

        private void StopAllSources()
        {
            StopSource(rainExterior2D);
            StopSource(rainInterior2D);
            StopSource(windBase2D);
            StopSource(windGust2D);
            StopSource(rainOpening3D);
            StopSource(rainRoof3D);
        }

        private void StopSource(AudioSource src)
        {
            if (src != null && src.isPlaying)
            {
                src.Stop();
            }
        }
    }
}
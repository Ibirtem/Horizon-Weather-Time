using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// A general-purpose positional audio environment.
    ///
    /// The system tracks the listener, probes the surrounding geometry (ceiling, walls,
    /// openings, roof), and drives any number of SpatialAudioEmitter components. It is
    /// completely independent from the weather system; weather simply feeds its stems into
    /// this system through WeatherAudioManager.
    /// </summary>
    [ExecuteInEditMode]
    [AddComponentMenu("Horizon/Spatial Audio System")]
#if UDONSHARP
    public class SpatialAudioSystem : UdonSharpBehaviour
#else
    public class SpatialAudioSystem : MonoBehaviour
#endif
    {
        [Header("System")]
        [Tooltip("Master switch for all emitters owned by this system.")]
        public bool audioEnabled = true;

        [Range(0f, 2f)]
        [Tooltip("Master loudness applied to every emitter.")]
        public float masterVolume = 1f;

        [Header("Emitters")]
        [Tooltip("Emitters driven by this system. The custom inspector keeps this list in sync.")]
        public SpatialAudioEmitter[] emitters;

        [Header("Listener")]
        [Tooltip("Optional explicit listener transform. Takes priority over automatic tracking.")]
        public Transform listenerOverride;

        [Tooltip("When no override or external listener is supplied, follow Camera.main.")]
        public bool trackMainCamera = true;

        [Header("Acoustic Probes")]
        [Tooltip("Enables ceiling, sky, wall, and roof probing. Disable for cheap ambient-only systems.")]
        public bool enableAcousticProbes = true;

        [Min(1f)]
        [Tooltip("Maximum distance for upward ceiling and sky-down roof probes.")]
        public float verticalProbeDistance = 60f;

        [Min(1f)]
        [Tooltip("Maximum horizontal distance to check for walls and open doorways.")]
        public float lateralProbeDistance = 18f;

        [Tooltip("Max vertical thickness between ceiling and top roof to count as a single-story roof.")]
        public float singleRoofMaxThickness = 1.8f;

        [Min(0f)]
        [Tooltip("Distance beyond Single Roof Thickness over which the roof patter fades to silence instead of cutting off. Larger = longer fade; 0 = hard cutoff.")]
        public float roofThicknessFalloff = 2.0f;

        [Range(1, 14)]
        [Tooltip("Raycasts executed per frame. Lower is cheaper, higher reacts faster.")]
        public int probesPerFrame = 2;

        [Tooltip("Layers treated as acoustic geometry. IgnoreRaycast, UI, player, and mirror layers are excluded by default.")]
        public int occluderMask = ~((1 << 2) | (1 << 5) | (1 << 9) | (1 << 10) | (1 << 18));

        [Range(0.1f, 20f)]
        [Tooltip("How quickly the acoustic state follows probe results.")]
        public float acousticSmoothing = 3.5f;

        [Range(0.1f, 10f)]
        [Tooltip("How quickly the overhead roof layer fades in/out. Slightly slower than the general acoustic state to avoid cutting.")]
        public float roofSmoothing = 1.5f;

        [Range(0f, 1f)]
        [Tooltip("Residual sound transmission deep inside sealed interiors.")]
        public float minimumIndoorGain = 0.05f;

        [Header("Opening Proximity")]
        [Min(1f)]
        [Tooltip("How far outward the system searches for the edge of the current ceiling/roof. Used to estimate distance to the nearest opening.")]
        public float openingProbeDistance = 12f;

        [Range(1, 4)]
        [Tooltip("Opening-depth raycasts per frame. These run in addition to the acoustic probes.")]
        public int openingProbesPerFrame = 2;

        [Header("Visualization")]
        [Min(1f)]
        [Tooltip("Radar range in meters used by the inspector and scene view.")]
        public float visualizerRange = 20f;

        [Tooltip("Draw probe rays and emitter placement handles in the scene view.")]
        public bool showProbeRaysInSceneView = true;

        // ---------------------------------------------------------------
        // Probe layout
        // 0: zenith, 1..4: 45-degree sky cone, 5: sky-down roof check,
        // 6..9: cardinal walls, 10..13: outside-wall sky checks.
        // ---------------------------------------------------------------

        private const int TOTAL_PROBES = 14;
        private int _probeCursor = 0;

        private const int OPENING_PROBE_STEPS = 6;
        private const int OPENING_CEILING_PROBES = 24;
        private const int OPENING_LINTEL_PROBES = 4;
        private const int OPENING_PROBE_TOTAL = OPENING_CEILING_PROBES + OPENING_LINTEL_PROBES;
        private int _openingProbeCursor = 0;

        private float _zenithOpen = 1f;
        private bool _hasCeiling = false;
        private float _ceilingY = 0f;
        private bool _hasTopRoof = false;
        private float _topRoofY = 0f;

        private float[] _coneOpen;
        private float[] _sideOpen;
        private float[] _sideWallDist;
        private float[] _outsideSkyExposed;
        private float[] _openingStepOpen;
        private float[] _sideLintelDist;

        private Vector3[] _coneDirs;
        private Vector3[] _cardinalDirs;

        // Debug copies for visualizers.
        private Vector3[] _probeOrigins;
        private Vector3[] _probeEnds;
        private float[] _probeOpen;
        private int[] _probeKinds;

        // Smoothed environment.
        private float _smoothDirectSky = 1f;
        private float _smoothOpenSides = 1f;
        private float _smoothExteriorWall = 1f;
        private float _smoothRoofExposed = 0f;
        private float _smoothOpeningAsymmetry = 0f;
        private Vector3 _smoothOpenDir = Vector3.forward;

        private float _openingDepth = 0f;
        private float _openingProximity = 1f;
        private float _smoothCeilingHeight = 1.4f;

        private Vector3 _listenerPosition;
        private Vector3 _externalListenerPosition;
        private bool _hasExternalListener = false;
        private bool _initialized = false;
        private bool _editorPreview = false;

        // ---------------------------------------------------------------
        // Public state
        // ---------------------------------------------------------------

        public Vector3 ListenerPosition { get { return _listenerPosition; } }
        public float SkyExposure { get { return _smoothDirectSky; } }
        public float LateralOpenness { get { return _smoothOpenSides; } }
        public float ExteriorWallExposure { get { return _smoothExteriorWall; } }
        public float RoofExposure { get { return _smoothRoofExposed; } }
        public float OpeningStrength { get { return _smoothOpeningAsymmetry; } }
        public Vector3 OpeningDirection { get { return _smoothOpenDir; } }
        public float OpeningProximity { get { return _openingProximity; } }
        public float OpeningDepth { get { return _openingDepth; } }
        public bool HasCeiling { get { return _hasCeiling; } }
        public bool HasTopRoof { get { return _hasTopRoof; } }
        public float CeilingHeight { get { return _hasCeiling ? Mathf.Max(0f, _ceilingY - _listenerPosition.y) : 0f; } }
        public float SmoothedCeilingHeight { get { return _smoothCeilingHeight; } }
        public float TopRoofHeight { get { return _hasTopRoof ? Mathf.Max(0f, _topRoofY - _listenerPosition.y) : 0f; } }
        public int ProbeCount { get { return TOTAL_PROBES; } }
        public bool IsEditorPreview { get { return _editorPreview; } }

        /// <summary>
        /// Combined wall transmission. 1 = sound passes freely, 0 = sealed.
        /// </summary>
        public float WallTransmission
        {
            get { return Mathf.Lerp(minimumIndoorGain, 1f, Mathf.Max(_smoothExteriorWall, _smoothOpenSides)); }
        }

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        private void OnEnable()
        {
            ValidateSettings();
            InitializeProbes();
            RefreshEmitterCache();
        }

        private void ValidateSettings()
        {
            if (!(acousticSmoothing > 0.01f)) acousticSmoothing = 3.5f;
            if (!(openingProbeDistance > 1f)) openingProbeDistance = 12f;
            if (openingProbesPerFrame <= 0) openingProbesPerFrame = 2;
            if (!(roofSmoothing > 0.01f)) roofSmoothing = 1.5f;
            if (!(roofThicknessFalloff > 0f)) roofThicknessFalloff = 2f;
        }

        private void OnDisable()
        {
            StopAllEmitters();
        }

        private void Update()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            Tick(Time.deltaTime, true, true);
        }

        /// <summary>
        /// Advances the acoustic simulation and applies it to every emitter.
        /// </summary>
        public void Tick(float dt, bool allowAudio, bool moveTransforms)
        {
            if (!_initialized) InitializeProbes();

            UpdateListenerPosition();

            if (enableAcousticProbes)
            {
                StepAcousticProbes(probesPerFrame);
                StepOpeningDepthProbes(openingProbesPerFrame);
                EvaluateEnvironment(dt);
                EvaluateOpeningDepth(dt);
            }
            else
            {
                SetOpenEnvironment();
            }

            float master = audioEnabled ? masterVolume : 0f;
            UpdateEmitters(dt, allowAudio, moveTransforms, master);
        }

        /// <summary>
        /// Updates the listener reference used by probes and emitters.
        /// </summary>
        public void SetListenerPosition(Vector3 position)
        {
            _externalListenerPosition = position;
            _hasExternalListener = true;
            _listenerPosition = position;
        }

        private void UpdateListenerPosition()
        {
            if (listenerOverride != null)
            {
                _listenerPosition = listenerOverride.position;
                return;
            }

            if (_hasExternalListener)
            {
                _listenerPosition = _externalListenerPosition;
                return;
            }

            if (trackMainCamera)
            {
#if UDONSHARP
                VRCPlayerApi player = Networking.LocalPlayer;
                if (Utilities.IsValid(player))
                {
                    _listenerPosition = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    return;
                }
#else
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    _listenerPosition = mainCamera.transform.position;
                }
#endif
            }
        }

        // ---------------------------------------------------------------
        // Editor preview / visual refresh
        // ---------------------------------------------------------------

        /// <summary>
        /// Enables or disables real-time audio preview from the editor. Preview is opt-in
        /// so editing a scene never starts audio by itself.
        /// </summary>
        public void SetEditorPreview(bool enabled)
        {
            _editorPreview = enabled;
            if (!enabled)
            {
                StopAllEmitters();
            }
        }

        /// <summary>
        /// Advances the system in edit mode when preview is active.
        /// </summary>
        public void EditorTick(float dt, Vector3 listenerPosition)
        {
            if (!_editorPreview) return;

            if (!_initialized) InitializeProbes();

            _listenerPosition = listenerPosition;
            _hasExternalListener = false;

            if (enableAcousticProbes)
            {
                StepAcousticProbes(probesPerFrame);
                StepOpeningDepthProbes(openingProbesPerFrame);
                EvaluateEnvironment(dt);
                EvaluateOpeningDepth(dt);
            }
            else
            {
                SetOpenEnvironment();
            }

            UpdateEmitters(dt, true, true, audioEnabled ? masterVolume : 0f);
        }

        /// <summary>
        /// Refreshes probes, environment, and emitter placement without starting audio.
        /// Used by inspector visualizers while the game is not running.
        /// </summary>
        public void EditorRefresh(Vector3 listenerPosition)
        {
            if (!_initialized) InitializeProbes();

            _listenerPosition = listenerPosition;
            _hasExternalListener = false;

            if (enableAcousticProbes)
            {
                StepAcousticProbes(TOTAL_PROBES);
                StepOpeningDepthProbes(OPENING_PROBE_TOTAL);
                EvaluateEnvironment(1f);
                EvaluateOpeningDepth(1f);
            }
            else
            {
                SetOpenEnvironment();
            }

            UpdateEmitters(0.5f, false, false, audioEnabled ? masterVolume : 0f);
        }

        /// <summary>
        /// Rebuilds the emitter array from direct children when it has not been assigned.
        /// </summary>
        public void RefreshEmitterCache()
        {
            if (emitters != null && emitters.Length > 0) return;

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
                emitters = null;
                return;
            }

            SpatialAudioEmitter[] trimmed = new SpatialAudioEmitter[count];
            for (int i = 0; i < count; i++) trimmed[i] = found[i];
            emitters = trimmed;
        }

        // ---------------------------------------------------------------
        // Probe initialization
        // ---------------------------------------------------------------

        private void InitializeProbes()
        {
            if (_initialized) return;

            _coneOpen = new float[4] { 1f, 1f, 1f, 1f };
            _sideOpen = new float[4] { 1f, 1f, 1f, 1f };
            _sideWallDist = new float[4] { lateralProbeDistance, lateralProbeDistance, lateralProbeDistance, lateralProbeDistance };
            _outsideSkyExposed = new float[4] { 1f, 1f, 1f, 1f };

            _openingStepOpen = new float[OPENING_PROBE_TOTAL];
            _sideLintelDist = new float[4];
            _openingDepth = openingProbeDistance;
            _openingProximity = 0f;

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

            _probeOrigins = new Vector3[TOTAL_PROBES];
            _probeEnds = new Vector3[TOTAL_PROBES];
            _probeOpen = new float[TOTAL_PROBES];
            _probeKinds = new int[TOTAL_PROBES];

            _listenerPosition = transform.position;
            _initialized = true;
        }

        // ---------------------------------------------------------------
        // Probe execution
        // ---------------------------------------------------------------

        /// <summary>
        /// Executes a fixed budget of round-robin raycasts per frame.
        /// </summary>
        private void StepAcousticProbes(int raysPerFrame)
        {
            int budget = Mathf.Clamp(raysPerFrame, 1, TOTAL_PROBES);
            Vector3 origin = _listenerPosition + new Vector3(0f, 0.1f, 0f);
            float upDistance = Mathf.Max(1f, verticalProbeDistance);
            float sideDistance = Mathf.Max(1f, lateralProbeDistance);
            RaycastHit hit;

            for (int r = 0; r < budget; r++)
            {
                int idx = _probeCursor;
                _probeCursor = (_probeCursor + 1) % TOTAL_PROBES;

                _probeOrigins[idx] = origin;
                _probeEnds[idx] = origin;
                _probeOpen[idx] = 0f;
                _probeKinds[idx] = 0;

                if (idx == 0)
                {
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.Zenith;

                    if (Physics.Raycast(origin, Vector3.up, out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        _zenithOpen = 0f;
                        _hasCeiling = true;
                        _ceilingY = hit.point.y;
                        _probeEnds[idx] = hit.point;
                    }
                    else
                    {
                        _zenithOpen = 1f;
                        _hasCeiling = false;
                        _ceilingY = origin.y + upDistance;
                        _probeEnds[idx] = origin + Vector3.up * upDistance;
                    }

                    _probeOpen[idx] = _zenithOpen;
                }
                else if (idx >= 1 && idx <= 4)
                {
                    int c = idx - 1;
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.SkyCone;

                    bool blocked = Physics.Raycast(origin, _coneDirs[c], out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore);
                    _coneOpen[c] = blocked ? 0f : 1f;
                    _probeOpen[idx] = _coneOpen[c];
                    _probeEnds[idx] = blocked ? hit.point : origin + _coneDirs[c] * upDistance;
                }
                else if (idx == 5)
                {
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.SkyDown;

                    Vector3 skyOrigin = new Vector3(origin.x, origin.y + upDistance, origin.z);
                    _probeOrigins[idx] = skyOrigin;

                    if (Physics.Raycast(skyOrigin, Vector3.down, out hit, upDistance + 5f, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        _hasTopRoof = true;
                        _topRoofY = hit.point.y;
                        _probeEnds[idx] = hit.point;
                    }
                    else
                    {
                        _hasTopRoof = false;
                        _topRoofY = origin.y;
                        _probeEnds[idx] = skyOrigin + Vector3.down * (upDistance + 5f);
                    }

                    _probeOpen[idx] = 1f;
                }
                else if (idx >= 6 && idx <= 9)
                {
                    int k = idx - 6;
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.Wall;

                    if (Physics.Raycast(origin, _cardinalDirs[k], out hit, sideDistance, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        _sideOpen[k] = 0f;
                        _sideWallDist[k] = hit.distance;
                        _probeEnds[idx] = hit.point;
                    }
                    else
                    {
                        _sideOpen[k] = 1f;
                        _sideWallDist[k] = sideDistance;
                        _probeEnds[idx] = origin + _cardinalDirs[k] * sideDistance;
                    }

                    _probeOpen[idx] = _sideOpen[k];
                }
                else
                {
                    int k = idx - 10;
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.OutsideWall;

                    if (_sideOpen[k] > 0.5f)
                    {
                        _outsideSkyExposed[k] = 1f;
                        _probeEnds[idx] = origin;
                    }
                    else
                    {
                        float probeDist = Mathf.Min(_sideWallDist[k] + 2f, sideDistance);
                        Vector3 outsideSkyOrigin = origin + _cardinalDirs[k] * probeDist + new Vector3(0f, upDistance, 0f);
                        _probeOrigins[idx] = outsideSkyOrigin;

                        if (Physics.Raycast(outsideSkyOrigin, Vector3.down, out hit, upDistance + 5f, occluderMask, QueryTriggerInteraction.Ignore))
                        {
                            // If sky-down hit outside the wall lands at or below the current
                            // ceiling, the wall faces open exterior sky.
                            _outsideSkyExposed[k] = (hit.point.y <= _ceilingY + 0.5f) ? 1f : 0f;
                            _probeEnds[idx] = hit.point;
                        }
                        else
                        {
                            _outsideSkyExposed[k] = 1f;
                            _probeEnds[idx] = outsideSkyOrigin + Vector3.down * (upDistance + 5f);
                        }
                    }

                    _probeOpen[idx] = _outsideSkyExposed[k];
                }
            }
        }

        // ---------------------------------------------------------------
        // Environment evaluation
        // ---------------------------------------------------------------

        /// <summary>
        /// Steps a round-robin set of upward probes along open sides. The nearest sample
        /// that is no longer covered by a ceiling/roof is treated as the opening edge.
        /// This gives the weather mix a smooth "how close am I to the doorway" gradient
        /// instead of a binary indoor/outdoor switch.
        /// </summary>
        private void StepOpeningDepthProbes(int raysPerFrame)
        {
            int budget = Mathf.Clamp(raysPerFrame, 1, OPENING_PROBE_TOTAL);
            float stepDistance = Mathf.Max(0.5f, openingProbeDistance / OPENING_PROBE_STEPS);
            float upDistance = Mathf.Max(1f, verticalProbeDistance);
            float sideDistance = Mathf.Max(1f, lateralProbeDistance);
            RaycastHit hit;

            for (int r = 0; r < budget; r++)
            {
                int idx = _openingProbeCursor;
                _openingProbeCursor = (_openingProbeCursor + 1) % OPENING_PROBE_TOTAL;

                if (idx < OPENING_CEILING_PROBES)
                {
                    int side = idx / OPENING_PROBE_STEPS;
                    int step = idx % OPENING_PROBE_STEPS;
                    float distance = (step + 1) * stepDistance;

                    if (_sideOpen[side] <= 0.5f)
                    {
                        _openingStepOpen[idx] = 0f;
                        continue;
                    }

                    Vector3 origin = _listenerPosition + _cardinalDirs[side] * distance + new Vector3(0f, 0.1f, 0f);
                    bool covered = Physics.Raycast(origin, Vector3.up, out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore);

                    _openingStepOpen[idx] = covered ? 0f : 1f;
                }
                else
                {
                    // Lintel probe: a horizontal ray slightly above head height catches
                    // doorways and windows even when there is a roof outside. Without it,
                    // a covered doorway would look like "deep indoors" to the ceiling edge test.
                    int side = idx - OPENING_CEILING_PROBES;

                    if (_sideOpen[side] <= 0.5f)
                    {
                        _sideLintelDist[side] = 0f;
                        continue;
                    }

                    Vector3 origin = _listenerPosition + new Vector3(0f, 0.1f, 0f);
                    float lintelOffset = _hasCeiling
                        ? Mathf.Clamp(_ceilingY - _listenerPosition.y - 0.15f, 1.2f, 2.2f)
                        : 2.2f;
                    origin.y = _listenerPosition.y + lintelOffset;

                    if (Physics.Raycast(origin, _cardinalDirs[side], out hit, sideDistance, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        _sideLintelDist[side] = hit.distance;
                    }
                    else
                    {
                        _sideLintelDist[side] = 0f;
                    }
                }
            }
        }

        private void EvaluateOpeningDepth(float dt)
        {
            float target;

            if (!_hasCeiling)
            {
                target = 0f;
            }
            else
            {
                target = Mathf.Max(1f, openingProbeDistance);

                for (int side = 0; side < 4; side++)
                {
                    if (_sideOpen[side] <= 0.5f) continue;

                    float sideDistance = target;

                    if (_sideLintelDist[side] > 0.05f)
                    {
                        sideDistance = _sideLintelDist[side];
                    }
                    else
                    {
                        for (int step = 0; step < OPENING_PROBE_STEPS; step++)
                        {
                            int idx = side * OPENING_PROBE_STEPS + step;
                            if (_openingStepOpen[idx] > 0.5f)
                            {
                                sideDistance = (step + 1) * Mathf.Max(0.5f, openingProbeDistance / OPENING_PROBE_STEPS);
                                break;
                            }
                        }
                    }

                    if (sideDistance < target) target = sideDistance;
                }
            }

            float lerpT = 1f - Mathf.Exp(-acousticSmoothing * dt);
            _openingDepth = Mathf.Lerp(_openingDepth, target, lerpT);
            _openingProximity = 1f - Mathf.Clamp01(_openingDepth / Mathf.Max(1f, openingProbeDistance));
        }

        private void EvaluateEnvironment(float dt)
        {
            float coneAvg = (_coneOpen[0] + _coneOpen[1] + _coneOpen[2] + _coneOpen[3]) * 0.25f;
            float targetDirectSky = _zenithOpen * 0.5f + coneAvg * 0.5f;

            float openSides = (_sideOpen[0] + _sideOpen[1] + _sideOpen[2] + _sideOpen[3]) * 0.25f;
            float extWallMax = Mathf.Max(
                Mathf.Max(_outsideSkyExposed[0], _outsideSkyExposed[1]),
                Mathf.Max(_outsideSkyExposed[2], _outsideSkyExposed[3]));

            float roofThickness = _topRoofY - _ceilingY;

            // Instead of a hard "single roof / not a single roof" switch, fade the roof
            // layer out over the empty thickness above the first ceiling. Beams and
            // joists close to the roof stay audible; a large gap fades to silence.
            float targetRoofExposed = 0f;

            if (_hasCeiling && _hasTopRoof)
            {
                if (roofThickness < 0f) roofThickness = 0f;

                if (roofThickness <= singleRoofMaxThickness)
                {
                    targetRoofExposed = 1f;
                }
                else if (roofThicknessFalloff > 0.01f)
                {
                    float t = Mathf.Clamp01((roofThickness - singleRoofMaxThickness) / roofThicknessFalloff);
                    targetRoofExposed = 1f - t * t * (3f - 2f * t);
                }
            }

            Vector3 openVector = Vector3.zero;
            for (int k = 0; k < 4; k++)
            {
                if (_sideOpen[k] > 0.5f) openVector += _cardinalDirs[k];
            }

            float asymMag = openVector.magnitude;
            float targetAsymmetry = _hasCeiling ? Mathf.Clamp01(asymMag) : 0f;

            float lerpT = 1f - Mathf.Exp(-acousticSmoothing * dt);

            float targetCeilingHeight = _hasCeiling ? Mathf.Max(0f, _ceilingY - _listenerPosition.y) : 0f;
            _smoothCeilingHeight = Mathf.Lerp(_smoothCeilingHeight, targetCeilingHeight, lerpT);

            _smoothDirectSky = Mathf.Lerp(_smoothDirectSky, targetDirectSky, lerpT);
            _smoothOpenSides = Mathf.Lerp(_smoothOpenSides, openSides, lerpT);
            _smoothExteriorWall = Mathf.Lerp(_smoothExteriorWall, extWallMax, lerpT);
            _smoothOpeningAsymmetry = Mathf.Lerp(_smoothOpeningAsymmetry, targetAsymmetry, lerpT);

            float roofLerpT = 1f - Mathf.Exp(-roofSmoothing * dt);
            _smoothRoofExposed = Mathf.Lerp(_smoothRoofExposed, targetRoofExposed, roofLerpT);

            if (asymMag > 0.01f)
            {
                Vector3 normDir = openVector / asymMag;
                _smoothOpenDir = Vector3.Slerp(_smoothOpenDir, normDir, lerpT).normalized;
            }
        }

        private void SetOpenEnvironment()
        {
            _smoothDirectSky = 1f;
            _smoothOpenSides = 1f;
            _smoothExteriorWall = 1f;
            _smoothRoofExposed = 0f;
            _smoothOpeningAsymmetry = 0f;
            _smoothOpenDir = Vector3.forward;
            _openingDepth = 0f;
            _openingProximity = 1f;
            _smoothCeilingHeight = 0f;
            _hasCeiling = false;
            _hasTopRoof = false;
        }

        // ---------------------------------------------------------------
        // Emitter driving
        // ---------------------------------------------------------------

        private void UpdateEmitters(float dt, bool allowAudio, bool moveTransforms, float master)
        {
            if (emitters == null) return;

            float ceilingHeight = _smoothCeilingHeight;

            for (int i = 0; i < emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = emitters[i];
                if (emitter == null) continue;

                float factor = EvaluateCondition(emitter.condition);
                emitter.SystemTick(dt, _listenerPosition, master, allowAudio, moveTransforms, factor, _smoothOpenDir, _openingDepth, ceilingHeight);
            }
        }

        private float EvaluateCondition(SpatialAudioCondition condition)
        {
            switch (condition)
            {
                case SpatialAudioCondition.Outdoors:
                    return Mathf.Clamp01(_smoothDirectSky * 0.7f + _smoothOpenSides * 0.3f);

                case SpatialAudioCondition.Indoors:
                    return Mathf.Clamp01((1f - _smoothDirectSky) * WallTransmission);

                case SpatialAudioCondition.UnderRoof:
                    return Mathf.Clamp01(_smoothRoofExposed);

                case SpatialAudioCondition.Opening:
                    return Mathf.Clamp01(_smoothOpeningAsymmetry * (1f - _smoothDirectSky));

                case SpatialAudioCondition.Sheltered:
                    return Mathf.Clamp01(1f - _smoothDirectSky);

                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Stops every emitter immediately.
        /// </summary>
        public void StopAllEmitters()
        {
            if (emitters == null) return;

            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] != null) emitters[i].StopPlayback();
            }
        }

        // ---------------------------------------------------------------
        // Debug accessors for the editor visualizers
        // ---------------------------------------------------------------

        public Vector3 GetProbeOrigin(int index)
        {
            if (_probeOrigins == null || index < 0 || index >= _probeOrigins.Length) return Vector3.zero;
            return _probeOrigins[index];
        }

        public Vector3 GetProbeEnd(int index)
        {
            if (_probeEnds == null || index < 0 || index >= _probeEnds.Length) return Vector3.zero;
            return _probeEnds[index];
        }

        public float GetProbeOpen(int index)
        {
            if (_probeOpen == null || index < 0 || index >= _probeOpen.Length) return 0f;
            return _probeOpen[index];
        }

        public int GetProbeKind(int index)
        {
            if (_probeKinds == null || index < 0 || index >= _probeKinds.Length) return 0;
            return _probeKinds[index];
        }

        public float GetSideOpen(int side)
        {
            if (_sideOpen == null || side < 0 || side >= _sideOpen.Length) return 0f;
            return _sideOpen[side];
        }

        public float GetSideWallDistance(int side)
        {
            if (_sideWallDist == null || side < 0 || side >= _sideWallDist.Length) return 0f;
            return _sideWallDist[side];
        }

        public Vector3 GetCardinalDirection(int side)
        {
            if (_cardinalDirs == null || side < 0 || side >= _cardinalDirs.Length) return Vector3.forward;
            return _cardinalDirs[side];
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireSphere(_listenerPosition, 0.28f);
            Gizmos.DrawLine(_listenerPosition, _listenerPosition + _smoothOpenDir * 1.5f);
        }
#endif
    }
}

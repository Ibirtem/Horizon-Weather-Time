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

        [Range(1, 30)]
        [Tooltip("Raycasts executed per frame. Lower is cheaper, higher reacts faster.")]
        public int probesPerFrame = 4;

        [Min(0.1f)]
        [Tooltip("Wall thickness (meters) at which exterior sound transmission drops to Minimum Indoor Gain.")]
        public float maxWallThickness = 2.5f;

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
        // 0..3: 45-degree sky cone, 4: zenith, 5: sky-down roof check,
        // 6..29: 24 horizontal sectors (15-degree resolution).
        // ---------------------------------------------------------------

        private const int SECTOR_COUNT = 24;
        private const int TOTAL_PROBES = 6 + SECTOR_COUNT;
        private int _probeCursor = 0;

        private const int OPENING_PROBE_STEPS = 4;
        private int _openingProbeCursor = 0;

        private float _zenithOpen = 1f;
        private bool _hasCeiling = false;
        private float _ceilingY = 0f;
        private bool _hasTopRoof = false;
        private float _topRoofY = 0f;

        private float[] _coneOpen;
        private Vector3[] _sectorDirs;
        private float[] _sectorRawOpen;
        private float[] _sectorOpen;
        private float[] _sectorWallDist;
        private float[] _sectorWallTransmission;
        private float[] _sectorEdgeDist;
        private float[] _openingStepOpen;

        private Vector3[] _coneDirs;

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
        private Vector3 _openingProbeDir = Vector3.forward;

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

        public int SectorCount { get { return SECTOR_COUNT; } }

        private void ValidateSettings()
        {
            if (!(acousticSmoothing > 0.01f)) acousticSmoothing = 3.5f;
            if (!(openingProbeDistance > 1f)) openingProbeDistance = 12f;
            if (probesPerFrame <= 0) probesPerFrame = 4;
            if (openingProbesPerFrame <= 0) openingProbesPerFrame = 2;
            if (!(roofSmoothing > 0.01f)) roofSmoothing = 1.5f;
            if (!(roofThicknessFalloff > 0f)) roofThicknessFalloff = 2f;
            if (!(maxWallThickness > 0.1f)) maxWallThickness = 2.5f;
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
                _probeCursor = 0;
                _openingProbeCursor = 0;
                StepAcousticProbes(TOTAL_PROBES);
                EvaluateEnvironment(1f);
                StepOpeningDepthProbes(OPENING_PROBE_STEPS);
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

            _sectorDirs = new Vector3[SECTOR_COUNT];
            _sectorRawOpen = new float[SECTOR_COUNT];
            _sectorOpen = new float[SECTOR_COUNT];
            _sectorWallDist = new float[SECTOR_COUNT];
            _sectorWallTransmission = new float[SECTOR_COUNT];
            _sectorEdgeDist = new float[SECTOR_COUNT];

            float stepRad = (Mathf.PI * 2f) / SECTOR_COUNT;
            for (int k = 0; k < SECTOR_COUNT; k++)
            {
                float rad = k * stepRad;
                _sectorDirs[k] = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                _sectorRawOpen[k] = 1f;
                _sectorOpen[k] = 1f;
                _sectorWallDist[k] = lateralProbeDistance;
                _sectorWallTransmission[k] = 1f;
                _sectorEdgeDist[k] = openingProbeDistance;
            }

            _openingStepOpen = new float[OPENING_PROBE_STEPS];
            _openingDepth = openingProbeDistance;
            _openingProximity = 0f;

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

                if (idx < 4)
                {
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.SkyCone;

                    bool blocked = Physics.Raycast(origin, _coneDirs[idx], out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore);
                    _coneOpen[idx] = blocked ? 0f : 1f;
                    _probeOpen[idx] = _coneOpen[idx];
                    _probeEnds[idx] = blocked ? hit.point : origin + _coneDirs[idx] * upDistance;
                }
                else if (idx == 4)
                {
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.Zenith;
                    EvaluateZenithCluster(origin, upDistance);
                }
                else if (idx == 5)
                {
                    _probeKinds[idx] = (int)SpatialAudioProbeKind.SkyDown;

                    if (!_hasCeiling)
                    {
                        _hasTopRoof = false;
                        _topRoofY = origin.y;
                        _probeOrigins[idx] = Vector3.zero;
                        _probeEnds[idx] = Vector3.zero;
                        _probeOpen[idx] = 1f;
                        continue;
                    }

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
                else
                {
                    EvaluateHorizontalSector(idx - 6, origin, sideDistance, upDistance);
                }
            }
        }

        /// <summary>
        /// Probes overhead using a 3-point perpendicular cluster so thin beams or lamps do not trigger false roof detection.
        /// </summary>
        private void EvaluateZenithCluster(Vector3 origin, float upDistance)
        {
            RaycastHit hit;
            if (!Physics.Raycast(origin, Vector3.up, out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
            {
                _zenithOpen = 1f;
                _hasCeiling = false;
                _ceilingY = origin.y + upDistance;
                _probeEnds[4] = origin + Vector3.up * upDistance;
                _probeOpen[4] = 1f;
                return;
            }

            int hitCount = 1;
            float ySum = hit.point.y;
            Vector3 centerHitPoint = hit.point;

            RaycastHit offsetHit;
            if (Physics.Raycast(origin + new Vector3(0.45f, 0f, 0f), Vector3.up, out offsetHit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
            {
                hitCount++;
                ySum += offsetHit.point.y;
            }

            if (Physics.Raycast(origin + new Vector3(0f, 0f, 0.45f), Vector3.up, out offsetHit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
            {
                hitCount++;
                ySum += offsetHit.point.y;
            }

            float coneAvg = (_coneOpen[0] + _coneOpen[1] + _coneOpen[2] + _coneOpen[3]) * 0.25f;

            if (hitCount < 3 && coneAvg >= 0.75f)
            {
                _zenithOpen = 1f;
                _hasCeiling = false;
                _ceilingY = origin.y + upDistance;
                _probeEnds[4] = origin + Vector3.up * upDistance;
                _probeOpen[4] = 1f;
                return;
            }

            _hasCeiling = true;
            _ceilingY = ySum / hitCount;
            _zenithOpen = 1f - (hitCount / 3f);
            _probeEnds[4] = centerHitPoint;
            _probeOpen[4] = _zenithOpen;
        }

        /// <summary>
        /// Evaluates a horizontal sector, filtering low railings, exterior courtyard obstacles beyond roof edges, and wall thickness.
        /// </summary>
        private void EvaluateHorizontalSector(int k, Vector3 origin, float sideDistance, float upDistance)
        {
            int probeIdx = 6 + k;
            Vector3 dir = _sectorDirs[k];
            _probeKinds[probeIdx] = (int)SpatialAudioProbeKind.Wall;
            _sectorEdgeDist[k] = openingProbeDistance;

            RaycastHit hit;
            if (!Physics.Raycast(origin, dir, out hit, sideDistance, occluderMask, QueryTriggerInteraction.Ignore))
            {
                _sectorRawOpen[k] = 1f;
                _sectorWallDist[k] = sideDistance;
                _sectorWallTransmission[k] = 1f;
                _probeEnds[probeIdx] = origin + dir * sideDistance;
                UpdateSectorFilterAround(k);
                return;
            }

            if (hit.distance < openingProbeDistance)
            {
                float ceilingClearance = _hasCeiling ? Mathf.Max(0f, (_ceilingY - origin.y) - 0.15f) : 0.45f;
                float upperOffset = Mathf.Min(0.45f, ceilingClearance);

                if (upperOffset > 0.12f)
                {
                    Vector3 upperOrigin = origin + new Vector3(0f, upperOffset, 0f);
                    RaycastHit upperHit;
                    if (!Physics.Raycast(upperOrigin, dir, out upperHit, sideDistance, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        _sectorRawOpen[k] = 1f;
                        _sectorWallDist[k] = sideDistance;
                        _sectorWallTransmission[k] = 1f;
                        _probeEnds[probeIdx] = origin + dir * sideDistance;
                        UpdateSectorFilterAround(k);
                        return;
                    }

                    if (upperHit.distance > hit.distance + 1.5f)
                    {
                        hit = upperHit;
                    }
                }
            }

            float dist = hit.distance;
            _sectorWallDist[k] = dist;
            _probeEnds[probeIdx] = hit.point;

            if (_hasCeiling && dist > 1.5f)
            {
                Vector3 preHitOrigin = origin + dir * (dist - 0.25f);
                RaycastHit preSkyHit;
                if (!Physics.Raycast(preHitOrigin, Vector3.up, out preSkyHit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
                {
                    _sectorRawOpen[k] = 1f;
                    _sectorWallTransmission[k] = 1f;
                    _sectorEdgeDist[k] = Mathf.Min(openingProbeDistance, dist - 0.25f);
                    UpdateSectorFilterAround(k);
                    return;
                }
            }

            _sectorRawOpen[k] = 0f;
            _sectorWallTransmission[k] = (_hasCeiling && dist <= openingProbeDistance)
                ? MeasureWallTransmission(hit, dir, upDistance)
                : 1f;

            UpdateSectorFilterAround(k);
        }

        /// <summary>
        /// Estimates sound transmission through a hit wall based on its measured thickness and exterior sky exposure.
        /// Returns 0 for inner rooms or walls exceeding maxWallThickness, and up to 1 for thin exterior walls.
        /// </summary>
        private float MeasureWallTransmission(RaycastHit hit, Vector3 dir, float upDistance)
        {
            float maxCheck = Mathf.Max(3.0f, maxWallThickness + 0.5f);
            float wallThickness = 0f;
            bool hasThickness = false;

            Collider col = hit.collider;
            if (IsColliderValid(col))
            {
                Ray backRay = new Ray(hit.point + dir * maxCheck, -dir);
                RaycastHit backHit;
                if (col.Raycast(backRay, out backHit, maxCheck))
                {
                    wallThickness = Mathf.Max(0f, maxCheck - backHit.distance);
                    hasThickness = true;
                }
                else if (Physics.CheckSphere(hit.point + dir * 0.5f, 0.05f, occluderMask, QueryTriggerInteraction.Ignore))
                {
                    return 0f;
                }
            }

            float outsideStep = hasThickness ? Mathf.Clamp(wallThickness + 1.2f, 1.2f, maxCheck + 0.5f) : 1.5f;
            Vector3 outsideOrigin = hit.point + dir * outsideStep;
            RaycastHit outSkyHit;

            if (Physics.Raycast(outsideOrigin, Vector3.up, out outSkyHit, upDistance, occluderMask, QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            if (!hasThickness)
            {
                return 0.65f;
            }

            return 1f - Mathf.SmoothStep(0.15f, Mathf.Max(0.25f, maxWallThickness), wallThickness);
        }

        private bool IsColliderValid(Collider col)
        {
#if UDONSHARP
            return Utilities.IsValid(col);
#else
            return col != null;
#endif
        }

        private void UpdateSectorFilterAround(int centerSector)
        {
            ApplySectorFilter(centerSector);
            ApplySectorFilter((centerSector + SECTOR_COUNT - 1) % SECTOR_COUNT);
            ApplySectorFilter((centerSector + 1) % SECTOR_COUNT);
        }

        /// <summary>
        /// Suppresses isolated 1-sector hits caused by thin poles, railings, or mullions flanked by open air.
        /// </summary>
        private void ApplySectorFilter(int sector)
        {
            int prev = (sector + SECTOR_COUNT - 1) % SECTOR_COUNT;
            int next = (sector + 1) % SECTOR_COUNT;
            bool filteredOpen = _sectorRawOpen[sector] > 0.5f || (_sectorRawOpen[prev] > 0.5f && _sectorRawOpen[next] > 0.5f);
            float val = filteredOpen ? 1f : 0f;
            _sectorOpen[sector] = val;
            _probeOpen[6 + sector] = val;
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
            if (!_hasCeiling)
            {
                for (int i = 0; i < OPENING_PROBE_STEPS; i++) _openingStepOpen[i] = 1f;
                return;
            }

            int budget = Mathf.Clamp(raysPerFrame, 1, OPENING_PROBE_STEPS);
            float stepDistance = Mathf.Max(0.5f, openingProbeDistance / OPENING_PROBE_STEPS);
            float upDistance = Mathf.Max(1f, verticalProbeDistance);
            RaycastHit hit;

            for (int r = 0; r < budget; r++)
            {
                int idx = _openingProbeCursor;
                _openingProbeCursor = (_openingProbeCursor + 1) % OPENING_PROBE_STEPS;

                float distance = (idx + 1) * stepDistance;
                Vector3 origin = _listenerPosition + _openingProbeDir * distance + new Vector3(0f, 0.1f, 0f);
                bool covered = Physics.Raycast(origin, Vector3.up, out hit, upDistance, occluderMask, QueryTriggerInteraction.Ignore);

                _openingStepOpen[idx] = covered ? 0f : 1f;
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
                float stepDistance = Mathf.Max(0.5f, openingProbeDistance / OPENING_PROBE_STEPS);

                for (int step = 0; step < OPENING_PROBE_STEPS; step++)
                {
                    if (_openingStepOpen[step] > 0.5f)
                    {
                        float dist = (step + 1) * stepDistance;
                        if (dist < target) target = dist;
                        break;
                    }
                }

                for (int k = 0; k < SECTOR_COUNT; k++)
                {
                    if (_sectorOpen[k] > 0.5f && _sectorEdgeDist[k] < target)
                    {
                        target = _sectorEdgeDist[k];
                    }
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

            float openSum = 0f;
            Vector3 openVector = Vector3.zero;
            float maxWallWindow = 0f;

            for (int k = 0; k < SECTOR_COUNT; k++)
            {
                float open = _sectorOpen[k];
                openSum += open;
                if (open > 0.5f) openVector += _sectorDirs[k];

                int prev = (k + SECTOR_COUNT - 1) % SECTOR_COUNT;
                int next = (k + 1) % SECTOR_COUNT;

                float tPrev = _sectorOpen[prev] > 0.5f ? 0f : _sectorWallTransmission[prev];
                float tCurr = open > 0.5f ? 0f : _sectorWallTransmission[k];
                float tNext = _sectorOpen[next] > 0.5f ? 0f : _sectorWallTransmission[next];

                float windowAvg = (tPrev + tCurr + tNext) / 3f;
                if (windowAvg > maxWallWindow) maxWallWindow = windowAvg;
            }

            float openSides = openSum / SECTOR_COUNT;
            float roofThickness = _topRoofY - _ceilingY;
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

            float vecMag = openVector.magnitude;
            bool hasOpenVec = vecMag > 0.01f;
            Vector3 openNorm = hasOpenVec ? (openVector / vecMag) : _openingProbeDir;

            // Select the strongest genuinely open sector so multi-window corners do not point into a solid wall.
            float bestScore = -1f;
            Vector3 bestSectorDir = _openingProbeDir;
            for (int k = 0; k < SECTOR_COUNT; k++)
            {
                if (_sectorOpen[k] <= 0.5f) continue;

                int prev = (k + SECTOR_COUNT - 1) % SECTOR_COUNT;
                int next = (k + 1) % SECTOR_COUNT;
                float clusterWidth = _sectorOpen[prev] + _sectorOpen[k] + _sectorOpen[next];
                float align = hasOpenVec ? Mathf.Max(0f, Vector3.Dot(_sectorDirs[k], openNorm)) : 0f;
                float score = clusterWidth + align * 1.5f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSectorDir = _sectorDirs[k];
                }
            }

            if (bestScore > 0f)
            {
                _openingProbeDir = bestSectorDir;
            }

            float dirStrength = Mathf.Clamp01(vecMag / 3.0f);
            float targetAsymmetry = _hasCeiling ? Mathf.Max(dirStrength, Mathf.Clamp01(openSides * 2.5f)) : 0f;

            float lerpT = 1f - Mathf.Exp(-acousticSmoothing * dt);

            float targetCeilingHeight = _hasCeiling ? Mathf.Max(0f, _ceilingY - _listenerPosition.y) : 0f;
            _smoothCeilingHeight = Mathf.Lerp(_smoothCeilingHeight, targetCeilingHeight, lerpT);

            _smoothDirectSky = Mathf.Lerp(_smoothDirectSky, targetDirectSky, lerpT);
            _smoothOpenSides = Mathf.Lerp(_smoothOpenSides, openSides, lerpT);
            _smoothExteriorWall = Mathf.Lerp(_smoothExteriorWall, maxWallWindow, lerpT);
            _smoothOpeningAsymmetry = Mathf.Lerp(_smoothOpeningAsymmetry, targetAsymmetry, lerpT);

            float roofLerpT = 1f - Mathf.Exp(-roofSmoothing * dt);
            _smoothRoofExposed = Mathf.Lerp(_smoothRoofExposed, targetRoofExposed, roofLerpT);

            if (bestScore > 0f)
            {
                _smoothOpenDir = Vector3.Slerp(_smoothOpenDir, bestSectorDir, lerpT).normalized;
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
            _openingProbeDir = Vector3.forward;
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

        public float GetSectorOpen(int sector)
        {
            if (_sectorOpen == null || sector < 0 || sector >= _sectorOpen.Length) return 0f;
            return _sectorOpen[sector];
        }

        public Vector3 GetSectorDirection(int sector)
        {
            if (_sectorDirs == null || sector < 0 || sector >= _sectorDirs.Length) return Vector3.forward;
            return _sectorDirs[sector];
        }

        public float GetSectorWallDistance(int sector)
        {
            if (_sectorWallDist == null || sector < 0 || sector >= _sectorWallDist.Length) return 0f;
            return _sectorWallDist[sector];
        }

        public float GetSideOpen(int side)
        {
            int sector = (side & 3) * (SECTOR_COUNT / 4);
            return GetSectorOpen(sector);
        }

        public float GetSideWallDistance(int side)
        {
            int sector = (side & 3) * (SECTOR_COUNT / 4);
            return GetSectorWallDistance(sector);
        }

        public Vector3 GetCardinalDirection(int side)
        {
            int sector = (side & 3) * (SECTOR_COUNT / 4);
            return GetSectorDirection(sector);
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

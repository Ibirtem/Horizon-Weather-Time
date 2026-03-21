using System;
using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Defines how the time of day advances in the system.
    /// </summary>
    public enum TimeMode
    {
        SyncWithSystemClock,
        SimulatedFlow,
        StaticManual
    }

    [ExecuteInEditMode]
#if UDONSHARP
    public class WeatherTimeSystem : UdonSharpBehaviour
#else
    public class WeatherTimeSystem : MonoBehaviour
#endif
    {
        [Header("Time Settings")]
        [Tooltip("Defines how the celestial bodies move over time.")]
        public TimeMode timeMode = TimeMode.SyncWithSystemClock;
        public bool simulateMoonPhase = true; [Range(-12f, 14f)]
        public float timeZoneOffset = 3.0f;

        [Header("Simulation Settings")]
        [Range(0f, 1f)]
        [SerializeField] public float _sunTimeOfDay = 0.25f;
        [Range(0f, 1f)]
        [SerializeField] private float _moonTimeOfDay = 0.75f;
        public float timeSpeed = 1.0f;

        [Tooltip("Duration of a lunar cycle in-game days (Synodic month). Real moon is ~29.5.")]
        public float lunarCycleDays = 28.0f;

        [Range(0f, 1f)]
        public float moonPhase = 0.5f;

        [Header("Astronomy Settings")]
        [Tooltip("Latitude on the planet (-90 to 90). Controls the trajectory of celestial bodies.")]
        [Range(-90f, 90f)]
        public float latitude = 0f;

        [Tooltip("Axial tilt of the planet. Controls the seasons (Declination). Earth is ~23.44.")]
        [Range(0f, 45f)]
        public float axialTilt = 23.44f;

        [Tooltip("Number of days in a year. Affects the seasonal cycle.")]
        [Min(1f)]
        public float daysInYear = 365.25f;

        [Tooltip("Current day of the year. Controls the seasons.")]
        [Min(1f)]
        public float dayOfYear = 1f;

        private bool _isExternallyControlled = false;

        [Header("Weather Settings")]
        [Tooltip("List of Weather Profiles. Stored as generic Objects for Udon compatibility.")]
        public UnityEngine.Object[] weatherProfilesList;

        [Tooltip("The index of the currently active profile.")]
        [SerializeField] private int _currentProfileIndex = 0;

        [Header("Independent Module States")]
        [SerializeField, HideInInspector] private int _lightingIndex = 0;
        [SerializeField, HideInInspector] private int _skyIndex = 0;
        [SerializeField, HideInInspector] private int _cloudIndex = 0;
        [SerializeField, HideInInspector] private int _moonIndex = 0;
        [SerializeField, HideInInspector] private int _fogIndex = 0;
        [SerializeField, HideInInspector] private int _effectsIndex = 0;

        // =========================================================
        // WEATHER TRANSITIONS
        // =========================================================
        [Header("Weather Transitions")]
        [Tooltip("Default duration for weather transitions in seconds.")]
        public float defaultTransitionDuration = 10f;

        public const int MOD_LIGHTING = 0;
        public const int MOD_SKY = 1;
        public const int MOD_CLOUD = 2;
        public const int MOD_MOON = 3;
        public const int MOD_FOG = 4;
        public const int MOD_EFFECTS = 5;
        private const int MODULE_COUNT = 6;

        private bool[] _modTransitioning;
        private float[] _modTransitionElapsed;
        private float[] _modTransitionDuration;

        private int _targetLightingIndex = 0;
        private int _targetSkyIndex = 0;
        private int _targetCloudIndex = 0;
        private int _targetMoonIndex = 0;
        private int _targetFogIndex = 0;
        private int _targetEffectsIndex = 0;

        // Pre-allocated interpolation buffers (created at bake time)
        [HideInInspector] public BakedProfileData _resolvedLighting;
        [HideInInspector] public BakedProfileData _resolvedSky;
        [HideInInspector] public BakedProfileData _resolvedCloud;
        [HideInInspector] public BakedProfileData _resolvedMoon;
        [HideInInspector] public BakedProfileData _resolvedFog;
        [HideInInspector] public BakedProfileData _resolvedEffects;

        public int GetCurrentIndex(int module) { return GetIndex(module, false); }
        public int GetTargetIndex(int module) { return GetIndex(module, true); }

        private void CommitTargetToCurrent(int module)
        {
            SetIndex(module, false, GetIndex(module, true));
        }

        private void SetCurrentAndTarget(int module, int value)
        {
            SetIndex(module, false, value);
            SetIndex(module, true, value);
            if (_modTransitioning != null) _modTransitioning[module] = false;
        }

        public int TargetLightingIndex { get { return _targetLightingIndex; } }
        public int TargetSkyIndex { get { return _targetSkyIndex; } }
        public int TargetCloudIndex { get { return _targetCloudIndex; } }
        public int TargetMoonIndex { get { return _targetMoonIndex; } }
        public int TargetFogIndex { get { return _targetFogIndex; } }
        public int TargetEffectsIndex { get { return _targetEffectsIndex; } }

        private int _lastActiveProfileIndex = -1;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public WeatherProfile CurrentWeatherProfile
        {
            get
            {
                if (weatherProfilesList == null || weatherProfilesList.Length == 0) return null;
                if (_currentProfileIndex >= weatherProfilesList.Length) _currentProfileIndex = 0;
                return weatherProfilesList[_currentProfileIndex] as WeatherProfile;
            }
        }
#endif

        // =========================================================
        // BAKED DATA
        // =========================================================

        [HideInInspector] public BakedProfileData[] bakedLightingModules;
        [HideInInspector] public BakedProfileData[] bakedSkyModules;
        [HideInInspector] public BakedProfileData[] bakedCloudModules;
        [HideInInspector] public BakedProfileData[] bakedMoonModules;
        [HideInInspector] public BakedProfileData[] bakedFogModules;
        [HideInInspector] public BakedProfileData[] bakedEffectsModules;

        [Header("Preset Mappings (Auto-generated)")]
        [HideInInspector] public int[] presetToLighting;
        [HideInInspector] public int[] presetToSky;
        [HideInInspector] public int[] presetToCloud;
        [HideInInspector] public int[] presetToMoon;
        [HideInInspector] public int[] presetToFog;
        [HideInInspector] public int[] presetToEffects;

        // =========================================================

        [Header("System References")]
        [SerializeField] private LightingManager _lightingManager;
        [SerializeField] private SkyManager _skyManager;
        [SerializeField] private WeatherEffectsManager _weatherEffectsManager;
        [SerializeField] private ReflectionManager _reflectionManager;

        private const float SECONDS_IN_DAY = 86400f;

        public float TimeOfDay => _sunTimeOfDay;

        public int LightingIndex => _lightingIndex;
        public int SkyIndex => _skyIndex;
        public int CloudIndex => _cloudIndex;
        public int MoonIndex => _moonIndex;
        public int FogIndex => _fogIndex;
        public int EffectsIndex => _effectsIndex;

        private Camera _mainCameraCache;

        // -----------------------------------------------------------------------
        // EDITOR ONLY METHODS
        // -----------------------------------------------------------------------
#if !COMPILER_UDONSHARP && UNITY_EDITOR

        private void Reset()
        {
            Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Running first-time setup...</color>", this);
            _lightingManager = FindOrCreateManager<LightingManager>("Lighting");
            _skyManager = FindOrCreateManager<SkyManager>("Sky");
            _weatherEffectsManager = FindOrCreateManager<WeatherEffectsManager>("Effects");
            _reflectionManager = FindOrCreateManager<ReflectionManager>("Reflections");
            if (_reflectionManager != null) _reflectionManager.EnsureProbeExists();
        }

        private T FindOrCreateManager<T>(string gameObjectName) where T : Component
        {
            T manager = GetComponentInChildren<T>();
            if (manager != null) return manager;

            GameObject managerObject = new GameObject(gameObjectName);
            managerObject.transform.SetParent(transform);
            managerObject.transform.localPosition = Vector3.zero;
            return managerObject.AddComponent<T>();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            daysInYear = Mathf.Max(1f, daysInYear);
            dayOfYear = Mathf.Clamp(dayOfYear, 1f, daysInYear);

            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    
                    UpdateSystem();
                    
                    UnityEditor.SceneView.RepaintAll();
                };
            }
        }
#endif

#endif

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnEnable()
        {
            WeatherProfile.OnProfileDataChanged += Editor_HotReloadProfile;
        }

        private void OnDisable()
        {
            WeatherProfile.OnProfileDataChanged -= Editor_HotReloadProfile;
        }
#endif

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void Editor_HotReloadProfile(WeatherProfile changedProfile)
        {
            if (this == null) return;
            
            UpdateSystem();

            if (!Application.isPlaying)
            {
                UnityEditor.SceneView.RepaintAll();
            }
        }
#endif

        private void Start()
        {
            InitTransitionArrays();
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            WarnAboutExternalLights();
#endif
            Refresh();
        }

        private void Update()
        {
            bool isPlaying = true;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            isPlaying = Application.isPlaying;
#endif

            if (isPlaying && !_isExternallyControlled)
            {
                CalculateTime();
                TickModuleTransitions();
                UpdateSystem();

                if (_weatherEffectsManager != null)
                {
                    Vector3 targetPos = GetViewPosition();
                    _weatherEffectsManager.UpdatePosition(targetPos);
                }
            }
        }

        // =========================================================
        // TRACKING LOGIC
        // =========================================================

        private Vector3 GetViewPosition()
        {
#if UDONSHARP
            return GetPositionVRC();
#else
            return GetPositionUnity();
#endif
        }

#if UDONSHARP
        private Vector3 GetPositionVRC()
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            
            if (Utilities.IsValid(player))
            {
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            }
            
            return transform.position;
        }
#else
        private Vector3 GetPositionUnity()
        {
            if (_mainCameraCache == null)
            {
                _mainCameraCache = Camera.main;
            }

            if (_mainCameraCache == null)
            {
                _mainCameraCache = FindObjectOfType<Camera>();
            }

            if (_mainCameraCache != null)
            {
                return _mainCameraCache.transform.position;
            }
#if UNITY_EDITOR
            if (UnityEditor.SceneView.lastActiveSceneView != null)
            {
                 return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
            }
#endif
            return transform.position;
        }
#endif

        public void ManualUpdate(float time01, Vector3 playerHeadPos)
        {
            _isExternallyControlled = true;
            _sunTimeOfDay = Mathf.Clamp01(time01);
            _moonTimeOfDay = (_sunTimeOfDay - moonPhase + 1.0f) % 1.0f;

            TickModuleTransitions();
            UpdateSystem();

            if (_weatherEffectsManager != null)
            {
                _weatherEffectsManager.UpdatePosition(playerHeadPos);
            }
        }

        /// <summary>
        /// Advances time-of-day, day-of-year, and lunar phase based on the current TimeMode.
        /// </summary>
        private void CalculateTime()
        {
            if (timeMode == TimeMode.SyncWithSystemClock)
            {
                DateTime currentUtc = DateTime.UtcNow;
                DateTime instanceTime = currentUtc.AddHours(timeZoneOffset);

                double totalSecondsInDay = instanceTime.TimeOfDay.TotalSeconds;
                _sunTimeOfDay = (float)(totalSecondsInDay / SECONDS_IN_DAY);

                dayOfYear = instanceTime.DayOfYear + _sunTimeOfDay;

                double jdNow = DateTimeToJulianDate(currentUtc);
                double daysSinceNewMoon = jdNow - 2451550.1;
                double synodicMonth = 29.53058868;
                moonPhase = (float)((daysSinceNewMoon % synodicMonth) / synodicMonth);
                if (moonPhase < 0f) moonPhase += 1.0f;

                _moonTimeOfDay = (_sunTimeOfDay - moonPhase + 1.0f) % 1.0f;
            }
            else
            {
                bool shouldSimulate = true;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                shouldSimulate = Application.isPlaying;
#endif
                if (shouldSimulate && timeMode == TimeMode.SimulatedFlow)
                {
                    float dayDelta = (Time.deltaTime * timeSpeed) / SECONDS_IN_DAY;

                    _sunTimeOfDay += dayDelta;
                    dayOfYear += dayDelta;

                    if (dayOfYear > daysInYear) dayOfYear -= daysInYear;
                    if (dayOfYear < 1f) dayOfYear += daysInYear;

                    if (simulateMoonPhase)
                    {
                        moonPhase += dayDelta / lunarCycleDays;
                        if (moonPhase > 1.0f) moonPhase -= 1.0f;
                        if (moonPhase < 0f) moonPhase += 1.0f;
                    }

                    if (_sunTimeOfDay >= 1.0f) _sunTimeOfDay -= 1.0f;
                    if (_sunTimeOfDay < 0f) _sunTimeOfDay += 1.0f;
                }

                if (simulateMoonPhase)
                {
                    _moonTimeOfDay = (_sunTimeOfDay - moonPhase + 1.0f) % 1.0f;
                }
            }
        }

        /// <summary>
        /// Converts UTC DateTime to Julian Date for lunar phase calculation.
        /// Reference epoch: Jan 6, 2000 11:14 UTC (new moon, JD 2451550.1).
        /// </summary>
        private double DateTimeToJulianDate(DateTime dt)
        {
            int y = dt.Year;
            int m = dt.Month;
            int d = dt.Day;

            if (m <= 2)
            {
                y -= 1;
                m += 12;
            }

            int A = y / 100;
            int B = 2 - A + (A / 4);

            double jd = (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + B - 1524.5;
            jd += dt.TimeOfDay.TotalDays;

            return jd;
        }

        public void Refresh()
        {
            if (!_isExternallyControlled)
            {
                CalculateTime();
            }
            UpdateSystem();
        }

        /// <summary>
        /// Forces a visual update without recalculating time.
        /// Safe to call when externally controlled.
        /// </summary>
        public void ForceVisualUpdate()
        {
            UpdateSystem();
        }

        // =========================================================
        // PUBLIC API
        // =========================================================

        /// <summary>
        /// API/GUI Compatibility: Maps a master preset index to the corresponding 
        /// module indices and applies them simultaneously without overhead.
        /// </summary>
        public void SetWeatherProfile(int index)
        {
            if (presetToLighting == null || presetToSky == null || presetToCloud == null ||
                presetToMoon == null || presetToFog == null || presetToEffects == null) return;

            if (index < 0 || index >= presetToLighting.Length) return;

            SetModuleStates(
                presetToLighting[index],
                presetToSky[index],
                presetToCloud[index],
                presetToMoon[index],
                presetToFog[index],
                presetToEffects[index]
            );
        }

        public void SetModuleStates(int lighting, int sky, int cloud, int moon, int fog, int effects)
        {
            CancelAllTransitions();
            SetCurrentAndTarget(MOD_LIGHTING, lighting);
            SetCurrentAndTarget(MOD_SKY, sky);
            SetCurrentAndTarget(MOD_CLOUD, cloud);
            SetCurrentAndTarget(MOD_MOON, moon);
            SetCurrentAndTarget(MOD_FOG, fog);
            SetCurrentAndTarget(MOD_EFFECTS, effects);
            UpdateSystem();
        }

        public void SetCloudState(int index) { SetModuleState(MOD_CLOUD, index); }
        public void SetFogState(int index) { SetModuleState(MOD_FOG, index); }
        public void SetEffectsState(int index) { SetModuleState(MOD_EFFECTS, index); }

        public void SetPlayerCameraPosition(Vector3 position)
        {
            if (_weatherEffectsManager != null)
            {
                _weatherEffectsManager.UpdatePosition(position);
            }
        }

        public void SetExternalTime(float sunTime01)
        {
            _isExternallyControlled = true;
            _sunTimeOfDay = Mathf.Clamp01(sunTime01);
            UpdateSystem();
        }

        public void ReleaseExternalControl()
        {
            _isExternallyControlled = false;
        }

        // =========================================================
        // INTERNAL LOGIC
        // =========================================================

        /// <summary>
        /// Main update loop. Computes sun/moon positions from astronomical parameters
        /// (season, time, latitude) and dispatches the data to all subsystems using 
        /// the currently active per-module baked data (Lighting, Sky, Clouds, etc.).
        /// </summary>
        private void UpdateSystem()
        {
            if (bakedLightingModules == null || bakedLightingModules.Length == 0) return;
            if (_lightingManager == null) return;

            ClampAllIndices();
            InitTransitionArrays();

            // === RESOLVE ===
            float tL = GetModuleTransitionFactor(MOD_LIGHTING);
            float tS = GetModuleTransitionFactor(MOD_SKY);
            float tC = GetModuleTransitionFactor(MOD_CLOUD);
            float tM = GetModuleTransitionFactor(MOD_MOON);
            float tF = GetModuleTransitionFactor(MOD_FOG);
            float tE = GetModuleTransitionFactor(MOD_EFFECTS);

            BakedProfileData lp = ResolveLightingState(tL);
            BakedProfileData sp = ResolveSkyState(tS);
            BakedProfileData cp = ResolveCloudState(tC);
            BakedProfileData mp = ResolveMoonState(tM);
            BakedProfileData fp = ResolveFogState(tF);
            BakedProfileData ep = ResolveEffectsState(tE);

            if (lp == null) return;

            int effectiveEffectsIndex = (_modTransitioning != null && _modTransitioning[MOD_EFFECTS])
                ? _targetEffectsIndex : _effectsIndex;
            if (_lastActiveProfileIndex != effectiveEffectsIndex)
            {
                _lastActiveProfileIndex = effectiveEffectsIndex;
                if (ep != null && ep.weatherEffectPrefab == null)
                {
                    Debug.LogWarning($"<b><color=#FF9900>[WARNING]</color></b> <color=white>[WeatherTimeSystem] Effects profile at index {effectiveEffectsIndex} has no Weather Effect Prefab.</color>", this);
                }
            }

            // --- ASTRONOMY ---
            float safeDays = Mathf.Max(1f, daysInYear);
            float yearProgress = (dayOfYear % safeDays) / safeDays;
            float sunDeclination = axialTilt * Mathf.Sin((yearProgress - 0.22f) * Mathf.PI * 2f);

            Vector3 sunDir = ComputeCelestialDirection(_sunTimeOfDay, sunDeclination, latitude);
            Quaternion sunLightRot = Quaternion.LookRotation(-sunDir);

            float draconicMonth = 27.2122f;
            float moonEclipticOffset = 5.14f * Mathf.Sin((dayOfYear / draconicMonth) * Mathf.PI * 2f);
            float moonDeclination = sunDeclination + moonEclipticOffset;

            Vector3 moonDir = ComputeCelestialDirection(_moonTimeOfDay, moonDeclination, latitude);
            Quaternion moonLightRot = Quaternion.LookRotation(-moonDir);

            float sunMoonDot = Vector3.Dot(sunDir, moonDir);
            float moonPhaseFraction = Mathf.Clamp01(0.5f - 0.5f * sunMoonDot);
            float moonPhaseBrightness = moonPhaseFraction * moonPhaseFraction * moonPhaseFraction;

            // === DISPATCH ===
            Color particleLightColor = ApplyLighting(sunLightRot, moonLightRot, lp, moonPhaseBrightness);
            Color currentFogColor = ApplyFog(fp, lp);
            ApplySky(sp, cp, mp, fp, currentFogColor);
            ApplyEffects(ep, particleLightColor);
        }

        /// <summary>
        /// Converts time-of-day, declination, and observer latitude into a world-space
        /// direction vector using standard equatorial-to-horizontal transformation.
        /// Convention: +X=East, +Y=Up, +Z=North. TimeOfDay 0=midnight, 0.5=noon.
        /// </summary>
        private Vector3 ComputeCelestialDirection(float timeOfDay, float declination, float latitudeDeg)
        {
            float haDeg = (timeOfDay - 0.5f) * 360f;

            float ha = haDeg * Mathf.Deg2Rad;
            float dec = declination * Mathf.Deg2Rad;

            float sinDec = Mathf.Sin(dec);
            float cosDec = Mathf.Cos(dec);
            float sinHA = Mathf.Sin(ha);
            float cosHA = Mathf.Cos(ha);

            float x_eq = -cosDec * sinHA;
            float y_eq = sinDec;
            float z_eq = -cosDec * cosHA;

            float tiltAngle = (90f - latitudeDeg) * Mathf.Deg2Rad;
            float sinTilt = Mathf.Sin(tiltAngle);
            float cosTilt = Mathf.Cos(tiltAngle);

            float x = x_eq;
            float y = y_eq * cosTilt - z_eq * sinTilt;
            float z = y_eq * sinTilt + z_eq * cosTilt;

            return new Vector3(x, y, z).normalized;
        }

        /// <summary>
        /// Applies the calculated celestial rotations and active lighting profile data.
        /// Calculates the final directional and ambient light colors.
        /// </summary>
        private Color ApplyLighting(Quaternion sunRotation, Quaternion moonRotation,
            BakedProfileData lp, float moonPhaseBrightness)
        {
            _lightingManager.UpdateLighting(
                sunRotation, moonRotation,
                _sunTimeOfDay, _moonTimeOfDay,
                lp.sunColorHorizon, lp.sunColorZenith, lp.sunIntensity,
                lp.moonLightColor, lp.moonLightIntensity,
                moonPhaseBrightness,
                lp.daySkyColor, lp.dayEquatorColor, lp.dayGroundColor,
                lp.nightSkyColor, lp.nightEquatorColor, lp.nightGroundColor
            );

            return _lightingManager.CalculateCurrentGlobalLight(
                _sunTimeOfDay, _moonTimeOfDay,
                lp.sunColorHorizon, lp.sunColorZenith, lp.sunIntensity,
                lp.moonLightColor, lp.moonLightIntensity,
                moonPhaseBrightness,
                lp.daySkyColor, lp.nightSkyColor
            );
        }

        /// <summary>
        /// Applies fog settings based on the active fog index, blending it dynamically with the time of day.
        /// </summary>
        private Color ApplyFog(BakedProfileData fp, BakedProfileData lp)
        {
            Color currentFogColor = Color.black;

            if (fp != null && fp.fogEnabled)
            {
                var sunLight = _lightingManager.SunLight;
                if (sunLight == null)
                {
                    RenderSettings.fog = false;
                    return Color.black;
                }

                float sunHeight = -sunLight.transform.forward.y;
                float fogDayFactor = Mathf.InverseLerp(-0.1f, 0.1f, sunHeight);
                float sunIntensityFactor = lp.sunIntensity > 0
                    ? (sunLight.intensity / lp.sunIntensity) : 0;

                float finalFogMix = Mathf.Clamp01(fogDayFactor * sunIntensityFactor);
                currentFogColor = Color.Lerp(fp.fogNightColor, fp.fogDayColor, finalFogMix);

                RenderSettings.fog = true;
                RenderSettings.fogMode = (FogMode)fp.fogMode;
                RenderSettings.fogColor = currentFogColor;
                RenderSettings.fogDensity = fp.fogDensity;
                RenderSettings.fogStartDistance = fp.fogStartDistance;
                RenderSettings.fogEndDistance = fp.fogEndDistance;
            }
            else
            {
                RenderSettings.fog = false;
            }

            return currentFogColor;
        }

        /// <summary>
        /// Updates skybox material with atmosphere, starfield, moon, clouds, and fog.
        /// Computes Local Sidereal Time for proper celestial pole rotation.
        /// </summary>
        private void ApplySky(BakedProfileData sp, BakedProfileData cp,
                      BakedProfileData mp, BakedProfileData fp, Color currentFogColor)
        {
            if (_skyManager == null) return;

            var sun = _lightingManager.SunLight;
            var moon = _lightingManager.MoonLight;

            if (sun != null && sp != null)
            {
                _skyManager.UpdateSky(sun.transform.forward,
                    sp.rayleigh, sp.turbidity,
                    sp.mieCoefficient, sp.mieDirectionalG, sp.exposure);

                // Stars
                float safeDays = Mathf.Max(1f, daysInYear);
                float vernalEquinoxDay = safeDays * 0.22f;
                float yearOffset = ((dayOfYear - vernalEquinoxDay) / safeDays) * 360f;
                float siderealAngle = _sunTimeOfDay * 360f * sp.starsRotationSpeed + yearOffset + 180f;
                float latTilt = 90f - latitude;

                Vector3 starfieldEuler = new Vector3(
                    -latTilt + sp.starfieldAlignment.x,
                    siderealAngle + sp.starfieldAlignment.y,
                    sp.starfieldAlignment.z);

                _skyManager.UpdateStars(starfieldEuler, sun.transform.forward,
                    sp.starsCubemap, sp.milkyWayCubemap,
                    sp.starsIntensity, sp.milkyWayIntensity,
                    sp.twinkleScale, sp.twinkleSharpness, sp.twinkleSpeed, sp.twinkleStrength);

                _skyManager.UpdateNightSky(sp.airglowIntensity, sp.airglowColor, sp.airglowHeight);

                // Clouds
                if (cp != null)
                {
                    _skyManager.UpdateClouds(
                        cp.cloudNoiseTexture, cp.weatherMapTexture, cp.blueNoiseTexture,
                        cp.curlNoiseTexture,
                        cp.cloudAltitude, cp.cloudScale, cp.cloudCoverage,
                        cp.cloudDensity, cp.cloudDetailAmount, cp.cloudWispiness,
                        cp.cloudWindSpeed, cp.cloudBaseColor, cp.cloudShadowColor,
                        cp.cloudLightScattering);

                    _skyManager.UpdateCirrus(
                        cp.cirrusNoiseTexture,
                        cp.cirrusCoverage, cp.cirrusOpacity,
                        cp.cirrusScale, cp.cirrusWindSpeed, cp.cirrusTint);
                }

                // Fog in skybox
                if (fp != null)
                {
                    _skyManager.UpdateSkyboxFog(fp.fogEnabled, currentFogColor, fp.fogSkyBlend);
                }
            }

            // Moon
            if (moon != null && mp != null)
            {
                Color finalMoonColor = _lightingManager.MoonSkyboxColor * mp.moonTint;
                _skyManager.UpdateMoon(moon.transform.forward,
                    mp.moonTexture, finalMoonColor, mp.moonSize);
            }
        }

        /// <summary>
        /// Updates the active particle effects (rain/snow) and applies current global lighting to them.
        /// </summary>
        private void ApplyEffects(BakedProfileData ep, Color particleLightColor)
        {
            if (_weatherEffectsManager == null) return;

            if (ep == null || ep.weatherEffectPrefab == null)
            {
                _weatherEffectsManager.UpdateWeatherEffects(null, 0);
                return;
            }

            _weatherEffectsManager.SetHeightOffset(ep.spawnHeightOffset);
            _weatherEffectsManager.UpdateWeatherEffects(ep.weatherEffectPrefab, ep.spawnHeightOffset);
            _weatherEffectsManager.UpdateEffectsLighting(
                particleLightColor,
                ep.volumeBounds,
                ep.particleSize,
                ep.weatherDensity);
        }

        private void ClampAllIndices()
        {
            for (int i = 0; i < MODULE_COUNT; i++)
            {
                BakedProfileData[] arr = GetBakedArray(i);
                int max = (arr != null) ? arr.Length : 0;
                if (max == 0) continue;
                if (GetIndex(i, false) >= max) SetIndex(i, false, 0);
                if (GetIndex(i, true) >= max) SetIndex(i, true, 0);
            }
        }

        /// <summary>
        /// Generic instant module setter.
        /// </summary>
        public void SetModuleState(int module, int value)
        {
            BakedProfileData[] arr = GetBakedArray(module);
            if (arr == null || value < 0 || value >= arr.Length) return;
            SetCurrentAndTarget(module, value);
            UpdateSystem();
        }

        public bool IsTransitioning
        {
            get
            {
                if (_modTransitioning == null) return false;
                for (int i = 0; i < MODULE_COUNT; i++)
                    if (_modTransitioning[i]) return true;
                return false;
            }
        }

        public bool IsModuleTransitioning(int moduleIndex)
        {
            if (_modTransitioning == null || moduleIndex < 0 || moduleIndex >= MODULE_COUNT)
                return false;
            return _modTransitioning[moduleIndex];
        }

        public float GetModuleProgress(int moduleIndex)
        {
            if (_modTransitioning == null || moduleIndex < 0 || moduleIndex >= MODULE_COUNT)
                return 0f;
            if (!_modTransitioning[moduleIndex]) return 0f;
            float dur = _modTransitionDuration[moduleIndex];
            if (dur <= 0f) return 1f;
            return Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(_modTransitionElapsed[moduleIndex] / dur));
        }

        /// <summary>
        /// Returns the index the dropdown should display:
        /// target if transitioning, current if idle.
        /// </summary>
        public int GetDisplayModuleIndex(int moduleIndex)
        {
            if (_modTransitioning != null && moduleIndex >= 0 && moduleIndex < MODULE_COUNT
                && _modTransitioning[moduleIndex])
            {
                return GetTargetIndex(moduleIndex);
            }
            return GetCurrentIndex(moduleIndex);
        }

        // =========================================================
        // MODULE ROUTING
        // =========================================================

        /// <summary>
        /// Returns current or target index for a module.
        /// </summary>
        private int GetIndex(int module, bool target)
        {
            switch (module)
            {
                case MOD_LIGHTING: return target ? _targetLightingIndex : _lightingIndex;
                case MOD_SKY: return target ? _targetSkyIndex : _skyIndex;
                case MOD_CLOUD: return target ? _targetCloudIndex : _cloudIndex;
                case MOD_MOON: return target ? _targetMoonIndex : _moonIndex;
                case MOD_FOG: return target ? _targetFogIndex : _fogIndex;
                case MOD_EFFECTS: return target ? _targetEffectsIndex : _effectsIndex;
                default: return 0;
            }
        }

        /// <summary>
        /// Sets current or target index for a module.
        /// </summary>
        private void SetIndex(int module, bool target, int value)
        {
            switch (module)
            {
                case MOD_LIGHTING: if (target) _targetLightingIndex = value; else _lightingIndex = value; break;
                case MOD_SKY: if (target) _targetSkyIndex = value; else _skyIndex = value; break;
                case MOD_CLOUD: if (target) _targetCloudIndex = value; else _cloudIndex = value; break;
                case MOD_MOON: if (target) _targetMoonIndex = value; else _moonIndex = value; break;
                case MOD_FOG: if (target) _targetFogIndex = value; else _fogIndex = value; break;
                case MOD_EFFECTS: if (target) _targetEffectsIndex = value; else _effectsIndex = value; break;
            }
        }

        /// <summary>
        /// Routes module constant to its baked data array.
        /// </summary>
        private BakedProfileData[] GetBakedArray(int module)
        {
            switch (module)
            {
                case MOD_LIGHTING: return bakedLightingModules;
                case MOD_SKY: return bakedSkyModules;
                case MOD_CLOUD: return bakedCloudModules;
                case MOD_MOON: return bakedMoonModules;
                case MOD_FOG: return bakedFogModules;
                case MOD_EFFECTS: return bakedEffectsModules;
                default: return null;
            }
        }

        // =========================================================
        // TRANSITION API
        // =========================================================

        private void InitTransitionArrays()
        {
            if (_modTransitioning == null || _modTransitioning.Length != MODULE_COUNT)
            {
                _modTransitioning = new bool[MODULE_COUNT];
                _modTransitionElapsed = new float[MODULE_COUNT];
                _modTransitionDuration = new float[MODULE_COUNT];
            }
        }

        /// <summary>
        /// Advances all active module timers. Called once per frame.
        /// </summary>
        private void TickModuleTransitions()
        {
            if (_modTransitioning == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < MODULE_COUNT; i++)
            {
                if (!_modTransitioning[i]) continue;
                _modTransitionElapsed[i] += dt;
                if (_modTransitionElapsed[i] >= _modTransitionDuration[i])
                {
                    FinalizeModuleTransition(i);
                }
            }
        }

        private void FinalizeModuleTransition(int moduleIndex)
        {
            if (_modTransitioning != null)
                _modTransitioning[moduleIndex] = false;
            CommitTargetToCurrent(moduleIndex);
        }

        private void CancelAllTransitions()
        {
            if (_modTransitioning == null) return;
            for (int i = 0; i < MODULE_COUNT; i++)
                _modTransitioning[i] = false;
        }

        /// <summary>
        /// Returns the raw transition factor for a module (0 if idle).
        /// </summary>
        private float GetModuleTransitionFactor(int moduleIndex)
        {
            if (_modTransitioning == null || !_modTransitioning[moduleIndex]) return 0f;
            float dur = _modTransitionDuration[moduleIndex];
            if (dur <= 0f) return 0f;
            return Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(_modTransitionElapsed[moduleIndex] / dur));
        }

        /// <summary>
        /// Starts a per-module transition.
        /// Usage: TransitionModuleTo(MOD_CLOUD, 2, 15f)
        /// </summary>
        public void TransitionModuleTo(int module, int targetIndex, float duration)
        {
            InitTransitionArrays();

            if (duration <= 0f)
            {
                SetCurrentAndTarget(module, targetIndex);
                return;
            }

            if (_modTransitioning[module])
            {
                FinalizeModuleTransition(module);
            }

            SetIndex(module, true, targetIndex);
            _modTransitionDuration[module] = duration;
            _modTransitionElapsed[module] = 0f;
            _modTransitioning[module] = true;
        }

        // =========================================================
        // TRANSITION PUBLIC API
        // =========================================================

        /// <summary>
        /// Transitions ALL modules to a master profile.
        /// </summary>
        public void TransitionToWeatherProfile(int index, float duration)
        {
            if (presetToLighting == null || index < 0 || index >= presetToLighting.Length) return;

            TransitionToModuleStates(
                presetToLighting[index], presetToSky[index],
                presetToCloud[index], presetToMoon[index],
                presetToFog[index], presetToEffects[index],
                duration
            );
        }

        /// <summary>
        /// Convenience overload using defaultTransitionDuration.
        /// </summary>
        public void TransitionToWeatherProfile(int index)
        {
            TransitionToWeatherProfile(index, defaultTransitionDuration);
        }

        /// <summary>
        /// Transitions ALL modules independently but simultaneously.
        /// </summary>
        public void TransitionToModuleStates(int lighting, int sky, int cloud,
            int moon, int fog, int effects, float duration)
        {
            if (duration <= 0f)
            {
                SetModuleStates(lighting, sky, cloud, moon, fog, effects);
                return;
            }

            TransitionModuleTo(MOD_LIGHTING, lighting, duration);
            TransitionModuleTo(MOD_SKY, sky, duration);
            TransitionModuleTo(MOD_CLOUD, cloud, duration);
            TransitionModuleTo(MOD_MOON, moon, duration);
            TransitionModuleTo(MOD_FOG, fog, duration);
            TransitionModuleTo(MOD_EFFECTS, effects, duration);
        }

        // =========================================================
        // RESOLVE LAYER
        // =========================================================

        private BakedProfileData SafeGet(BakedProfileData[] array, int index)
        {
            if (array == null || array.Length == 0) return null;
            if (index < 0 || index >= array.Length) return null;
            return array[index];
        }

        // --- Lighting ---

        private BakedProfileData ResolveLightingState(float t)
        {
            BakedProfileData from = SafeGet(bakedLightingModules, _lightingIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedLighting == null || _lightingIndex == _targetLightingIndex) return from;

            BakedProfileData to = SafeGet(bakedLightingModules, _targetLightingIndex);
            if (to == null) return from;

            LerpLightingFields(_resolvedLighting, from, to, t);
            return _resolvedLighting;
        }

        private void LerpLightingFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.sunColorHorizon = Color.Lerp(a.sunColorHorizon, b.sunColorHorizon, t);
            r.sunColorZenith = Color.Lerp(a.sunColorZenith, b.sunColorZenith, t);
            r.sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t);
            r.moonLightColor = Color.Lerp(a.moonLightColor, b.moonLightColor, t);
            r.moonLightIntensity = Mathf.Lerp(a.moonLightIntensity, b.moonLightIntensity, t);
            r.daySkyColor = Color.Lerp(a.daySkyColor, b.daySkyColor, t);
            r.dayEquatorColor = Color.Lerp(a.dayEquatorColor, b.dayEquatorColor, t);
            r.dayGroundColor = Color.Lerp(a.dayGroundColor, b.dayGroundColor, t);
            r.nightSkyColor = Color.Lerp(a.nightSkyColor, b.nightSkyColor, t);
            r.nightEquatorColor = Color.Lerp(a.nightEquatorColor, b.nightEquatorColor, t);
            r.nightGroundColor = Color.Lerp(a.nightGroundColor, b.nightGroundColor, t);
        }

        // --- Sky ---

        private BakedProfileData ResolveSkyState(float t)
        {
            BakedProfileData from = SafeGet(bakedSkyModules, _skyIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedSky == null || _skyIndex == _targetSkyIndex) return from;

            BakedProfileData to = SafeGet(bakedSkyModules, _targetSkyIndex);
            if (to == null) return from;

            LerpSkyFields(_resolvedSky, from, to, t);
            return _resolvedSky;
        }

        private void LerpSkyFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.rayleigh = Mathf.Lerp(a.rayleigh, b.rayleigh, t);
            r.turbidity = Mathf.Lerp(a.turbidity, b.turbidity, t);
            r.mieCoefficient = Mathf.Lerp(a.mieCoefficient, b.mieCoefficient, t);
            r.mieDirectionalG = Mathf.Lerp(a.mieDirectionalG, b.mieDirectionalG, t);
            r.exposure = Mathf.Lerp(a.exposure, b.exposure, t);

            r.starsRotationSpeed = Mathf.Lerp(a.starsRotationSpeed, b.starsRotationSpeed, t);
            r.starfieldAlignment = Vector3.Lerp(a.starfieldAlignment, b.starfieldAlignment, t);

            r.starsCubemap = t < 0.5f ? a.starsCubemap : b.starsCubemap;
            r.milkyWayCubemap = t < 0.5f ? a.milkyWayCubemap : b.milkyWayCubemap;

            r.starsIntensity = Mathf.Lerp(a.starsIntensity, b.starsIntensity, t);
            r.milkyWayIntensity = Mathf.Lerp(a.milkyWayIntensity, b.milkyWayIntensity, t);
            r.twinkleScale = Mathf.Lerp(a.twinkleScale, b.twinkleScale, t);
            r.twinkleSharpness = Mathf.Lerp(a.twinkleSharpness, b.twinkleSharpness, t);
            r.twinkleSpeed = Mathf.Lerp(a.twinkleSpeed, b.twinkleSpeed, t);
            r.twinkleStrength = Mathf.Lerp(a.twinkleStrength, b.twinkleStrength, t);

            r.airglowIntensity = Mathf.Lerp(a.airglowIntensity, b.airglowIntensity, t);
            r.airglowColor = Color.Lerp(a.airglowColor, b.airglowColor, t);
            r.airglowHeight = Mathf.Lerp(a.airglowHeight, b.airglowHeight, t);
        }

        // --- Clouds ---

        private BakedProfileData ResolveCloudState(float t)
        {
            BakedProfileData from = SafeGet(bakedCloudModules, _cloudIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedCloud == null || _cloudIndex == _targetCloudIndex) return from;

            BakedProfileData to = SafeGet(bakedCloudModules, _targetCloudIndex);
            if (to == null) return from;

            LerpCloudFields(_resolvedCloud, from, to, t);
            return _resolvedCloud;
        }

        private void LerpCloudFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.cloudNoiseTexture = t < 0.5f ? a.cloudNoiseTexture : b.cloudNoiseTexture;
            r.weatherMapTexture = t < 0.5f ? a.weatherMapTexture : b.weatherMapTexture;
            r.blueNoiseTexture = t < 0.5f ? a.blueNoiseTexture : b.blueNoiseTexture;
            r.curlNoiseTexture = t < 0.5f ? a.curlNoiseTexture : b.curlNoiseTexture;
            r.cirrusNoiseTexture = t < 0.5f ? a.cirrusNoiseTexture : b.cirrusNoiseTexture;

            r.cloudAltitude = Mathf.Lerp(a.cloudAltitude, b.cloudAltitude, t);
            r.cloudScale = Mathf.Lerp(a.cloudScale, b.cloudScale, t);
            r.cloudCoverage = Mathf.Lerp(a.cloudCoverage, b.cloudCoverage, t);
            r.cloudDensity = Mathf.Lerp(a.cloudDensity, b.cloudDensity, t);
            r.cloudDetailAmount = Mathf.Lerp(a.cloudDetailAmount, b.cloudDetailAmount, t);
            r.cloudWispiness = Mathf.Lerp(a.cloudWispiness, b.cloudWispiness, t);
            r.cloudWindSpeed = Vector2.Lerp(a.cloudWindSpeed, b.cloudWindSpeed, t);
            r.cloudBaseColor = Color.Lerp(a.cloudBaseColor, b.cloudBaseColor, t);
            r.cloudShadowColor = Color.Lerp(a.cloudShadowColor, b.cloudShadowColor, t);
            r.cloudLightScattering = Mathf.Lerp(a.cloudLightScattering, b.cloudLightScattering, t);

            r.cirrusCoverage = Mathf.Lerp(a.cirrusCoverage, b.cirrusCoverage, t);
            r.cirrusOpacity = Mathf.Lerp(a.cirrusOpacity, b.cirrusOpacity, t);
            r.cirrusScale = Mathf.Lerp(a.cirrusScale, b.cirrusScale, t);
            r.cirrusWindSpeed = Vector2.Lerp(a.cirrusWindSpeed, b.cirrusWindSpeed, t);
            r.cirrusTint = Color.Lerp(a.cirrusTint, b.cirrusTint, t);
        }

        // --- Moon ---

        private BakedProfileData ResolveMoonState(float t)
        {
            BakedProfileData from = SafeGet(bakedMoonModules, _moonIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedMoon == null || _moonIndex == _targetMoonIndex) return from;

            BakedProfileData to = SafeGet(bakedMoonModules, _targetMoonIndex);
            if (to == null) return from;

            LerpMoonFields(_resolvedMoon, from, to, t);
            return _resolvedMoon;
        }

        private void LerpMoonFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.moonTint = Color.Lerp(a.moonTint, b.moonTint, t);
            r.moonTexture = t < 0.5f ? a.moonTexture : b.moonTexture;
            r.moonSize = Mathf.Lerp(a.moonSize, b.moonSize, t);
        }

        // --- Fog ---

        private BakedProfileData ResolveFogState(float t)
        {
            BakedProfileData from = SafeGet(bakedFogModules, _fogIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedFog == null || _fogIndex == _targetFogIndex) return from;

            BakedProfileData to = SafeGet(bakedFogModules, _targetFogIndex);
            if (to == null) return from;

            LerpFogFields(_resolvedFog, from, to, t);
            return _resolvedFog;
        }

        private void LerpFogFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.fogEnabled = t < 0.5f ? a.fogEnabled : b.fogEnabled;
            r.fogNightColor = Color.Lerp(a.fogNightColor, b.fogNightColor, t);
            r.fogDayColor = Color.Lerp(a.fogDayColor, b.fogDayColor, t);
            r.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            r.fogStartDistance = Mathf.Lerp(a.fogStartDistance, b.fogStartDistance, t);
            r.fogEndDistance = Mathf.Lerp(a.fogEndDistance, b.fogEndDistance, t);
            r.fogMode = t < 0.5f ? a.fogMode : b.fogMode;
            r.fogSkyBlend = Mathf.Lerp(a.fogSkyBlend, b.fogSkyBlend, t);
        }

        // --- Effects ---

        private BakedProfileData ResolveEffectsState(float t)
        {
            BakedProfileData from = SafeGet(bakedEffectsModules, _effectsIndex);
            if (from == null) return null;
            if (t <= 0f || _resolvedEffects == null || _effectsIndex == _targetEffectsIndex) return from;

            BakedProfileData to = SafeGet(bakedEffectsModules, _targetEffectsIndex);
            if (to == null) return from;

            LerpEffectsFields(_resolvedEffects, from, to, t);
            return _resolvedEffects;
        }

        private void LerpEffectsFields(BakedProfileData r, BakedProfileData a, BakedProfileData b, float t)
        {
            r.weatherEffectPrefab = t < 0.5f ? a.weatherEffectPrefab : b.weatherEffectPrefab;
            r.spawnHeightOffset = Mathf.Lerp(a.spawnHeightOffset, b.spawnHeightOffset, t);
            r.volumeBounds = Vector3.Lerp(a.volumeBounds, b.volumeBounds, t);
            r.particleSize = Mathf.Lerp(a.particleSize, b.particleSize, t);
            r.weatherDensity = Mathf.Lerp(a.weatherDensity, b.weatherDensity, t);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void WarnAboutExternalLights()
        {
            Light[] allLights = FindObjectsOfType<Light>();
            foreach (var light in allLights)
            {
                if (light.type == LightType.Directional && !IsOurLight(light))
                {
                    Debug.LogWarning($"<b><color=#FF9900>[WARNING]</color></b> <color=white>[WeatherTimeSystem] Found an external Directional Light named '{light.gameObject.name}'.</color>", light.gameObject);
                }
            }
        }

        private bool IsOurLight(Light light)
        {
            if (_lightingManager == null) return false;
            return light == _lightingManager.SunLight || light == _lightingManager.MoonLight;
        }
#endif
    }
}
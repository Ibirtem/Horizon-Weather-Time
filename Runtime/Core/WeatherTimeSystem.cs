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
        private bool _requiresEditorUpdate = false;

        [Header("Weather Settings")]
        [Tooltip("List of Weather Profiles. Stored as generic Objects for Udon compatibility.")]
        public UnityEngine.Object[] weatherProfilesList;

        [SerializeField, HideInInspector] private int _bakedProfileCount = 0;

        [Tooltip("The index of the currently active profile.")]
        [SerializeField] private int _currentProfileIndex = 0;

        [Header("Independent Module States")]
        [SerializeField, HideInInspector] private int _lightingIndex = 0;
        [SerializeField, HideInInspector] private int _skyIndex = 0;
        [SerializeField, HideInInspector] private int _cloudIndex = 0;
        [SerializeField, HideInInspector] private int _moonIndex = 0;
        [SerializeField, HideInInspector] private int _fogIndex = 0;
        [SerializeField, HideInInspector] private int _effectsIndex = 0;

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
        [Header("Baked Data")]
        [SerializeField] private Color[] _bake_sunZenith;
        [SerializeField] private Color[] _bake_sunHorizon;
        [SerializeField] private float[] _bake_sunIntensity;

        [SerializeField] private Color[] _bake_moonColor;
        [SerializeField] private float[] _bake_moonIntensity;

        [SerializeField] private Color[] _bake_daySky;
        [SerializeField] private Color[] _bake_dayEquator;
        [SerializeField] private Color[] _bake_dayGround;

        [SerializeField] private Color[] _bake_nightSky;
        [SerializeField] private Color[] _bake_nightEquator;
        [SerializeField] private Color[] _bake_nightGround;

        [SerializeField] private float[] _bake_rayleigh;
        [SerializeField] private float[] _bake_turbidity;
        [SerializeField] private float[] _bake_mieCoeff;
        [SerializeField] private float[] _bake_mieG;
        [SerializeField] private float[] _bake_exposure;

        [SerializeField] private Texture[] _bake_starsTex;
        [SerializeField] private Texture[] _bake_milkyWayTex;
        [SerializeField] private Vector3[] _bake_starAlignment;
        [SerializeField] private float[] _bake_starSpeed;
        [SerializeField] private float[] _bake_starsIntensity;
        [SerializeField] private float[] _bake_milkyWayIntensity;

        [SerializeField] private float[] _bake_twinkleScale;
        [SerializeField] private float[] _bake_twinkleSpeed;
        [SerializeField] private float[] _bake_twinkleStrength;

        [SerializeField] private GameObject[] _bake_effectPrefab;
        [SerializeField] private float[] _bake_effectHeight;

        [SerializeField] private Vector3[] _bake_volumeBounds;
        [SerializeField] private float[] _bake_particleSize;
        [SerializeField] private float[] _bake_weatherDensity;

        [SerializeField] private Texture[] _bake_moonTex;
        [SerializeField] private float[] _bake_moonSize;
        [SerializeField] private Color[] _bake_moonTint;

        [SerializeField] private Texture[] _bake_cloudTex;
        [SerializeField] private float[] _bake_cloudAltitude;
        [SerializeField] private float[] _bake_cloudScale;
        [SerializeField] private float[] _bake_cloudCoverage;
        [SerializeField] private float[] _bake_cloudDensity;
        [SerializeField] private float[] _bake_cloudDetail;
        [SerializeField] private float[] _bake_cloudWisp;
        [SerializeField] private Vector2[] _bake_cloudWind;
        [SerializeField] private Color[] _bake_cloudColor;
        [SerializeField] private Color[] _bake_cloudShadow;
        [SerializeField] private float[] _bake_cloudScatter;
        [SerializeField] private Texture[] _bake_weatherMapTex;
        [SerializeField] private Texture[] _bake_blueNoiseTex;

        [SerializeField] private bool[] _bake_fogEnabled;
        [SerializeField] private int[] _bake_fogMode;
        [SerializeField] private Color[] _bake_fogDay;
        [SerializeField] private Color[] _bake_fogNight;
        [SerializeField] private float[] _bake_fogDensity;
        [SerializeField] private float[] _bake_fogStart;
        [SerializeField] private float[] _bake_fogEnd;
        [SerializeField] private float[] _bake_fogSkyBlend;
        // =========================================================

        [Header("System References")]
        [SerializeField] private LightingManager _lightingManager;
        [SerializeField] private SkyManager _skyManager;
        [SerializeField] private WeatherEffectsManager _weatherEffectsManager;
        [SerializeField] private ReflectionManager _reflectionManager;

        private const float SECONDS_IN_DAY = 86400f;

        public float TimeOfDay => _sunTimeOfDay;

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

        private void OnValidate()
        {
            daysInYear = Mathf.Max(1f, daysInYear);
            dayOfYear = Mathf.Clamp(dayOfYear, 1f, daysInYear);

            if (!Application.isPlaying) 
            {
                BakeProfiles();
                
                UnityEditor.EditorApplication.delayCall += () => 
                {
                    if (this == null) return; 
                    if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying) return;
                    
                    UpdateSystem();
                };
            }
        }

        private void BakeProfiles()
        {
            if (weatherProfilesList == null) 
            {
                _bakedProfileCount = 0;
                return;
            }
            int count = weatherProfilesList.Length;
            _bakedProfileCount = count;

            // Arrays Init
            _bake_sunZenith = new Color[count];
            _bake_sunHorizon = new Color[count];
            _bake_sunIntensity = new float[count];
            _bake_moonColor = new Color[count];
            _bake_moonIntensity = new float[count];
            
            _bake_daySky = new Color[count];
            _bake_dayEquator = new Color[count];
            _bake_dayGround = new Color[count];
            _bake_nightSky = new Color[count];
            _bake_nightEquator = new Color[count];
            _bake_nightGround = new Color[count];

            _bake_rayleigh = new float[count];
            _bake_turbidity = new float[count];
            _bake_mieCoeff = new float[count];
            _bake_mieG = new float[count];
            _bake_exposure = new float[count];

            // --- STARS & DEEP SPACE ---
            _bake_starsTex = new Texture[count];
            _bake_milkyWayTex = new Texture[count];
            _bake_starAlignment = new Vector3[count];
            _bake_starSpeed = new float[count];
            _bake_starsIntensity = new float[count];
            _bake_milkyWayIntensity = new float[count];
            
            _bake_twinkleScale = new float[count];
            _bake_twinkleSpeed = new float[count];
            _bake_twinkleStrength = new float[count];

            _bake_effectPrefab = new GameObject[count];
            _bake_effectHeight = new float[count];

            _bake_volumeBounds = new Vector3[count];
            _bake_particleSize = new float[count];
            _bake_weatherDensity = new float[count];


            _bake_moonTex = new Texture[count];
            _bake_moonSize = new float[count];
            _bake_moonTint = new Color[count];

            _bake_cloudTex = new Texture[count];
            _bake_weatherMapTex = new Texture[count];
            _bake_blueNoiseTex = new Texture[count];
            _bake_cloudAltitude = new float[count];
            _bake_cloudScale = new float[count];
            _bake_cloudCoverage = new float[count];
            _bake_cloudDensity = new float[count];
            _bake_cloudDetail = new float[count];
            _bake_cloudWisp = new float[count];
            _bake_cloudWind = new Vector2[count];
            _bake_cloudColor = new Color[count];
            _bake_cloudShadow = new Color[count];
            _bake_cloudScatter = new float[count];

            _bake_fogEnabled = new bool[count];
            _bake_fogMode = new int[count];
            _bake_fogDay = new Color[count];
            _bake_fogNight = new Color[count];
            _bake_fogDensity = new float[count];
            _bake_fogStart = new float[count];
            _bake_fogEnd = new float[count];
            _bake_fogSkyBlend = new float[count];

            for (int i = 0; i < count; i++)
            {
                var p = weatherProfilesList[i] as WeatherProfile;
                if (p == null) continue;

                // Lighting
                var light = p.lightingProfile;
                _bake_sunZenith[i] = light != null ? light.sunColorZenith : Color.white;
                _bake_sunHorizon[i] = light != null ? light.sunColorHorizon : new Color(1f, 0.7f, 0.4f);
                _bake_sunIntensity[i] = light != null ? light.sunIntensity : 1.0f;
                _bake_moonColor[i] = light != null ? light.moonColor : new Color(0.8f, 0.9f, 1f);
                _bake_moonIntensity[i] = light != null ? light.moonIntensity : 0.04f;
                
                _bake_daySky[i] = light != null ? light.daySkyColor : new Color(0.4f, 0.5f, 0.6f);
                _bake_dayEquator[i] = light != null ? light.dayEquatorColor : new Color(0.3f, 0.35f, 0.4f);
                _bake_dayGround[i] = light != null ? light.dayGroundColor : new Color(0.2f, 0.2f, 0.2f);
                
                _bake_nightSky[i] = light != null ? light.nightSkyColor : new Color(0.05f, 0.05f, 0.1f);
                _bake_nightEquator[i] = light != null ? light.nightEquatorColor : new Color(0.02f, 0.02f, 0.05f);
                _bake_nightGround[i] = light != null ? light.nightGroundColor : new Color(0.01f, 0.01f, 0.02f);

                // Sky & Deep Space
                var sky = p.skyProfile;
                _bake_rayleigh[i] = sky != null ? sky.rayleigh : 1.0f;
                _bake_turbidity[i] = sky != null ? sky.turbidity : 5.0f;
                _bake_mieCoeff[i] = sky != null ? sky.mieCoefficient : 0.005f;
                _bake_mieG[i] = sky != null ? sky.mieDirectionalG : 0.8f;
                _bake_exposure[i] = sky != null ? sky.exposure : 0.3f;

                _bake_starsTex[i] = sky != null ? sky.starsTexture : null;
                _bake_milkyWayTex[i] = sky != null ? sky.milkyWayTexture : null;
                _bake_starAlignment[i] = sky != null ? sky.starfieldAlignment : Vector3.zero;
                _bake_starSpeed[i] = sky != null ? sky.starsRotationSpeed : 0.5f;
                _bake_starsIntensity[i] = sky != null ? sky.starsIntensity : 1.0f;
                _bake_milkyWayIntensity[i] = sky != null ? sky.milkyWayIntensity : 1.0f;

                _bake_twinkleScale[i] = sky != null ? sky.twinkleScale : 150f;
                _bake_twinkleSpeed[i] = sky != null ? sky.twinkleSpeed : 0.7f;
                _bake_twinkleStrength[i] = sky != null ? sky.twinkleStrength : 0.8f;

                // Effects
                var fx = p.effectsProfile;
                _bake_effectPrefab[i] = fx != null ? fx.weatherEffectPrefab : null;
                _bake_effectHeight[i] = fx != null ? fx.spawnHeightOffset : 15f;

                _bake_volumeBounds[i] = fx != null ? fx.volumeBounds : new Vector3(40, 40, 40);
                _bake_particleSize[i] = fx != null ? fx.particleSize : 0.05f;
                _bake_weatherDensity[i] = fx != null ? fx.weatherDensity : 1.0f;

                // Moon
                var moon = p.moonProfile;
                _bake_moonTex[i] = moon != null ? moon.moonTexture : null;
                _bake_moonSize[i] = moon != null ? moon.moonSize : 0.02f;
                _bake_moonTint[i] = moon != null ? moon.moonColor : Color.white;

                // Clouds
                var clouds = p.cloudProfile;
                _bake_cloudTex[i] = clouds != null ? clouds.cloudNoiseTexture : null;
                _bake_weatherMapTex[i] = clouds != null ? clouds.weatherMapTexture : null;
                _bake_blueNoiseTex[i] = clouds != null ? clouds.blueNoiseTexture : null;
                
                _bake_cloudAltitude[i] = clouds != null ? clouds.altitude : 4.0f;
                _bake_cloudScale[i] = clouds != null ? clouds.scale : 3.5f;
                _bake_cloudCoverage[i] = (clouds != null && clouds.enabled) ? clouds.coverage : 0.0f;
                _bake_cloudDensity[i] = clouds != null ? clouds.density : 1.0f;
                _bake_cloudDetail[i] = clouds != null ? clouds.detailAmount : 0.5f;
                _bake_cloudWisp[i] = clouds != null ? clouds.wispiness : 0.3f;
                _bake_cloudWind[i] = clouds != null ? clouds.windSpeed : Vector2.zero;
                _bake_cloudColor[i] = clouds != null ? clouds.baseColor : Color.white;
                _bake_cloudShadow[i] = clouds != null ? clouds.shadowColor : Color.gray;
                _bake_cloudScatter[i] = clouds != null ? clouds.lightScattering : 2.0f;
                
                // Fog
                var fog = p.fogProfile;
                _bake_fogEnabled[i] = fog != null && fog.enabled;
                _bake_fogMode[i] = fog != null ? (int)fog.fogMode : 1; 
                _bake_fogDay[i] = fog != null ? fog.dayColor : Color.gray;
                _bake_fogNight[i] = fog != null ? fog.nightColor : Color.black;
                _bake_fogDensity[i] = fog != null ? fog.density : 0.002f;
                _bake_fogStart[i] = fog != null ? fog.startDistance : 10f;
                _bake_fogEnd[i] = fog != null ? fog.endDistance : 250f;
                _bake_fogSkyBlend[i] = fog != null ? fog.skyboxBlendIntegrity : 1.0f;
            }
        }
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

        public void Editor_HotReloadProfile(WeatherProfile changedProfile)
        {
            if (this == null) return; 
            
            BakeProfiles();
            UpdateSystem();

            if (!Application.isPlaying)
            {
                UnityEditor.SceneView.RepaintAll();
            }
        }
#endif

        private void Start()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            WarnAboutExternalLights();
            BakeProfiles();
#endif
            Refresh();
        }

        private void Update()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying && _requiresEditorUpdate)
            {
                _requiresEditorUpdate = false;
                UpdateSystem();
            }
#endif
            bool isPlaying = true;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            isPlaying = Application.isPlaying;
#endif

            if (isPlaying && !_isExternallyControlled)
            {
                CalculateTime();
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
            _moonTimeOfDay = (_sunTimeOfDay + 0.5f) % 1.0f;

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

                _moonTimeOfDay = (_sunTimeOfDay + moonPhase) % 1.0f;
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
                    _moonTimeOfDay = (_sunTimeOfDay + moonPhase) % 1.0f;
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

        public void SetExternalTime(float sunTime01)
        {
            _isExternallyControlled = true;
            _sunTimeOfDay = sunTime01;
            UpdateSystem();
        }

        public void ReleaseExternalControl()
        {
            _isExternallyControlled = false;
        }

        /// <summary>
        /// Sets the global weather preset. This overrides all individual module states 
        /// (clouds, fog, lighting, etc.) to match the selected base preset.
        /// </summary>
        /// <param name="index">The index of the WeatherProfile in the weatherProfilesList.</param>
        public void SetWeatherProfile(int index)
        {
            if (index >= 0 && index < _bakedProfileCount)
            {
                _currentProfileIndex = index;

                _lightingIndex = index;
                _skyIndex = index;
                _cloudIndex = index;
                _moonIndex = index;
                _fogIndex = index;
                _effectsIndex = index;

                UpdateSystem();
            }
        }

        /// <summary>
        /// Independently overrides the active cloud layer state without affecting the rest of the weather.
        /// Useful for dynamically rolling in storm clouds while keeping ambient lighting intact.
        /// </summary>
        /// <param name="cloudPresetIndex">The index of the baked profile from which to read cloud data.</param>
        public void SetCloudState(int cloudPresetIndex)
        {
            if (cloudPresetIndex >= 0 && cloudPresetIndex < _bakedProfileCount)
            {
                _cloudIndex = cloudPresetIndex;
                UpdateSystem();
            }
        }

        /// <summary>
        /// Independently overrides the active fog layer state.
        /// Ideal for creating morning mist or localized thick fog scenarios.
        /// </summary>
        /// <param name="fogPresetIndex">The index of the baked profile from which to read fog data.</param>
        public void SetFogState(int fogPresetIndex)
        {
            if (fogPresetIndex >= 0 && fogPresetIndex < _bakedProfileCount)
            {
                _fogIndex = fogPresetIndex;
                UpdateSystem();
            }
        }

        /// <summary>
        /// Independently sets the weather effects (particles like rain/snow).
        /// </summary>
        public void SetEffectsState(int effectsPresetIndex)
        {
            if (effectsPresetIndex >= 0 && effectsPresetIndex < _bakedProfileCount)
            {
                _effectsIndex = effectsPresetIndex;
                UpdateSystem();
            }
        }

        public void SetPlayerCameraPosition(Vector3 position)
        {
            if (_weatherEffectsManager != null)
            {
                _weatherEffectsManager.UpdatePosition(position);
            }
        }

        // =========================================================
        // INTERNAL LOGIC
        // =========================================================

        /// <summary>
        /// Main update loop. Computes sun/moon positions from astronomical parameters
        /// (season, time, latitude) and dispatches to all subsystems.
        /// </summary>
        private void UpdateSystem()
        {
            if (_bakedProfileCount == 0 || weatherProfilesList == null) return;

            if (_currentProfileIndex < 0 || _currentProfileIndex >= _bakedProfileCount)
                _currentProfileIndex = 0;

            if (_bake_sunZenith == null || _lightingIndex >= _bake_sunZenith.Length) return;
            if (_lightingManager == null) return;

            if (_lastActiveProfileIndex != _effectsIndex)
            {
                _lastActiveProfileIndex = _effectsIndex;

                if (_bake_effectPrefab != null && _effectsIndex < _bake_effectPrefab.Length)
                {
                    if (_bake_effectPrefab[_effectsIndex] == null)
                    {
                        Debug.LogWarning($"<b><color=#FF9900>[WARNING]</color></b> <color=white>[WeatherTimeSystem] Effects Module at index {_effectsIndex} has NO Weather Effect Prefab assigned. Particles will be skipped.</color>", this);
                    }
                }
            }

            // --- ASTRONOMY CALCULATIONS ---
            float safeDays = Mathf.Max(1f, daysInYear);
            float yearProgress = (dayOfYear % safeDays) / safeDays;

            float sunDeclination = axialTilt * Mathf.Sin((yearProgress - 0.22f) * Mathf.PI * 2f);

            Vector3 sunDir = ComputeCelestialDirection(_sunTimeOfDay, sunDeclination, latitude);
            Quaternion sunLightRot = Quaternion.LookRotation(-sunDir);

            float moonEclipticOffset = 5.14f * Mathf.Sin(moonPhase * Mathf.PI * 2f);
            float moonDeclination = sunDeclination + moonEclipticOffset;

            Vector3 moonDir = ComputeCelestialDirection(_moonTimeOfDay, moonDeclination, latitude);
            Quaternion moonLightRot = Quaternion.LookRotation(-moonDir);

            Color particleLightColor = ApplyLighting(sunLightRot, moonLightRot);
            Color currentFogColor = ApplyFog();
            ApplySky(currentFogColor);
            ApplyEffects(particleLightColor);
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
        private Color ApplyLighting(Quaternion sunRotation, Quaternion moonRotation)
        {
            int idx = _lightingIndex;

            if (_bake_daySky == null || idx >= _bake_daySky.Length) return Color.white;

            _lightingManager.UpdateLighting(
                sunRotation, moonRotation,
                _sunTimeOfDay, _moonTimeOfDay,
                _bake_sunHorizon[idx], _bake_sunZenith[idx], _bake_sunIntensity[idx],
                _bake_moonColor[idx], _bake_moonIntensity[idx],
                _bake_daySky[idx], _bake_dayEquator[idx], _bake_dayGround[idx],
                _bake_nightSky[idx], _bake_nightEquator[idx], _bake_nightGround[idx]
            );

            return _lightingManager.CalculateCurrentGlobalLight(
                _sunTimeOfDay, _moonTimeOfDay,
                _bake_sunHorizon[idx], _bake_sunZenith[idx], _bake_sunIntensity[idx],
                _bake_moonColor[idx], _bake_moonIntensity[idx],
                _bake_daySky[idx], _bake_nightSky[idx]
            );
        }

        /// <summary>
        /// Applies fog settings based on the active fog index, blending it dynamically with the time of day.
        /// </summary>
        private Color ApplyFog()
        {
            int idx = _fogIndex;
            Color currentFogColor = Color.black;

            if (_bake_fogEnabled != null && idx < _bake_fogEnabled.Length && _bake_fogEnabled[idx])
            {
                var sunLight = _lightingManager.SunLight;
                if (sunLight == null)
                {
                    RenderSettings.fog = false;
                    return Color.black;
                }

                float sunHeight = -sunLight.transform.forward.y;
                float fogDayFactor = Mathf.InverseLerp(-0.1f, 0.1f, sunHeight);
                float sunIntensityFactor = _bake_sunIntensity[_lightingIndex] > 0 ? (sunLight.intensity / _bake_sunIntensity[_lightingIndex]) : 0;

                float finalFogMix = Mathf.Clamp01(fogDayFactor * sunIntensityFactor);
                currentFogColor = Color.Lerp(_bake_fogNight[idx], _bake_fogDay[idx], finalFogMix);

                RenderSettings.fog = true;
                RenderSettings.fogMode = (FogMode)_bake_fogMode[idx];
                RenderSettings.fogColor = currentFogColor;
                RenderSettings.fogDensity = _bake_fogDensity[idx];
                RenderSettings.fogStartDistance = _bake_fogStart[idx];
                RenderSettings.fogEndDistance = _bake_fogEnd[idx];
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
        private void ApplySky(Color currentFogColor)
        {
            if (_skyManager == null) return;

            var sun = _lightingManager.SunLight;
            var moon = _lightingManager.MoonLight;

            if (sun != null && _bake_rayleigh != null && _skyIndex < _bake_rayleigh.Length)
            {
                _skyManager.UpdateSky(sun.transform.forward,
                    _bake_rayleigh[_skyIndex], _bake_turbidity[_skyIndex],
                    _bake_mieCoeff[_skyIndex], _bake_mieG[_skyIndex], _bake_exposure[_skyIndex]
                );

                Texture safeStars = (_bake_starsTex != null && _skyIndex < _bake_starsTex.Length) ? _bake_starsTex[_skyIndex] : null;
                Texture safeMW = (_bake_milkyWayTex != null && _skyIndex < _bake_milkyWayTex.Length) ? _bake_milkyWayTex[_skyIndex] : null;

                Vector3 baseAlign = _bake_starAlignment[_skyIndex];

                float safeDays = Mathf.Max(1f, daysInYear);
                float vernalEquinoxDay = safeDays * 0.22f;
                float yearOffset = ((dayOfYear - vernalEquinoxDay) / safeDays) * 360f;
                float siderealAngle = _sunTimeOfDay * 360f * _bake_starSpeed[_skyIndex] + yearOffset + 180f;

                float latTilt = 90f - latitude;

                Vector3 starfieldEuler = new Vector3(
                    -latTilt + baseAlign.x,
                    siderealAngle + baseAlign.y,
                    baseAlign.z
                );

                _skyManager.UpdateStars(starfieldEuler, sun.transform.forward,
                    safeStars, safeMW,
                    _bake_starsIntensity[_skyIndex],
                    _bake_milkyWayIntensity[_skyIndex],
                    _bake_twinkleScale[_skyIndex],
                    _bake_twinkleSpeed[_skyIndex],
                    _bake_twinkleStrength[_skyIndex]
                );

                if (_bake_cloudAltitude != null && _cloudIndex < _bake_cloudAltitude.Length)
                {
                    Texture safeCloudTex = (_bake_cloudTex != null && _cloudIndex < _bake_cloudTex.Length) ? _bake_cloudTex[_cloudIndex] : null;
                    Texture safeWeatherMap = (_bake_weatherMapTex != null && _cloudIndex < _bake_weatherMapTex.Length) ? _bake_weatherMapTex[_cloudIndex] : null;
                    Texture safeBlueNoise = (_bake_blueNoiseTex != null && _cloudIndex < _bake_blueNoiseTex.Length) ? _bake_blueNoiseTex[_cloudIndex] : null;

                    _skyManager.UpdateClouds(safeCloudTex, safeWeatherMap, safeBlueNoise,
                        _bake_cloudAltitude[_cloudIndex], _bake_cloudScale[_cloudIndex], _bake_cloudCoverage[_cloudIndex],
                        _bake_cloudDensity[_cloudIndex], _bake_cloudDetail[_cloudIndex], _bake_cloudWisp[_cloudIndex],
                        _bake_cloudWind[_cloudIndex], _bake_cloudColor[_cloudIndex], _bake_cloudShadow[_cloudIndex],
                        _bake_cloudScatter[_cloudIndex]
                    );
                }

                if (_bake_fogEnabled != null && _fogIndex < _bake_fogEnabled.Length)
                {
                    _skyManager.UpdateSkyboxFog(_bake_fogEnabled[_fogIndex], currentFogColor, _bake_fogSkyBlend[_fogIndex]);
                }
            }

            if (moon != null && _bake_moonSize != null && _moonIndex < _bake_moonSize.Length)
            {
                Texture safeMoonTex = (_bake_moonTex != null && _moonIndex < _bake_moonTex.Length) ? _bake_moonTex[_moonIndex] : null;
                _skyManager.UpdateMoon(moon.transform.forward, safeMoonTex, _lightingManager.MoonSkyboxColor, _bake_moonSize[_moonIndex]);
            }
        }

        /// <summary>
        /// Updates the active particle effects (rain/snow) and applies current global lighting to them.
        /// </summary>
        private void ApplyEffects(Color particleLightColor)
        {
            if (_weatherEffectsManager == null) return;

            int idx = _effectsIndex;

            if (_bake_effectPrefab == null || idx >= _bake_effectPrefab.Length || _bake_effectPrefab[idx] == null)
            {
                _weatherEffectsManager.UpdateWeatherEffects(null, 0);
                return;
            }

            if (_bake_effectHeight != null && idx < _bake_effectHeight.Length)
            {
                _weatherEffectsManager.SetHeightOffset(_bake_effectHeight[idx]);
                _weatherEffectsManager.UpdateWeatherEffects(_bake_effectPrefab[idx], _bake_effectHeight[idx]);

                Vector3 volumeBounds = (_bake_volumeBounds != null && idx < _bake_volumeBounds.Length)
                    ? _bake_volumeBounds[idx] : new Vector3(40, 40, 40);
                float particleSize = (_bake_particleSize != null && idx < _bake_particleSize.Length)
                    ? _bake_particleSize[idx] : 0.05f;
                float weatherDensity = (_bake_weatherDensity != null && idx < _bake_weatherDensity.Length)
                    ? _bake_weatherDensity[idx] : 1.0f;

                _weatherEffectsManager.UpdateEffectsLighting(
                    particleLightColor,
                    volumeBounds,
                    particleSize,
                    weatherDensity
                );
            }
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
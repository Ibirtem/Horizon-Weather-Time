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

        [Header("Baked Runtime Data (Auto-generated)")]
        [Tooltip("Do not edit manually. Populated by the baking system.")]
        public BakedProfileData[] bakedProfiles;

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
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                && !Application.isPlaying) return;

            if (WeatherBakeUtility.NeedsRebake(this))
            {
                WeatherBakeUtility.BakeAllProfiles(this);
            }
            else if (bakedProfiles != null)
            {
                for (int i = 0; i < bakedProfiles.Length; i++)
                {
                    WeatherBakeUtility.RebakeSingleProfile(this, i);
                }
            }

            UpdateSystem();
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

            if (changedProfile != null && weatherProfilesList != null)
            {
                for (int i = 0; i < weatherProfilesList.Length; i++)
                {
                    if (weatherProfilesList[i] == changedProfile)
                    {
                        WeatherBakeUtility.RebakeSingleProfile(this, i);
                    }
                }
            }
            else
            {
                if (bakedProfiles != null)
                {
                    for (int i = 0; i < bakedProfiles.Length; i++)
                    {
                        WeatherBakeUtility.RebakeSingleProfile(this, i);
                    }
                }
            }

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
            WeatherBakeUtility.BakeAllProfiles(this);
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
            if (bakedProfiles == null) return;
            if (index >= 0 && index < bakedProfiles.Length)
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
            if (bakedProfiles != null && cloudPresetIndex >= 0
                && cloudPresetIndex < bakedProfiles.Length)
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
            if (bakedProfiles != null && fogPresetIndex >= 0
                && fogPresetIndex < bakedProfiles.Length)
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
            if (bakedProfiles != null && effectsPresetIndex >= 0
                && effectsPresetIndex < bakedProfiles.Length)
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
            if (bakedProfiles == null || bakedProfiles.Length == 0) return;
            if (_lightingManager == null) return;

            int profileCount = bakedProfiles.Length;
            if (_lightingIndex >= profileCount) _lightingIndex = 0;
            if (_skyIndex >= profileCount) _skyIndex = 0;
            if (_cloudIndex >= profileCount) _cloudIndex = 0;
            if (_moonIndex >= profileCount) _moonIndex = 0;
            if (_fogIndex >= profileCount) _fogIndex = 0;
            if (_effectsIndex >= profileCount) _effectsIndex = 0;

            BakedProfileData lp = bakedProfiles[_lightingIndex];  // Lighting
            BakedProfileData sp = bakedProfiles[_skyIndex];       // Sky
            BakedProfileData cp = bakedProfiles[_cloudIndex];     // Clouds
            BakedProfileData mp = bakedProfiles[_moonIndex];      // Moon
            BakedProfileData fp = bakedProfiles[_fogIndex];       // Fog
            BakedProfileData ep = bakedProfiles[_effectsIndex];   // Effects

            if (lp == null) return;

            if (_lastActiveProfileIndex != _effectsIndex)
            {
                _lastActiveProfileIndex = _effectsIndex;
                if (ep != null && ep.weatherEffectPrefab == null)
                {
                    Debug.LogWarning($"<b><color=#FF9900>[WARNING]</color></b> <color=white>[WeatherTimeSystem] Effects profile at index {_effectsIndex} has no Weather Effect Prefab.</color>", this);
                }
            }

            // --- ASTRONOMY ---
            float safeDays = Mathf.Max(1f, daysInYear);
            float yearProgress = (dayOfYear % safeDays) / safeDays;
            float sunDeclination = axialTilt * Mathf.Sin((yearProgress - 0.22f) * Mathf.PI * 2f);

            Vector3 sunDir = ComputeCelestialDirection(_sunTimeOfDay, sunDeclination, latitude);
            Quaternion sunLightRot = Quaternion.LookRotation(-sunDir);

            float moonEclipticOffset = 5.14f * Mathf.Sin(moonPhase * Mathf.PI * 2f);
            float moonDeclination = sunDeclination + moonEclipticOffset;
            Vector3 moonDir = ComputeCelestialDirection(_moonTimeOfDay, moonDeclination, latitude);
            Quaternion moonLightRot = Quaternion.LookRotation(-moonDir);

            // --- DISPATCH TO MODULES ---
            Color particleLightColor = ApplyLighting(sunLightRot, moonLightRot, lp);
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
        private Color ApplyLighting(Quaternion sunRotation, Quaternion moonRotation, BakedProfileData lp)
        {
            _lightingManager.UpdateLighting(
                sunRotation, moonRotation,
                _sunTimeOfDay, _moonTimeOfDay,
                lp.sunColorHorizon, lp.sunColorZenith, lp.sunIntensity,
                lp.moonLightColor, lp.moonLightIntensity,
                lp.daySkyColor, lp.dayEquatorColor, lp.dayGroundColor,
                lp.nightSkyColor, lp.nightEquatorColor, lp.nightGroundColor
            );

            return _lightingManager.CalculateCurrentGlobalLight(
                _sunTimeOfDay, _moonTimeOfDay,
                lp.sunColorHorizon, lp.sunColorZenith, lp.sunIntensity,
                lp.moonLightColor, lp.moonLightIntensity,
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
                    sp.twinkleScale, sp.twinkleSpeed, sp.twinkleStrength);

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
                _skyManager.UpdateMoon(moon.transform.forward,
                    mp.moonTexture, _lightingManager.MoonSkyboxColor, mp.moonSize);
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
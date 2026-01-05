using System;
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
    public class WeatherTimeSystem : UdonSharpBehaviour
#else
    public class WeatherTimeSystem : MonoBehaviour
#endif
    {
        [Header("Time Settings")]
        public bool useRealTime = true;
        public bool simulateMoonPhase = true;
        [Range(-12f, 14f)]
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

        private bool _isExternallyControlled = false;
        private bool _requiresEditorUpdate = false;

        [Header("Weather Settings")]
        [Tooltip("List of Weather Profiles. Stored as generic Objects for Udon compatibility.")]
        public UnityEngine.Object[] weatherProfilesList;

        [SerializeField, HideInInspector] private int _bakedProfileCount = 0;

        [Tooltip("The index of the currently active profile.")]
        [SerializeField] private int _currentProfileIndex = 0;

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

        [SerializeField] private Color[] _bake_dayAmbient;
        [SerializeField] private Color[] _bake_nightAmbient;

        [SerializeField] private float[] _bake_rayleigh;
        [SerializeField] private float[] _bake_turbidity;
        [SerializeField] private float[] _bake_mieCoeff;
        [SerializeField] private float[] _bake_mieG;
        [SerializeField] private float[] _bake_exposure;

        [SerializeField] private Texture[] _bake_starsTex;
        [SerializeField] private float[] _bake_starsRot;
        [SerializeField] private float[] _bake_twinkleScale;
        [SerializeField] private float[] _bake_twinkleSpeed;
        [SerializeField] private float[] _bake_twinkleStrength;

        [SerializeField] private GameObject[] _bake_effectPrefab;
        [SerializeField] private float[] _bake_effectHeight;

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

            _bake_sunZenith = new Color[count];
            _bake_sunHorizon = new Color[count];
            _bake_sunIntensity = new float[count];
            _bake_moonColor = new Color[count];
            _bake_moonIntensity = new float[count];
            _bake_dayAmbient = new Color[count];
            _bake_nightAmbient = new Color[count];

            _bake_rayleigh = new float[count];
            _bake_turbidity = new float[count];
            _bake_mieCoeff = new float[count];
            _bake_mieG = new float[count];
            _bake_exposure = new float[count];

            _bake_starsTex = new Texture[count];
            _bake_starsRot = new float[count];
            _bake_twinkleScale = new float[count];
            _bake_twinkleSpeed = new float[count];
            _bake_twinkleStrength = new float[count];

            _bake_effectPrefab = new GameObject[count];
            _bake_effectHeight = new float[count];

            _bake_moonTex = new Texture[count];
            _bake_moonSize = new float[count];
            _bake_moonTint = new Color[count];

            _bake_cloudTex = new Texture[count];
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

            for (int i = 0; i < count; i++)
            {
                var p = weatherProfilesList[i] as WeatherProfile;
                if (p == null) continue;

                // Lighting
                _bake_sunZenith[i] = p.lightSettings.sunColorZenith;
                _bake_sunHorizon[i] = p.lightSettings.sunColorHorizon;
                _bake_sunIntensity[i] = p.lightSettings.sunIntensity;
                _bake_moonColor[i] = p.lightSettings.moonColor;
                _bake_moonIntensity[i] = p.lightSettings.moonIntensity;
                _bake_dayAmbient[i] = p.lightSettings.dayAmbientColor;
                _bake_nightAmbient[i] = p.lightSettings.nightAmbientColor;

                // Sky
                _bake_rayleigh[i] = p.skySettings.rayleigh;
                _bake_turbidity[i] = p.skySettings.turbidity;
                _bake_mieCoeff[i] = p.skySettings.mieCoefficient;
                _bake_mieG[i] = p.skySettings.mieDirectionalG;
                _bake_exposure[i] = p.skySettings.exposure;

                // Stars
                _bake_starsTex[i] = p.skySettings.starsSettings.starsTexture;
                _bake_starsRot[i] = p.skySettings.starsSettings.starsRotationSpeed;
                _bake_twinkleScale[i] = p.skySettings.starsSettings.twinkleScale;
                _bake_twinkleSpeed[i] = p.skySettings.starsSettings.twinkleSpeed;
                _bake_twinkleStrength[i] = p.skySettings.starsSettings.twinkleStrength;

                // Effects
                _bake_effectPrefab[i] = p.effectsSettings.weatherEffectPrefab;
                _bake_effectHeight[i] = p.effectsSettings.spawnHeightOffset;

                // Moon
                _bake_moonTex[i] = p.moonSettings.moonTexture;
                _bake_moonSize[i] = p.moonSettings.moonSize;
                _bake_moonTint[i] = p.moonSettings.moonColor;

                // Clouds
                _bake_cloudTex[i] = p.cloudSettings.cloudNoiseTexture;
                _bake_cloudAltitude[i] = p.cloudSettings.altitude;
                _bake_cloudScale[i] = p.cloudSettings.scale;
                _bake_cloudCoverage[i] = p.cloudSettings.coverage;
                _bake_cloudDensity[i] = p.cloudSettings.density;
                _bake_cloudDetail[i] = p.cloudSettings.detailAmount;
                _bake_cloudWisp[i] = p.cloudSettings.wispiness;
                _bake_cloudWind[i] = p.cloudSettings.windSpeed;
                _bake_cloudColor[i] = p.cloudSettings.baseColor;
                _bake_cloudShadow[i] = p.cloudSettings.shadowColor;
                _bake_cloudScatter[i] = p.cloudSettings.lightScattering;
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

        public void Editor_HotReloadProfile()
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

        private void CalculateTime()
        {
            if (useRealTime)
            {
                DateTime currentUtc = DateTime.UtcNow;
                DateTime instanceTime = currentUtc.AddHours(timeZoneOffset);

                double totalSecondsInDay = instanceTime.TimeOfDay.TotalSeconds;
                _sunTimeOfDay = (float)(totalSecondsInDay / SECONDS_IN_DAY);

                double dayOfYearFractional = instanceTime.DayOfYear + _sunTimeOfDay;

                moonPhase = (float)((dayOfYearFractional % lunarCycleDays) / lunarCycleDays);

                _moonTimeOfDay = (_sunTimeOfDay + moonPhase) % 1.0f;
            }
            else
            {
                bool shouldSimulate = true;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                shouldSimulate = Application.isPlaying;
#endif
                if (shouldSimulate)
                {
                    float dayDelta = (Time.deltaTime * timeSpeed) / SECONDS_IN_DAY;

                    _sunTimeOfDay += dayDelta;

                    if (simulateMoonPhase)
                    {
                        moonPhase += dayDelta / lunarCycleDays;

                        if (moonPhase > 1.0f) moonPhase -= 1.0f;
                    }

                    _sunTimeOfDay %= 1.0f;
                }

                if (simulateMoonPhase)
                {
                    _moonTimeOfDay = (_sunTimeOfDay + moonPhase) % 1.0f;
                }
            }
        }

        public void Refresh()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying) CalculateTime();
#endif
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

        public void SetWeatherProfile(int index)
        {
            if (index >= 0 && index < _bakedProfileCount)
            {
                _currentProfileIndex = index;
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
        // INTERNAL LOGIC (Using Baked Data Only)
        // =========================================================

        private void UpdateSystem()
        {
            if (_bakedProfileCount == 0) return;

            if (_currentProfileIndex >= _bakedProfileCount) _currentProfileIndex = 0;
            int idx = _currentProfileIndex;

            if (_bake_sunZenith == null || idx >= _bake_sunZenith.Length) return;
            if (_lightingManager == null) return;

            // 1. Update Lights
            _lightingManager.UpdateLighting(
                _sunTimeOfDay, _moonTimeOfDay,
                _bake_sunHorizon[idx], _bake_sunZenith[idx], _bake_sunIntensity[idx],
                _bake_moonColor[idx], _bake_moonIntensity[idx],
                _bake_dayAmbient[idx], _bake_nightAmbient[idx]
            );

            // 2. Calculate Lighting Color for Particles
            Color particleLightColor = _lightingManager.CalculateCurrentGlobalLight(
                _sunTimeOfDay, _moonTimeOfDay,
                _bake_sunHorizon[idx], _bake_sunZenith[idx], _bake_sunIntensity[idx],
                _bake_moonColor[idx], _bake_moonIntensity[idx],
                _bake_dayAmbient[idx], _bake_nightAmbient[idx]
            );

            // 3. Update Skybox & Stars
            if (_skyManager != null)
            {
                var sun = _lightingManager.SunLight;
                var moon = _lightingManager.MoonLight;

                if (sun != null)
                {
                    _skyManager.UpdateSky(sun.transform.forward,
                        _bake_rayleigh[idx],
                        _bake_turbidity[idx],
                        _bake_mieCoeff[idx],
                        _bake_mieG[idx],
                        _bake_exposure[idx]
                    );

                    _skyManager.UpdateStars(_sunTimeOfDay, sun.transform.forward,
                        _bake_starsTex[idx],
                        _bake_starsRot[idx],
                        _bake_twinkleScale[idx],
                        _bake_twinkleSpeed[idx],
                        _bake_twinkleStrength[idx]
                    );

                    _skyManager.UpdateClouds(
                        _bake_cloudTex[idx],
                        _bake_cloudAltitude[idx],
                        _bake_cloudScale[idx],
                        _bake_cloudCoverage[idx],
                        _bake_cloudDensity[idx],
                        _bake_cloudDetail[idx],
                        _bake_cloudWisp[idx],
                        _bake_cloudWind[idx],
                        _bake_cloudColor[idx],
                        _bake_cloudShadow[idx],
                        _bake_cloudScatter[idx]
                    );
                }

                if (moon != null)
                {
                    _skyManager.UpdateMoon(
                        moon.transform.forward,
                        _bake_moonTex[idx],
                        _lightingManager.MoonSkyboxColor,
                        _bake_moonSize[idx]
                    );
                }
            }

            // 4. Update Weather Effects (Rain/Snow)
            if (_weatherEffectsManager != null)
            {
                if (idx < _bake_effectHeight.Length && idx < _bake_effectPrefab.Length)
                {
                    _weatherEffectsManager.SetHeightOffset(_bake_effectHeight[idx]);
                    _weatherEffectsManager.UpdateWeatherEffects(_bake_effectPrefab[idx], _bake_effectHeight[idx]);
                    _weatherEffectsManager.UpdateEffectsLighting(particleLightColor);
                }
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
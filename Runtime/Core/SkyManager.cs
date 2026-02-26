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
    public class SkyManager : UdonSharpBehaviour
#else
    public class SkyManager : MonoBehaviour
#endif
    {
        [Header("Configuration")]
        [Tooltip("The base material asset for the skybox.")]
        public Material skyboxMaterial;
        [SerializeField] private Shader skyboxShader;

        private Material _skyboxInstance;
        private Material _originalSkybox;
        private Texture _currentStarsTexture;
        private Texture _currentMoonTexture;
        private Texture _currentCloudTexture;
        private Vector2 _currentCloudOffset;

        private bool _areIDsInitialized = false;

        // --- Shader Property IDs ---
        // Atmosphere
        private int SunPositionID;
        private int RayleighID;
        private int TurbidityID;
        private int MieCoefficientID;
        private int MieDirectionalGID;
        private int ExposureID;

        // Stars
        private int StarsTexID;
        private int MilkyWayTexID;
        private int StarfieldRotationID;
        private int StarsIntensityID;
        private int MilkyWayIntensityID;

        // Twinkle
        private int StarsFadeID;
        private int TwinkleScaleID;
        private int TwinkleDetailID;
        private int TwinkleSharpnessID;
        private int TwinkleSpeedID;
        private int TwinkleStrengthID;

        // Moon
        private int MoonTexID;
        private int MoonColorID;
        private int MoonSizeID;
        private int MoonPositionID;

        // Clouds
        private int CloudTexID, CloudScaleID, CloudCoverageID, CloudWindID, CloudColorID, CloudShadowColorID;
        private int CloudAltitudeID, CloudDensityID, CloudDetailID, CloudWispID, CloudScatterID;

        private int WeatherMapTexID, BlueNoiseTexID;

        // Fog
        private int FogColorID;
        private int FogBlendID;

        private void InitializeShaderIDs()
        {
            // Atmosphere
            SunPositionID = VRCShader.PropertyToID("_SunPosition");
            RayleighID = VRCShader.PropertyToID("_Rayleigh");
            TurbidityID = VRCShader.PropertyToID("_Turbidity");
            MieCoefficientID = VRCShader.PropertyToID("_MieCoefficient");
            MieDirectionalGID = VRCShader.PropertyToID("_MieDirectionalG");
            ExposureID = VRCShader.PropertyToID("_Exposure");

            // Stars
            StarsTexID = VRCShader.PropertyToID("_StarsTex");
            MilkyWayTexID = VRCShader.PropertyToID("_MilkyWayTex");
            StarfieldRotationID = VRCShader.PropertyToID("_StarfieldRotation");
            StarsIntensityID = VRCShader.PropertyToID("_StarsIntensity");
            MilkyWayIntensityID = VRCShader.PropertyToID("_MilkyWayIntensity");

            StarsFadeID = VRCShader.PropertyToID("_StarsFade");
            TwinkleScaleID = VRCShader.PropertyToID("_TwinkleScale");
            TwinkleDetailID = VRCShader.PropertyToID("_TwinkleDetail");
            TwinkleSharpnessID = VRCShader.PropertyToID("_TwinkleSharpness");
            TwinkleSpeedID = VRCShader.PropertyToID("_TwinkleSpeed");
            TwinkleStrengthID = VRCShader.PropertyToID("_TwinkleStrength");

            // Moon
            MoonTexID = VRCShader.PropertyToID("_MoonTex");
            MoonColorID = VRCShader.PropertyToID("_MoonColor");
            MoonSizeID = VRCShader.PropertyToID("_MoonSize");
            MoonPositionID = VRCShader.PropertyToID("_MoonPosition");

            // Clouds
            CloudTexID = VRCShader.PropertyToID("_CloudTex");
            CloudScaleID = VRCShader.PropertyToID("_CloudScale");
            CloudCoverageID = VRCShader.PropertyToID("_CloudCoverage");
            CloudWindID = VRCShader.PropertyToID("_CloudWind");
            CloudColorID = VRCShader.PropertyToID("_CloudColor");
            CloudShadowColorID = VRCShader.PropertyToID("_CloudShadowColor");
            CloudAltitudeID = VRCShader.PropertyToID("_CloudAltitude");
            CloudDensityID = VRCShader.PropertyToID("_CloudDensity");
            CloudDetailID = VRCShader.PropertyToID("_CloudDetail");
            CloudWispID = VRCShader.PropertyToID("_CloudWisp");
            CloudScatterID = VRCShader.PropertyToID("_CloudScatter");

            WeatherMapTexID = VRCShader.PropertyToID("_WeatherMapTex");
            BlueNoiseTexID = VRCShader.PropertyToID("_BlueNoiseTex");

            FogColorID = VRCShader.PropertyToID("_HorizonFogColor");
            FogBlendID = VRCShader.PropertyToID("_HorizonFogBlend");

            _areIDsInitialized = true;
        }

        private void EnsureInitialized()
        {
            if (!_areIDsInitialized) InitializeShaderIDs();
            if (_skyboxInstance == null) InitializeSkybox();
        }

        private void OnEnable()
        {
            InitializeShaderIDs();
            InitializeSkybox();
        }

        private void OnDisable()
        {
            CleanupSkybox();
        }

        private void InitializeSkybox()
        {
            if (_skyboxInstance != null) return;

            if (skyboxMaterial == null)
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                if (skyboxShader == null) skyboxShader = Shader.Find("Horizon/Procedural Skybox");
                if (skyboxShader != null)
                {
                    _skyboxInstance = new Material(skyboxShader);
                    _skyboxInstance.name = "Horizon Skybox (Generated)";
                }
#else
                Debug.LogError("<b><color=#FF3333>[ERROR]</color></b> <color=white>[SkyManager] Skybox Material missing!</color>");
                return;
#endif
            }
            else
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                _skyboxInstance = new Material(skyboxMaterial);
                _skyboxInstance.name = "Horizon Skybox (Instance)";
#else
                _skyboxInstance = skyboxMaterial;
#endif
            }

            if (_skyboxInstance != null && RenderSettings.skybox != _skyboxInstance)
            {
                _originalSkybox = RenderSettings.skybox;
                RenderSettings.skybox = _skyboxInstance;
            }
        }

        private void CleanupSkybox()
        {
            if (RenderSettings.skybox == _skyboxInstance) RenderSettings.skybox = _originalSkybox;

            if (_skyboxInstance != null)
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                if (Application.isPlaying) Destroy(_skyboxInstance);
                else DestroyImmediate(_skyboxInstance);
#endif
                _skyboxInstance = null;
            }
            _areIDsInitialized = false;
        }

        public void UpdateSky(Vector3 sunDirection, float rayleigh, float turbidity, float mieCoeff, float mieG, float exposure)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;
            if (RenderSettings.skybox != _skyboxInstance) RenderSettings.skybox = _skyboxInstance;

            Vector3 directionToSun = -sunDirection.normalized;

            _skyboxInstance.SetVector(SunPositionID, directionToSun);
            _skyboxInstance.SetFloat(RayleighID, rayleigh);
            _skyboxInstance.SetFloat(TurbidityID, turbidity);
            _skyboxInstance.SetFloat(MieCoefficientID, mieCoeff);
            _skyboxInstance.SetFloat(MieDirectionalGID, mieG);
            _skyboxInstance.SetFloat(ExposureID, exposure);
        }

        public void UpdateMoon(Vector3 moonDirection, Texture moonTex, Color moonColor, float moonSize)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            Vector3 dir = -moonDirection.normalized;
            _skyboxInstance.SetVector(MoonPositionID, dir);

            if (_currentMoonTexture != moonTex)
            {
                _skyboxInstance.SetTexture(MoonTexID, moonTex);
                _currentMoonTexture = moonTex;
            }

            _skyboxInstance.SetColor(MoonColorID, moonColor);
            _skyboxInstance.SetFloat(MoonSizeID, moonSize);
        }

        public void UpdateStars(float timeOfDay, Vector3 sunDirection,
            Texture starsTex, Texture mwTex,
            Vector3 alignment, float rotationSpeed,
            float starsInt, float mwInt,
            float twinkleScale, float twinkleSpeed, float twinkleStrength)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (_currentStarsTexture != starsTex)
            {
                _skyboxInstance.SetTexture(StarsTexID, starsTex);
                _currentStarsTexture = starsTex;
            }
            if (mwTex != null) _skyboxInstance.SetTexture(MilkyWayTexID, mwTex);

            Vector3 finalRotation = alignment;
            finalRotation.y += timeOfDay * 360f * rotationSpeed;

            _skyboxInstance.SetVector(StarfieldRotationID, finalRotation);

            // Calculate Fade (Day/Night)
            float sunHeight = Mathf.InverseLerp(-0.1f, 0.15f, -sunDirection.y);
            float starsAlpha = 1f - sunHeight;
            _skyboxInstance.SetFloat(StarsFadeID, starsAlpha);

            // Set Intensities
            _skyboxInstance.SetFloat(StarsIntensityID, starsInt);
            _skyboxInstance.SetFloat(MilkyWayIntensityID, mwInt);

            // Twinkle
            _skyboxInstance.SetFloat(TwinkleScaleID, twinkleScale);
            _skyboxInstance.SetInt(TwinkleDetailID, 3);
            _skyboxInstance.SetFloat(TwinkleSpeedID, twinkleSpeed);
            _skyboxInstance.SetFloat(TwinkleStrengthID, twinkleStrength);
        }

        public void UpdateClouds(Texture cloudTex, Texture weatherMapTex, Texture blueNoiseTex, float altitude, float scale, float coverage, float density, float detail, float wisp, Vector2 windSpeed, Color baseColor, Color shadowColor, float scatter)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (_currentCloudTexture != cloudTex)
            {
                _skyboxInstance.SetTexture(CloudTexID, cloudTex);
                _currentCloudTexture = cloudTex;
            }

            if (weatherMapTex != null) _skyboxInstance.SetTexture(WeatherMapTexID, weatherMapTex);
            if (blueNoiseTex != null) _skyboxInstance.SetTexture(BlueNoiseTexID, blueNoiseTex);

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (Application.isPlaying) { _currentCloudOffset += windSpeed * Time.deltaTime; }
#else
            _currentCloudOffset += windSpeed * Time.deltaTime;
#endif
            if (_currentCloudOffset.x > 100f) _currentCloudOffset.x -= 100f;
            if (_currentCloudOffset.y > 100f) _currentCloudOffset.y -= 100f;

            _skyboxInstance.SetVector(CloudWindID, _currentCloudOffset);
            _skyboxInstance.SetFloat(CloudAltitudeID, altitude);
            _skyboxInstance.SetFloat(CloudScaleID, scale);
            _skyboxInstance.SetFloat(CloudCoverageID, coverage);
            _skyboxInstance.SetFloat(CloudDensityID, density);
            _skyboxInstance.SetFloat(CloudDetailID, detail);
            _skyboxInstance.SetFloat(CloudWispID, wisp);
            _skyboxInstance.SetColor(CloudColorID, baseColor);
            _skyboxInstance.SetColor(CloudShadowColorID, shadowColor);
            _skyboxInstance.SetFloat(CloudScatterID, scatter);
        }

        /// <summary>
        /// Updates the procedural skybox to seamlessly blend with the Unity global fog.
        /// </summary>
        public void UpdateSkyboxFog(bool isEnabled, Color fogColor, float blendAmount)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (isEnabled)
            {
                _skyboxInstance.SetColor(FogColorID, fogColor);
                _skyboxInstance.SetFloat(FogBlendID, blendAmount);
            }
            else
            {
                _skyboxInstance.SetFloat(FogBlendID, 0f);
            }
        }
    }
}
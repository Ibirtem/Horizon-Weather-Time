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
        [Header("World Space Clouds (Experimental)")]
        [Tooltip("Optional in-world volumetric cloud container.")]
        public HorizonCloudVolume worldCloudVolume;

        [Header("Configuration")]
        [Tooltip("The base material asset for the skybox.")]
        public Material skyboxMaterial;
        [SerializeField] private Shader skyboxShader;

        [Header("Internal Data")]
        [Tooltip("Pre-baked Optical Depth LUT for atmospheric scattering.")]
        public Texture2D transmittanceLUT;

        private int TransmittanceLUT_ID;

        [Tooltip("Pre-baked 3D noise for star twinkle.")]
        public Texture3D twinkleNoiseTex;
        private int TwinkleNoiseTexID;

        private Material _skyboxInstance;
        private Material _originalSkybox;
        private Texture _currentStarsTexture;
        private Texture _currentMoonTexture;
        private Texture3D _currentCloudTexture;
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
        private int TwinkleSharpnessID;
        private int TwinkleSpeedID;
        private int TwinkleStrengthID;

        // Moon
        private int MoonTexID;
        private int MoonColorID;
        private int MoonSizeID;
        private int MoonPositionID;

        // Clouds
        private int CloudNoise3DID, CloudScaleID, CloudCoverageID, CloudWindID, CloudColorID, CloudShadowColorID;
        private int CloudAltitudeID, CloudDensityID, CloudDetailID, CloudWispID, CloudScatterID;
        private int WeatherMapTexID, BlueNoiseTexID;

        private int CurlNoiseTexID;
        private Texture _currentCurlTexture;

        private int CirrusTexID, CirrusCoverageID, CirrusOpacityID, CirrusScaleID, CirrusWindID, CirrusTintID;
        private Texture _currentCirrusTexture;
        private Vector2 _currentCirrusOffset;

        private int CloudTimeID;

        // Fog
        private int FogColorID;
        private int FogBlendID;

        // Night Sky (Airglow)
        private int AirglowIntensityID;
        private int AirglowColorID;
        private int AirglowHeightID;

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
            TwinkleSharpnessID = VRCShader.PropertyToID("_TwinkleSharpness");
            TwinkleSpeedID = VRCShader.PropertyToID("_TwinkleSpeed");
            TwinkleStrengthID = VRCShader.PropertyToID("_TwinkleStrength");

            // Moon
            MoonTexID = VRCShader.PropertyToID("_MoonTex");
            MoonColorID = VRCShader.PropertyToID("_MoonColor");
            MoonSizeID = VRCShader.PropertyToID("_MoonSize");
            MoonPositionID = VRCShader.PropertyToID("_MoonPosition");

            // Clouds
            CloudNoise3DID = VRCShader.PropertyToID("_CloudNoise3D");
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

            CloudTimeID = VRCShader.PropertyToID("_CloudTime");

            CirrusTexID = VRCShader.PropertyToID("_CirrusTex");
            CirrusCoverageID = VRCShader.PropertyToID("_CirrusCoverage");
            CirrusOpacityID = VRCShader.PropertyToID("_CirrusOpacity");
            CirrusScaleID = VRCShader.PropertyToID("_CirrusScale");
            CirrusWindID = VRCShader.PropertyToID("_CirrusWind");
            CirrusTintID = VRCShader.PropertyToID("_CirrusTint");

            WeatherMapTexID = VRCShader.PropertyToID("_WeatherMapTex");
            BlueNoiseTexID = VRCShader.PropertyToID("_BlueNoiseTex");
            CurlNoiseTexID = VRCShader.PropertyToID("_CurlNoiseTex");

            FogColorID = VRCShader.PropertyToID("_HorizonFogColor");
            FogBlendID = VRCShader.PropertyToID("_HorizonFogBlend");

            TransmittanceLUT_ID = VRCShader.PropertyToID("_TransmittanceLUT");
            TwinkleNoiseTexID = VRCShader.PropertyToID("_TwinkleNoiseTex");

            // Night Sky (Airglow)
            AirglowIntensityID = VRCShader.PropertyToID("_AirglowIntensity");
            AirglowColorID = VRCShader.PropertyToID("_AirglowColor");
            AirglowHeightID = VRCShader.PropertyToID("_AirglowHeight");

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

        private double GetUtcSeconds()
        {
            System.DateTime now = System.DateTime.UtcNow;
            System.DateTime epoch = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            return (now - epoch).TotalSeconds;
        }

        private float _currentExposure = 15.0f;
        private float _currentRayleigh = 1.0f;
        private float _currentTurbidity = 2.5f;
        private float _currentMieCoeff = 0.005f;
        private float _currentMieG = 0.76f;
        private Color _currentFogColor = Color.gray;
        private float _currentFogBlend = 0f;

        public void UpdateSky(Vector3 sunDirection, float rayleigh, float turbidity, float mieCoeff, float mieG, float exposure)
        {
            _currentExposure = exposure;
            _currentRayleigh = rayleigh;
            _currentTurbidity = turbidity;
            _currentMieCoeff = mieCoeff;
            _currentMieG = mieG;
            
            EnsureInitialized();
            if (_skyboxInstance == null) return;
            if (RenderSettings.skybox != _skyboxInstance) RenderSettings.skybox = _skyboxInstance;

            if (transmittanceLUT != null)
            {
                _skyboxInstance.SetTexture(TransmittanceLUT_ID, transmittanceLUT);
            }

            Vector3 directionToSun = -sunDirection.normalized;

            _skyboxInstance.SetVector(SunPositionID, directionToSun);
            _skyboxInstance.SetFloat(RayleighID, rayleigh);
            _skyboxInstance.SetFloat(TurbidityID, turbidity);
            _skyboxInstance.SetFloat(MieCoefficientID, mieCoeff);
            _skyboxInstance.SetFloat(MieDirectionalGID, mieG);
            _skyboxInstance.SetFloat(ExposureID, exposure);

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            SyncAssetTextures();
#endif
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

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            SyncAssetTextures();
#endif
        }

        /// <summary>
        /// Updates the deep space elements.
        /// Uses the precalculated celestial Euler rotation to position the stars and Milky Way,
        /// and fades them based on the physical sun altitude to simulate daylight scattering.
        /// </summary>
        public void UpdateStars(Vector3 starfieldRotation, Vector3 sunDirection,
            Cubemap starsCube, Cubemap mwCube,
            float starsInt, float mwInt,
            float twinkleScale, float twinkleSharpness, float twinkleSpeed, float twinkleStrength)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (starsCube != null) _skyboxInstance.SetTexture("_StarsCube", starsCube);
            if (mwCube != null) _skyboxInstance.SetTexture("_MilkyWayCube", mwCube);
            if (twinkleNoiseTex != null) _skyboxInstance.SetTexture(TwinkleNoiseTexID, twinkleNoiseTex);

            _skyboxInstance.SetVector(StarfieldRotationID, starfieldRotation);

            // Calculate Fade (Day/Night) based on physical sun height
            float sunHeight = Mathf.InverseLerp(-0.13f, -0.02f, -sunDirection.y);
            float starsAlpha = 1f - sunHeight;
            _skyboxInstance.SetFloat(StarsFadeID, starsAlpha);

            // Set Intensities
            _skyboxInstance.SetFloat(StarsIntensityID, starsInt);
            _skyboxInstance.SetFloat(MilkyWayIntensityID, mwInt);

            // Twinkle
            _skyboxInstance.SetFloat(TwinkleScaleID, twinkleScale);
            _skyboxInstance.SetFloat(TwinkleSharpnessID, twinkleSharpness);
            _skyboxInstance.SetFloat(TwinkleSpeedID, twinkleSpeed);
            _skyboxInstance.SetFloat(TwinkleStrengthID, twinkleStrength);

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            SyncAssetTextures();
#endif
        }

        /**
         * Updates skybox cloud properties and forwards complete atmosphere/fog context to HorizonCloudVolume.
         */
        public void UpdateClouds(
            Texture3D cloudTex, Texture weatherMapTex, Texture blueNoiseTex, Texture2D curlNoiseTex,
            float altitude, float thickness, float scale, float coverage, float density,
            float detail, float wisp, float curlStrength, Vector2 windSpeed,
            Color baseColor, Color shadowColor, float scatter,
            float silverLining, float multiScattering, float ambientIntensity)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (_currentCloudTexture != cloudTex)
            {
                _skyboxInstance.SetTexture(CloudNoise3DID, cloudTex);
                _currentCloudTexture = cloudTex;
            }

            if (_currentCurlTexture != curlNoiseTex)
            {
                if (curlNoiseTex != null)
                    _skyboxInstance.SetTexture(CurlNoiseTexID, curlNoiseTex);
                _currentCurlTexture = curlNoiseTex;
            }

            if (weatherMapTex != null) _skyboxInstance.SetTexture(WeatherMapTexID, weatherMapTex);
            if (blueNoiseTex != null) _skyboxInstance.SetTexture(BlueNoiseTexID, blueNoiseTex);

            double utcSec = GetUtcSeconds();
            double rawX = (utcSec * (double)windSpeed.x) % 100.0;
            if (rawX < 0.0) rawX += 100.0;
            double rawY = (utcSec * (double)windSpeed.y) % 100.0;
            if (rawY < 0.0) rawY += 100.0;

            _currentCloudOffset.x = (float)rawX;
            _currentCloudOffset.y = (float)rawY;

            float cloudTime = (float)(utcSec % 100000.0);
            _skyboxInstance.SetFloat(CloudTimeID, cloudTime);

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

            if (worldCloudVolume != null)
            {
                Vector3 sunDirection = _skyboxInstance.GetVector(SunPositionID);
                Vector3 moonDirection = _skyboxInstance.GetVector(MoonPositionID);
                Color moonColor = _skyboxInstance.GetColor(MoonColorID);

                worldCloudVolume.UpdateClouds(
                    cloudTex, weatherMapTex, blueNoiseTex, curlNoiseTex,
                    altitude, thickness, scale, coverage, density, detail, wisp, curlStrength,
                    _currentCloudOffset, cloudTime,
                    baseColor, shadowColor, scatter, silverLining, multiScattering, ambientIntensity,
                    sunDirection, moonDirection, moonColor,
                    _currentRayleigh, _currentTurbidity, _currentMieCoeff, _currentMieG,
                    _currentExposure, _currentFogColor, _currentFogBlend, transmittanceLUT);
            }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            SyncAssetTextures();
#endif
        }

        private Color ComputeAtmosphereTintForClouds(Vector3 sunDirection)
        {
            Color ambientSky = RenderSettings.ambientSkyColor;
            float sunHeight = sunDirection.y;

            // 1. Затемнение неба (в точности как в оригинальном скайбоксе)
            float nightDarkening = Mathf.Clamp01(sunHeight * 1.0f + 1.0f);
            nightDarkening = nightDarkening * nightDarkening;

            // 2. Фактор заката (плавно появляется у горизонта и исчезает при уходе солнца)
            float aboveFactor = Mathf.Clamp01(sunHeight / 0.2f);
            float belowFactor = Mathf.Clamp01(-sunHeight / 0.1f);
            float sunsetFactor = (1.0f - aboveFactor) * (1.0f - belowFactor);
            sunsetFactor = Mathf.Pow(sunsetFactor, 1.5f);

            // 3. Закатный цвет берем от базового эмбиента, а не хардкодим яркость 1.0
            Color warmTint = new Color(1.0f, 0.6f, 0.3f);
            Color sunsetAmbient = ambientSky * warmTint * 1.5f;

            // 4. Смешиваем и применяем ночное затемнение
            Color tint = Color.Lerp(ambientSky, sunsetAmbient, sunsetFactor);
            tint *= nightDarkening;
            
            tint.a = 1.0f;
            return tint;
        }

        private Color ComputeSkyTint(float sunHeight)
        {
            Color sky = RenderSettings.ambientSkyColor;
            float nightDarkening = Mathf.Clamp01(sunHeight * 1.0f + 1.0f);
            nightDarkening *= nightDarkening;
            return sky * nightDarkening;
        }

        private Color ComputeEquatorTint(float sunHeight)
        {
            Color eq = RenderSettings.ambientEquatorColor;
            
            // Окно заката (солнце от +0.3 до -0.1)
            float sunsetFactor = Mathf.Clamp01(1.0f - sunHeight * 3.0f);
            sunsetFactor = Mathf.Pow(sunsetFactor, 1.5f);
            
            // Закатный оранжевый в LDR (не пересвеченный)
            Color sunsetEq = new Color(0.8f, 0.25f, 0.05f);
            eq = Color.Lerp(eq, sunsetEq, sunsetFactor);

            float nightDarkening = Mathf.Clamp01(sunHeight * 1.0f + 1.0f);
            nightDarkening *= nightDarkening;
            return eq * nightDarkening;
        }

        public void UpdateCirrus(Texture cirrusTex, float coverage, float opacity, float scale, Vector2 windSpeed, Color tint)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            if (_currentCirrusTexture != cirrusTex)
            {
                _skyboxInstance.SetTexture(CirrusTexID, cirrusTex);
                _currentCirrusTexture = cirrusTex;
            }

            double utcSec = GetUtcSeconds();
            double rawCirrusX = (utcSec * (double)windSpeed.x) % 100.0;
            if (rawCirrusX < 0.0) rawCirrusX += 100.0;
            double rawCirrusY = (utcSec * (double)windSpeed.y) % 100.0;
            if (rawCirrusY < 0.0) rawCirrusY += 100.0;

            _currentCirrusOffset.x = (float)rawCirrusX;
            _currentCirrusOffset.y = (float)rawCirrusY;

            _skyboxInstance.SetVector(CirrusWindID, _currentCirrusOffset);
            _skyboxInstance.SetFloat(CirrusCoverageID, coverage);
            _skyboxInstance.SetFloat(CirrusOpacityID, opacity);
            _skyboxInstance.SetFloat(CirrusScaleID, scale);
            _skyboxInstance.SetColor(CirrusTintID, tint);

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            SyncAssetTextures();
#endif
        }

        /**
         * Updates skybox horizon fog parameters and caches them for world-space clouds.
         */
        public void UpdateSkyboxFog(bool isEnabled, Color fogColor, float blendAmount)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            _currentFogColor = fogColor;
            _currentFogBlend = isEnabled ? blendAmount : 0f;

            _skyboxInstance.SetColor(FogColorID, fogColor);
            _skyboxInstance.SetFloat(FogBlendID, _currentFogBlend);
        }

        /// <summary>
        /// Updates night sky atmospheric emission (airglow).
        /// All colors should be in linear space.
        /// </summary>
        public void UpdateNightSky(float airglowIntensity, Color airglowColor, float airglowHeightKm)
        {
            EnsureInitialized();
            if (_skyboxInstance == null) return;

            _skyboxInstance.SetFloat(AirglowIntensityID, airglowIntensity);
            _skyboxInstance.SetColor(AirglowColorID, airglowColor);
            _skyboxInstance.SetFloat(AirglowHeightID, airglowHeightKm);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void SyncTextureToAsset(int propertyID, Texture tex)
        {
            if (!Application.isPlaying && skyboxMaterial != null && tex != null)
            {
                if (skyboxMaterial.GetTexture(propertyID) != tex)
                {
                    skyboxMaterial.SetTexture(propertyID, tex);
                    UnityEditor.EditorUtility.SetDirty(skyboxMaterial);
                }
            }
        }

        private void SyncTextureToAssetString(string propertyName, Texture tex)
        {
            if (!Application.isPlaying && skyboxMaterial != null && tex != null)
            {
                if (skyboxMaterial.GetTexture(propertyName) != tex)
                {
                    skyboxMaterial.SetTexture(propertyName, tex);
                    UnityEditor.EditorUtility.SetDirty(skyboxMaterial);
                }
            }
        }
#endif

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        /// <summary>
        /// Synchronizes textures from the virtual instance (_skyboxInstance)
        /// to the physical material asset (skyboxMaterial).
        /// </summary>
        private void SyncAssetTextures()
        {
            if (Application.isPlaying || skyboxMaterial == null || _skyboxInstance == null) return;

            bool isDirty = false;

            void SyncTex(int id)
            {
                Texture tex = _skyboxInstance.GetTexture(id);
                if (tex != null && skyboxMaterial.GetTexture(id) != tex)
                {
                    skyboxMaterial.SetTexture(id, tex);
                    isDirty = true;
                }
            }

            void SyncTexStr(string name)
            {
                Texture tex = _skyboxInstance.GetTexture(name);
                if (tex != null && skyboxMaterial.GetTexture(name) != tex)
                {
                    skyboxMaterial.SetTexture(name, tex);
                    isDirty = true;
                }
            }

            SyncTex(TransmittanceLUT_ID);
            SyncTex(MoonTexID);
            SyncTexStr("_StarsCube");
            SyncTexStr("_MilkyWayCube");
            SyncTex(TwinkleNoiseTexID);
            SyncTex(CloudNoise3DID);
            SyncTex(CurlNoiseTexID);
            SyncTex(WeatherMapTexID);
            SyncTex(BlueNoiseTexID);
            SyncTex(CirrusTexID);

            if (isDirty)
            {
                UnityEditor.EditorUtility.SetDirty(skyboxMaterial);
            }
        }
#endif

    }
}
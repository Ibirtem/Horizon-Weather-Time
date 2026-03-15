using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Runtime-accessible container for baked weather profile data.
    /// </summary>
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class BakedProfileData : UdonSharpBehaviour
#else
    public class BakedProfileData : MonoBehaviour
#endif
    {
        [HideInInspector] public string profileName = "";

        // =====================================================
        // LIGHTING
        // =====================================================
        [Header("Lighting")]
        public Color sunColorZenith = Color.white;
        public Color sunColorHorizon = new Color(1f, 0.7f, 0.4f);
        public float sunIntensity = 1.0f;
        public Color moonLightColor = new Color(0.8f, 0.9f, 1f);
        public float moonLightIntensity = 0.04f;

        public Color daySkyColor = new Color(0.4f, 0.5f, 0.6f);
        public Color dayEquatorColor = new Color(0.3f, 0.35f, 0.4f);
        public Color dayGroundColor = new Color(0.2f, 0.2f, 0.2f);
        public Color nightSkyColor = new Color(0.05f, 0.05f, 0.1f);
        public Color nightEquatorColor = new Color(0.02f, 0.02f, 0.05f);
        public Color nightGroundColor = new Color(0.01f, 0.01f, 0.02f);

        // =====================================================
        // SKY & ATMOSPHERE
        // =====================================================
        [Header("Sky")]
        public float rayleigh = 1.0f;
        public float turbidity = 5.0f;
        public float mieCoefficient = 0.005f;
        public float mieDirectionalG = 0.8f;
        public float exposure = 15.0f;

        // =====================================================
        // DEEP SPACE
        // =====================================================
        [Header("Stars")]
        public Cubemap starsCubemap;
        public Cubemap milkyWayCubemap;
        public Vector3 starfieldAlignment = Vector3.zero;
        public float starsRotationSpeed = 0.5f;
        public float starsIntensity = 0.04f;
        public float milkyWayIntensity = 0.005f;
        public float twinkleScale = 150f;
        public float twinkleSharpness = 4f;
        public float twinkleSpeed = 0.004f;
        public float twinkleStrength = 0.8f;

        // =====================================================
        // NIGHT SKY (AIRGLOW)
        // =====================================================
        [Header("Night Sky")]
        public float airglowIntensity = 0.0004f;
        public Color airglowColor = new Color(0.4f, 0.6f, 0.3f, 1f);
        public float airglowHeight = 90f;

        // =====================================================
        // MOON
        // =====================================================
        [Header("Moon")]
        public Texture moonTexture;
        public float moonSize = 0.012f;
        public Color moonTint = new Color(0.85f, 0.85f, 0.8f, 1f);

        // =====================================================
        // VOLUMETRIC CLOUDS
        // =====================================================
        [Header("Clouds")]
        public Texture3D cloudNoiseTexture;
        public Texture weatherMapTexture;
        public Texture blueNoiseTexture;
        public Texture2D curlNoiseTexture;
        public float cloudAltitude = 2.0f;
        public float cloudScale = 1.0f;
        public float cloudCoverage = 0.6f;
        public float cloudDensity = 1.4f;
        public float cloudDetailAmount = 0.4f;
        public float cloudWispiness = 0.1f;
        public Vector2 cloudWindSpeed = new Vector2(0.002f, 0.001f);
        public Color cloudBaseColor = new Color(0.95f, 0.95f, 0.95f);
        public Color cloudShadowColor = new Color(0.35f, 0.4f, 0.5f);
        public float cloudLightScattering = 1.5f;

        // =====================================================
        // CIRRUS CLOUDS
        // =====================================================
        [Header("Cirrus")]
        public Texture2D cirrusNoiseTexture;
        public float cirrusCoverage = 0.67f;
        public float cirrusOpacity = 0.7f;
        public float cirrusScale = 0.5f;
        public Vector2 cirrusWindSpeed = new Vector2(0.002f, 0.001f);
        public Color cirrusTint = Color.white;

        // =====================================================
        // FOG
        // =====================================================
        [Header("Fog")]
        public bool fogEnabled = true;
        public int fogMode = 2;
        public Color fogDayColor = new Color(0.7f, 0.8f, 1f);
        public Color fogNightColor = new Color(0.05f, 0.05f, 0.15f);
        public float fogDensity = 0.002f;
        public float fogStartDistance = 10f;
        public float fogEndDistance = 250f;
        public float fogSkyBlend = 1.0f;

        // =====================================================
        // WEATHER EFFECTS
        // =====================================================
        [Header("Effects")]
        public GameObject weatherEffectPrefab;
        public float spawnHeightOffset = 15f;
        public Vector3 volumeBounds = new Vector3(40f, 40f, 40f);
        public float particleSize = 0.05f;
        public float weatherDensity = 1.0f;

        // =====================================================
        // EDITOR-ONLY: Copy logic
        // =====================================================
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        /// <summary>
        /// Copies all data from a WeatherProfile's sub-profiles into this flat container.
        /// </summary>
        public void CopyFromProfile(WeatherProfile wp)
        {
            if (wp == null) return;
            profileName = wp.profileName;

            // ----- LIGHTING -----
            var lp = wp.lightingProfile;
            if (lp != null)
            {
                sunColorZenith = lp.sunColorZenith;
                sunColorHorizon = lp.sunColorHorizon;
                sunIntensity = lp.sunIntensity;
                moonLightColor = lp.moonColor;
                moonLightIntensity = lp.moonIntensity;

                daySkyColor = lp.daySkyColor;
                dayEquatorColor = lp.dayEquatorColor;
                dayGroundColor = lp.dayGroundColor;
                nightSkyColor = lp.nightSkyColor;
                nightEquatorColor = lp.nightEquatorColor;
                nightGroundColor = lp.nightGroundColor;
            }

            // ----- SKY -----
            var sp = wp.skyProfile;
            if (sp != null)
            {
                rayleigh = sp.rayleigh;
                turbidity = sp.turbidity;
                mieCoefficient = sp.mieCoefficient;
                mieDirectionalG = sp.mieDirectionalG;
                exposure = sp.exposure;

                starsCubemap = sp.starsCubemap;
                milkyWayCubemap = sp.milkyWayCubemap;
                starfieldAlignment = sp.starfieldAlignment;
                starsRotationSpeed = sp.starsRotationSpeed;
                starsIntensity = sp.starsIntensity;
                milkyWayIntensity = sp.milkyWayIntensity;
                twinkleScale = sp.twinkleScale;
                twinkleSharpness = sp.twinkleSharpness;
                twinkleSpeed = sp.twinkleSpeed;
                twinkleStrength = sp.twinkleStrength;

                airglowIntensity = sp.airglowIntensity;
                airglowColor = sp.airglowColor;
                airglowHeight = sp.airglowHeight;
            }

            // ----- MOON -----
            var mp = wp.moonProfile;
            if (mp != null)
            {
                moonTexture = mp.moonTexture;
                moonSize = mp.moonSize;
                moonTint = mp.moonColor;
            }

            // ----- CLOUDS -----
            var cp = wp.cloudProfile;
            if (cp != null)
            {
                cloudNoiseTexture = cp.cloudNoiseTexture;
                weatherMapTexture = cp.weatherMapTexture;
                blueNoiseTexture = cp.blueNoiseTexture;
                curlNoiseTexture = cp.curlNoiseTexture;
                cloudAltitude = cp.altitude;
                cloudScale = cp.scale;
                cloudCoverage = cp.enabled ? cp.coverage : 0f;
                cloudDensity = cp.density;
                cloudDetailAmount = cp.detailAmount;
                cloudWispiness = cp.wispiness;
                cloudWindSpeed = cp.windSpeed;
                cloudBaseColor = cp.baseColor;
                cloudShadowColor = cp.shadowColor;
                cloudLightScattering = cp.lightScattering;

                cirrusNoiseTexture = cp.cirrusNoiseTexture;
                cirrusCoverage = cp.cirrusCoverage;
                cirrusOpacity = cp.cirrusOpacity;
                cirrusScale = cp.cirrusScale;
                cirrusWindSpeed = cp.cirrusWindSpeed;
                cirrusTint = cp.cirrusTint;
            }

            // ----- FOG -----
            var fp = wp.fogProfile;
            if (fp != null)
            {
                fogEnabled = fp.enabled;
                fogMode = (int)fp.fogMode;
                fogDayColor = fp.dayColor;
                fogNightColor = fp.nightColor;
                fogDensity = fp.density;
                fogStartDistance = fp.startDistance;
                fogEndDistance = fp.endDistance;
                fogSkyBlend = fp.skyboxBlendIntegrity;
            }
            else
            {
                fogEnabled = false;
            }

            // ----- EFFECTS -----
            var ep = wp.effectsProfile;
            if (ep != null)
            {
                weatherEffectPrefab = ep.weatherEffectPrefab;
                spawnHeightOffset = ep.spawnHeightOffset;
                volumeBounds = ep.volumeBounds;
                particleSize = ep.particleSize;
                weatherDensity = ep.weatherDensity;
            }
            else
            {
                weatherEffectPrefab = null;
            }
        }
#endif
    }
}
Shader "Horizon/Procedural Skybox"
{
    Properties
    {
        [Header(Deep Space)]
        [NoScaleOffset] _StarsCube ("Stars Cubemap", Cube) = "black" {}
        [NoScaleOffset] _MilkyWayCube ("Milky Way Cubemap", Cube) = "black" {}
        
        [HideInInspector] _StarfieldRotation ("Rotation (Euler)", Vector) = (0,0,0,0)
        [HideInInspector] _StarsIntensity ("Stars Intensity", Float) = 0.01
        [HideInInspector] _MilkyWayIntensity ("MW Intensity", Float) = 0.002
        [HideInInspector] _StarsFade ("Stars Fade", Range(0.0, 1.0)) = 1.0

        [Header(Stars Twinkle)]
        [HideInInspector] _TwinkleScale ("Twinkle Scale", Float) = 150.0
        [HideInInspector] _TwinkleSharpness ("Twinkle Sharpness", Float) = 5.0
        [HideInInspector] _TwinkleSpeed ("Twinkle Speed", Float) = 0.004
        [HideInInspector] _TwinkleStrength ("Twinkle Strength", Range(0.0, 2.0)) = 0.8
        [NoScaleOffset] _TwinkleNoiseTex ("Twinkle Noise 3D", 3D) = "gray" {}

        [Header(Airglow)]
        [HideInInspector] _AirglowIntensity ("Airglow Intensity", Float) = 0.0004
        [HideInInspector] _AirglowColor ("Airglow Base Color", Color) = (0.4, 0.6, 0.3, 1.0)
        [HideInInspector] _AirglowHeight ("Airglow Emission Altitude (km)", Float) = 90.0

        [Header(Moon)]
        [NoScaleOffset] _MoonTex ("Moon Texture", 2D) = "white" {}
        _MoonColor ("Moon Color", Color) = (0.85, 0.85, 0.8, 1)
        _MoonSize ("Moon Size", Range(0.005, 0.05)) = 0.012
        [HideInInspector] _MoonPosition ("Moon Direction", Vector) = (0.0, -0.5, 0.0, 0.0)

        [Header(Volumetric Clouds)]
        [NoScaleOffset] _CloudNoise3D ("Cloud Noise 3D (RGBA)", 3D) = "black" {}
        [NoScaleOffset] _BlueNoiseTex ("Blue Noise (R)", 2D) = "black" {}
        [NoScaleOffset] _WeatherMapTex ("Weather Map (RGBA)", 2D) = "black" {}
        [NoScaleOffset] _CurlNoiseTex ("Curl Noise (RG)", 2D) = "gray" {}

        [HideInInspector] _CloudTime ("Cloud Time (UTC)", Float) = 0.0
        
        _CloudColor ("Cloud Lit Color", Color) = (0.9, 0.9, 0.9, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.3, 0.3, 0.35, 1)
        _CloudAltitude ("Altitude Base (km)", Float) = 3.0
        _CloudScale ("Noise Scale", Float) = 1.0
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.5
        _CloudDensity ("Density Multiplier", Float) = 1.0
        _CloudDetail ("Erosion Amount", Range(0, 1)) = 0.5
        _CloudWisp ("Wispiness", Range(0, 1)) = 0.3
        _CloudScatter ("Light Absorption", Float) = 0.5
        [HideInInspector] _CloudWind ("Cloud Offset", Vector) = (0,0,0,0)

        [Header(Cirrus Clouds)]
        [NoScaleOffset] _CirrusTex ("Cirrus Texture", 2D) = "black" {}
        [HideInInspector] _CirrusCoverage ("Cirrus Coverage", Range(0, 1)) = 0.5
        [HideInInspector] _CirrusOpacity ("Cirrus Opacity", Range(0, 1)) = 0.8
        [HideInInspector] _CirrusScale ("Cirrus Scale", Float) = 1.0
        [HideInInspector] _CirrusWind ("Cirrus Wind Offset", Vector) = (0,0,0,0)
        [HideInInspector] _CirrusTint ("Cirrus Tint", Color) = (1,1,1,1)

        [Header(Horizon Fog)]
        [HideInInspector] _HorizonFogColor ("Fog Color", Color) = (0.5, 0.5, 0.5, 1)
        [HideInInspector] _HorizonFogBlend ("Fog Blend", Range(0.0, 1.0)) = 0.0

        [Header(Atmosphere)]
        [HideInInspector] _Turbidity ("Turbidity", Range(1.0, 10.0)) = 2.5
        [HideInInspector] _Rayleigh ("Rayleigh", Range(0.0, 5.0)) = 1.0
        [HideInInspector] _MieCoefficient ("Mie Coefficient", Range(0.0, 0.1)) = 0.005
        [HideInInspector] _MieDirectionalG ("Mie Directional G", Range(0.0, 1.0)) = 0.76
        [HideInInspector] _Exposure ("Exposure", Float) = 15.0
        [HideInInspector] _SunPosition ("Sun Direction", Vector) = (0.0, 0.5, 0.0, 0.0)

        [Header(Optimization)]
        [NoScaleOffset] _TransmittanceLUT ("Transmittance LUT", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #pragma multi_compile_instancing

            // =====================================================================
            //  SHADER PROPERTIES
            // =====================================================================

            // Deep Space
            samplerCUBE _StarsCube;
            samplerCUBE _MilkyWayCube;
            float3    _StarfieldRotation;
            float     _StarsIntensity;
            float     _MilkyWayIntensity;
            float     _StarsFade;

            // Stars Twinkle
            float _TwinkleScale;
            float _TwinkleSharpness;
            float _TwinkleSpeed;
            float _TwinkleStrength;
            sampler3D _TwinkleNoiseTex;

            // Airglow
            float  _AirglowIntensity;
            float4 _AirglowColor;
            float  _AirglowHeight;

            // Moon
            sampler2D _MoonTex;
            float4    _MoonColor;
            float     _MoonSize;
            float3    _MoonPosition;

            // Volumetric Clouds
            sampler3D _CloudNoise3D;
            sampler2D _WeatherMapTex;
            sampler2D _BlueNoiseTex;
            sampler2D _CurlNoiseTex;
            float4    _CloudColor;
            float4    _CloudShadowColor;
            float     _CloudAltitude;
            float     _CloudScale;
            float     _CloudCoverage;
            float     _CloudDensity;
            float     _CloudDetail;
            float     _CloudWisp;
            float     _CloudScatter;
            float2    _CloudWind;

            float     _CloudTime;

            // Cirrus Clouds
            sampler2D _CirrusTex;
            float     _CirrusCoverage;
            float     _CirrusOpacity;
            float     _CirrusScale;
            float2    _CirrusWind;
            float4    _CirrusTint;

            // Atmosphere
            float  _Turbidity;
            float  _Rayleigh;
            float  _MieCoefficient;
            float  _MieDirectionalG;
            float  _Exposure;
            float3 _SunPosition;

            // Horizon Fog
            float4 _HorizonFogColor;
            float  _HorizonFogBlend;

            sampler2D _TransmittanceLUT;

            // =====================================================================
            //  CONSTANTS
            // =====================================================================

            #define PI   3.14159265358979
            #define PI_2 1.57079632679

            // --- Planet Geometry ---
            #define PLANET_GROUND_RADIUS 6371000.0
            #define ATMOSPHERE_THICKNESS 100000.0
            #define ATMOSPHERE_RADIUS    (PLANET_GROUND_RADIUS + ATMOSPHERE_THICKNESS)

            // --- Rayleigh Scattering (sea level, λ: 680/550/440 nm) ---
            static const float3 RAYLEIGH_BETA = float3(5.8e-6, 13.5e-6, 33.1e-6);
            static const float  RAYLEIGH_SCALE_HEIGHT = 8500.0;

            // --- Mie Scattering (sea level) ---
            #define MIE_BETA_BASE 21e-6
            static const float MIE_SCALE_HEIGHT = 1200.0;

            // --- Ozone Absorption (Chappuis band, ~500-700 nm) ---
            static const float3 OZONE_BETA = float3(0.65e-6, 1.88e-6, 0.085e-6);
            #define OZONE_CENTER_ALT 25000.0
            #define OZONE_HALF_WIDTH 15000.0

            // --- Sun ---
            #define SUN_ANGULAR_COS 0.99998

            // --- Lunar Surface ---
            static const float3 LUNAR_DUST_TINT = float3(0.92, 0.79, 0.64);
            static const float3 EARTHSHINE_TINT = float3(0.88, 0.96, 1.0);

            // --- Atmosphere Integration ---
            #define ATMOSPHERE_STEPS 16

            // --- Cloud Shape ---
            #define CLOUD_NOISE_FREQ        0.00045
            #define CLOUD_WEATHER_FREQ      0.000038
            #define CLOUD_VERTICAL_SCALE    2.0
            #define CLOUD_CURL_UV_SCALE     0.0004
            #define CLOUD_CURL_STRENGTH     0.08

            // --- Cloud Raymarching ---
            #define CLOUD_STEPS             48
            #define CLOUD_EMPTY_STEP_MUL    3.0
            #define CLOUD_PLANET_RADIUS     600000.0
            #define CLOUD_MAX_DISTANCE      60000.0
            #define CLOUD_THICKNESS         2500.0
            #define CLOUD_TRANSMITTANCE_MIN 0.01

            // --- Cloud Lighting ---
            #define CLOUD_LIGHT_STEPS       4
            #define LIGHT_DENOM             21.0
            #define CLOUD_ABSORPTION_SCALE  0.002

            // =====================================================================
            //  Twilight & Night Transition
            // =====================================================================

            // How rapidly the sky darkens after sunset.
            #define TWILIGHT_STEEPNESS 1.0

            // When additional darkening begins relative to sun altitude.
            #define TWILIGHT_OFFSET 1.0

            // Minimum sky brightness floor (prevents absolute black).
            #define TWILIGHT_MIN_BRIGHTNESS 0.0005

            // Multiple scattering: range below horizon. 
            #define MS_TWILIGHT_RANGE 1.8

            // Multiple scattering: strength at sunset.
            #define MS_TWILIGHT_OFFSET 0.5

            // Multiple scattering: intensity multiplier.
            #define MS_INTENSITY 0.2

            // Star luminance gate: sky brightness threshold to fully hide stars.
            #define STAR_LUMINANCE_GATE 50.0

            // =====================================================================
            //  UTILITY: Remap
            // =====================================================================

            float Remap(float value, float inMin, float inMax, float outMin, float outMax)
            {
                return outMin + (value - inMin) / (inMax - inMin) * (outMax - outMin);
            }

            // =====================================================================
            //  UTILITY: Sphere Rotation (Euler angles, degrees)
            // =====================================================================

            float3 RotateSphere(float3 dir, float3 euler)
            {
                float3 rad = euler * (PI / 180.0);
                float3 c = cos(rad);
                float3 s = sin(rad);

                float3 rZ = float3(
                    dir.x * c.z - dir.y * s.z,
                    dir.x * s.z + dir.y * c.z,
                    dir.z
                );
                float3 rX = float3(
                    rZ.x,
                    rZ.y * c.x - rZ.z * s.x,
                    rZ.y * s.x + rZ.z * c.x
                );
                return float3(
                    rX.x * c.y - rX.z * s.y,
                    rX.y,
                    rX.x * s.y + rX.z * c.y
                );
            }

            // =====================================================================
            //  ATMOSPHERE: Phase Functions
            //  - Rayleigh: standard 3/(16π)(1+cos²θ)
            //  - Mie: Cornette-Shanks (improved HG, better normalization)
            // =====================================================================

            float RayleighPhase(float cosTheta)
            {
                return (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta);
            }

            float CornetteShanksPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                float num = 3.0 * (1.0 - g2) * (1.0 + cosTheta * cosTheta);
                float denom = (8.0 * PI) * (2.0 + g2) * pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5);
                return num / max(denom, 1e-10);
            }

            // =====================================================================
            //  ATMOSPHERE: Density Profile
            //  Returns float3(rayleigh, mie, ozone) density at given altitude.
            //  Rayleigh & Mie: exponential falloff with scale height.
            //  Ozone: triangular profile peaking at ~25 km.
            // =====================================================================

            float3 AtmosphereDensity(float altitude)
            {
                float rayleigh = exp(-altitude / RAYLEIGH_SCALE_HEIGHT);
                float mie      = exp(-altitude / MIE_SCALE_HEIGHT);
                float ozone    = max(0.0, 1.0 - abs(altitude - OZONE_CENTER_ALT) / OZONE_HALF_WIDTH);
                return float3(rayleigh, mie, ozone);
            }

            // =====================================================================
            //  ATMOSPHERE: Ray-Sphere Intersection
            //  Returns (near, far) distances.
            // =====================================================================

            float2 AtmosphereRayIntersect(float3 origin, float3 dir, float radius)
            {
                float b = dot(origin, dir);
                float c = dot(origin, origin) - radius * radius;
                float discriminant = b * b - c;
                if (discriminant < 0.0) return float2(-1.0, -1.0);
                float sqrtDisc = sqrt(discriminant);
                return float2(-b - sqrtDisc, -b + sqrtDisc);
            }

            // =====================================================================
            //  ATMOSPHERE: LUT Sampling
            // =====================================================================

            float3 SampleOpticalDepthLUT(float altitude, float cosTheta)
            {
                float v = sqrt(saturate(altitude / ATMOSPHERE_THICKNESS));

                float absCos = abs(cosTheta);
                float u = 0.5 + sign(cosTheta) * 0.5 * sqrt(absCos);

                return tex2Dlod(_TransmittanceLUT, float4(u, v, 0, 0)).rgb;
            }

            // =====================================================================
            //  ATMOSPHERE: Full Sky Computation
            //
            //  Physically-based single scattering with:
            //  - Rayleigh + Mie + Ozone
            //  - Ground intersection (below-horizon darkening)
            //  - Multiple scattering approximation (ambient fill)
            //  - Sun disk with limb darkening
            //  - Circumsolar aureole (corona + Mie forward glow)
            //
            //  Returns float4(skyColor.rgb, sunGlowMask)
            // =====================================================================

            float4 ComputeAtmosphere(float3 viewDir, float3 sunDir,
                                     float turbidity, float rayleighMul, float mieMul, float mieG)
            {
                float3 origin = float3(0, PLANET_GROUND_RADIUS, 0);

                // Ray boundaries
                float2 atmosHit  = AtmosphereRayIntersect(origin, viewDir, ATMOSPHERE_RADIUS);
                if (atmosHit.y < 0.0) return 0.0;

                float2 groundHit = AtmosphereRayIntersect(origin, viewDir, PLANET_GROUND_RADIUS);
                bool hitsGround  = (groundHit.x > 0.0);

                float rayStart  = max(0.0, atmosHit.x);
                float rayEnd    = hitsGround ? groundHit.x : atmosHit.y;
                float rayLength = rayEnd - rayStart;
                if (rayLength <= 0.0) return 0.0;

                // Scattering coefficients
                float3 betaR = RAYLEIGH_BETA * rayleighMul;
                float  mieBeta = MIE_BETA_BASE * turbidity * mieMul;
                float3 betaM = float3(mieBeta, mieBeta, mieBeta);
                float3 betaO = OZONE_BETA;

                // Phase values for this view-sun angle
                float cosTheta = dot(viewDir, sunDir);
                float phaseR   = RayleighPhase(cosTheta);
                float phaseM   = CornetteShanksPhase(cosTheta, mieG);

                // View-ray integration
                float stepSize = rayLength / float(ATMOSPHERE_STEPS);
                half3 totalInscatter    = 0.0;
                float3 totalTransmittance = 1.0;

                [loop]
                for (int i = 0; i < ATMOSPHERE_STEPS; i++)
                {
                    float t = rayStart + (float(i) + 0.5) * stepSize;
                    float3 samplePos = origin + viewDir * t;
                    float altitude = max(0.0, length(samplePos) - PLANET_GROUND_RADIUS);

                    float3 localDensity = AtmosphereDensity(altitude);

                    // View-ray extinction for this step
                    float3 stepExtinction = (betaR * localDensity.x
                                           + betaM * localDensity.y
                                           + betaO * localDensity.z) * stepSize;
                    float3 stepTransmittance = exp(-stepExtinction);

                    float sunCosTheta = dot(normalize(samplePos), sunDir);
                    
                    float3 sunOpticalDepth = SampleOpticalDepthLUT(altitude, sunCosTheta);

                    float3 sunTransmittance = exp(-(betaR * sunOpticalDepth.x
                                                   + betaM * sunOpticalDepth.y
                                                   + betaO * sunOpticalDepth.z));

                    half3 inscatterContribution = 0.0; 
                    
                    float sunTransLum = dot(sunTransmittance, float3(0.2126, 0.7152, 0.0722));
                    
                    if (sunTransLum > 0.00001) 
                    {
                        float3 scatterR = betaR * localDensity.x * phaseR;
                        float3 scatterM = betaM * localDensity.y * phaseM;
                        inscatterContribution = sunTransmittance * (scatterR + scatterM) * stepSize;
                    }

                    // Multiple scattering approximation (vanishes at night)
                    float msStrength = saturate(sunDir.y * MS_TWILIGHT_RANGE + MS_TWILIGHT_OFFSET);
                    msStrength *= msStrength;
                    if (msStrength > 0.001)
                    {
                        float3 msApprox = (betaR * localDensity.x + betaM * localDensity.y * 0.25) * stepSize;
                        float sunH01 = saturate(sunDir.y * 2.5 + 0.5);
                        float3 msColor = lerp(float3(0.015, 0.018, 0.04), float3(0.05, 0.07, 0.13), sunH01);

                        float viewElev = saturate(dot(normalize(samplePos), viewDir));
                        float msHorizonMask = smoothstep(0.0, 0.15, abs(viewDir.y));

                        inscatterContribution += msApprox * msColor * MS_INTENSITY * msStrength * msHorizonMask;
                    }

                    totalInscatter += totalTransmittance * inscatterContribution;
                    totalTransmittance *= stepTransmittance;
                }

                float3 skyColor = totalInscatter;

                // Night floor: suppress residual atmospheric glow
                float nightDarkening = saturate(sunDir.y * TWILIGHT_STEEPNESS + TWILIGHT_OFFSET);
                skyColor *= max(nightDarkening * nightDarkening, TWILIGHT_MIN_BRIGHTNESS);

                if (hitsGround)
                {
                    float3 groundPoint  = origin + viewDir * rayEnd;
                    float3 groundNormal = normalize(groundPoint);
                    float  groundNdotL  = max(0.0, dot(groundNormal, sunDir));
                    skyColor += totalTransmittance * float3(0.1, 0.1, 0.1) * groundNdotL * 0.05;
                }

                // --- Sun Disk ---
                float sunDisk       = smoothstep(SUN_ANGULAR_COS - 0.000005, SUN_ANGULAR_COS, cosTheta);
                float sunEdge       = saturate((cosTheta - SUN_ANGULAR_COS) / (1.0 - SUN_ANGULAR_COS));
                float limbDarkening = pow(sunEdge, 0.25) * 0.55 + 0.45;
                float sunMask       = sunDisk * limbDarkening;
                float sunVisibility = smoothstep(-0.01, 0.02, sunDir.y);

                // --- Circumsolar Aureole ---
                float angularDist  = acos(clamp(cosTheta, -1.0, 1.0));
                float corona       = exp(-angularDist * angularDist * 8000.0) * 0.6;
                float aureole      = exp(-angularDist * 80.0) * 0.15;
                float horizonSpread = 1.0 + (1.0 - saturate(sunDir.y * 4.0)) * 1.5;
                float totalGlow    = (corona + aureole) * horizonSpread;

                float sunHeightFactor = saturate(sunDir.y * 3.0);
                float3 diskColor = lerp(float3(1.0, 0.75, 0.4),  float3(1.0, 0.98, 0.95), sunHeightFactor);
                float3 glowColor = lerp(float3(1.0, 0.45, 0.1),  float3(1.0, 0.9,  0.75), sunHeightFactor);

                skyColor += totalTransmittance * sunMask  * diskColor * 40.0 * sunVisibility;
                skyColor += totalTransmittance * totalGlow * glowColor * 6.0  * sunVisibility;

                return float4(skyColor, saturate(sunMask + totalGlow));
            }

            // =====================================================================
            //  CLOUDS: Ray-Sphere Intersection (simplified, centered at origin)
            // =====================================================================

            float2 CloudRaySphere(float3 rayOrigin, float3 rayDir, float sphereRadius)
            {
                float t = dot(-rayOrigin, rayDir);
                float3 p = rayOrigin + rayDir * t;
                float d2 = dot(p, p);
                if (d2 > sphereRadius * sphereRadius) return float2(-1, -1);
                float x = sqrt(sphereRadius * sphereRadius - d2);
                return float2(t - x, t + x);
            }

            // =====================================================================
            //  CLOUDS: Dual Henyey-Greenstein Phase (silver lining effect)
            // =====================================================================

            float DualHG(float costheta, float g1, float g2, float blend)
            {
                float p1 = (1.0 - g1*g1) / (4.0*PI * pow(1.0 + g1*g1 - 2.0*g1*costheta, 1.5));
                float p2 = (1.0 - g2*g2) / (4.0*PI * pow(1.0 + g2*g2 - 2.0*g2*costheta, 1.5));
                return lerp(p1, p2, blend);
            }

            // =====================================================================
            //  CLOUDS: Density Sampling
            // =====================================================================

            struct CloudWeather {
                float coverage;
                float type;
                float erosion;
                float density;
                float macro;
            };

            // =====================================================================
            //  CLOUD HEIGHT GRADIENT
            // =====================================================================

            float CloudHeightGradient(float h, float cloudType)
            {
                float stratus = smoothstep(0.0, 0.04, h) 
                            * (1.0 - smoothstep(0.08, 0.25, h));
                
                float cumulus = smoothstep(0.0, 0.08, h) 
                            * (1.0 - smoothstep(0.30, 0.70, h));
                
                float cb = smoothstep(0.0, 0.03, h) 
                        * (1.0 - smoothstep(0.70, 1.0, h));
                
                float a = lerp(stratus, cumulus, saturate(cloudType * 2.0));
                return lerp(a, cb, saturate(cloudType * 2.0 - 1.0));
            }

            // =====================================================================
            //  DENSITY HEIGHT REMAP
            // =====================================================================

            float DensityHeightRemap(float baseNoise, float h, float coverage)
            {
                return saturate(Remap(baseNoise, 1.0 - coverage, 1.0, 0.0, 1.0));
            }

            float SampleCloudDensityUnified(float3 p, float heightFraction, 
                float lod, CloudWeather weather, bool applyErosion)
            {
                if (weather.macro < 0.01) return 0.0;

                float h = heightFraction;
                float horizFreq = CLOUD_NOISE_FREQ * _CloudScale;

                float verticalBase = h * CLOUD_VERTICAL_SCALE; 
                float verticalOffset = sin(p.x * 0.00013) * cos(p.z * 0.00017) * 0.15;

                float verticalDrift = _CloudTime * 0.0003;

                float2 curlUV = p.xz * CLOUD_CURL_UV_SCALE + _CloudWind * 0.05;
                float2 curlOffset = tex2Dlod(_CurlNoiseTex, float4(curlUV, 0, 0)).rg 
                                * 2.0 - 1.0;

                float3 noiseUVW = float3(
                    p.x * horizFreq + _CloudWind.x + curlOffset.x * CLOUD_CURL_STRENGTH,
                    verticalBase + verticalOffset + verticalDrift,
                    p.z * horizFreq + _CloudWind.y + curlOffset.y * CLOUD_CURL_STRENGTH
                );

                float4 noise3D = tex3Dlod(_CloudNoise3D, float4(noiseUVW, lod));

                // === BASE SHAPE ===
                float overcastBlend = saturate((weather.macro - 0.6) * 4.0);
                float baseNoise = lerp(noise3D.r, noise3D.g * 0.6 + 0.2, overcastBlend);

                // === HEIGHT + COVERAGE ===
                float heightGrad = CloudHeightGradient(h, weather.type);
                float effectiveCoverage = weather.macro * heightGrad;

                if (effectiveCoverage < 0.01) return 0.0;

                float baseShape = DensityHeightRemap(baseNoise, h, effectiveCoverage);

                if (applyErosion && baseShape > 0.001)
                {
                    float erosionStrength = _CloudDetail * weather.erosion
                                        * (1.0 - overcastBlend * 0.7);
                    float detailNoise = dot(noise3D.gba, float3(0.5, 0.3, 0.2));

                    float topBlend = smoothstep(0.1, 0.4, h);
                    float detailForErosion = lerp(detailNoise, 1.0 - detailNoise, topBlend);

                    float edgeFactor = smoothstep(0.0, 0.3, baseShape) 
                                    * smoothstep(0.8, 0.3, baseShape);

                    float erosionHeightMask = smoothstep(0.0, 0.15, h);

                    float erosion = detailForErosion * erosionStrength 
                                * edgeFactor * erosionHeightMask;
                    baseShape = saturate(baseShape - erosion);

                    baseShape -= noise3D.a * _CloudWisp * 0.3 
                            * (1.0 - overcastBlend * 0.8) * edgeFactor * erosionHeightMask;
                    baseShape = max(0.0, baseShape);
                }

                return saturate(baseShape * weather.density) * _CloudDensity;
            }

            // =====================================================================
            //  VERTEX / FRAGMENT STRUCTURES
            // =====================================================================

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex       : SV_POSITION;
                float3 worldPos     : TEXCOORD0;
                float3 sunDirection : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex       = UnityObjectToClipPos(v.vertex);
                o.worldPos     = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sunDirection = normalize(_SunPosition);
                return o;
            }

            // =====================================================================
            //  FRAGMENT SHADER
            // =====================================================================

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                float3 direction = normalize(i.worldPos - _WorldSpaceCameraPos.xyz);
                float3 up = float3(0, 1, 0);

                // =============================================================
                //  1. ATMOSPHERE
                // =============================================================

                float4 atmosResult = ComputeAtmosphere(
                    direction, i.sunDirection,
                    _Turbidity, _Rayleigh, _MieCoefficient, _MieDirectionalG
                );
                float3 finalColor = atmosResult.rgb;

                float sunHeight = dot(i.sunDirection, up);

                // =============================================================
                //  2. DEEP SPACE (Stars + Milky Way)
                // =============================================================

                float skyLuminance = dot(finalColor, float3(0.2126, 0.7152, 0.0722));
                float starVisibility = _StarsFade * saturate(1.0 - skyLuminance * STAR_LUMINANCE_GATE);
                starVisibility *= smoothstep(-0.02, 0.15, direction.y);

                if (starVisibility > 0.001)
                {
                    float3 spaceDir = RotateSphere(direction, _StarfieldRotation);

                    float3 starCol = texCUBE(_StarsCube, spaceDir).rgb * _StarsIntensity;
                    float3 mwRaw   = texCUBE(_MilkyWayCube, spaceDir).rgb;
                    
                    float mwLum = dot(mwRaw, float3(0.2126, 0.7152, 0.0722));
                    float3 mwCol = lerp(mwRaw, float3(mwLum, mwLum, mwLum), 0.4) * _MilkyWayIntensity;

                    // #define TWINKLE_DEBUG 1

                    float3 twinkleCoord = spaceDir * (_TwinkleScale * 0.2);
                    twinkleCoord.z += frac(_Time.y * _TwinkleSpeed);
                    float twinkleNoise = tex3Dlod(_TwinkleNoiseTex, float4(twinkleCoord, 0)).r;

                    #if TWINKLE_DEBUG
                        finalColor = float3(twinkleNoise, twinkleNoise, twinkleNoise);
                        return half4(finalColor, 1.0);
                    #endif

                    float halfWidth = 0.5 / _TwinkleSharpness;
                    float twinkleMask = smoothstep(0.5 - halfWidth, 0.5 + halfWidth, twinkleNoise) * _TwinkleStrength;
                    float twinkleMultiplier = 1.0 + (twinkleMask - _TwinkleStrength * 0.5) * 2.0;

                    float3 spaceColor = starCol * twinkleMultiplier + mwCol;

                    float horizonPath = 1.0 / max(direction.y, 0.15);
                    float reddeningStrength = max(0.0, horizonPath - 1.0) * 0.12;
                    float3 starReddening = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT * reddeningStrength);
                    spaceColor *= starReddening;

                    finalColor += spaceColor * starVisibility;
                }

                // =============================================================
                //  2.5 AIRGLOW (Upper Atmosphere Chemiluminescence)
                // =============================================================
                if (_AirglowIntensity > 0.000001)
                {
                    float agCosZ = max(direction.y, 0.0);

                    // --- Van Rhijn enhancement ---
                    float agSinZSq = 1.0 - agCosZ * agCosZ;
                    float agShellRadius = PLANET_GROUND_RADIUS + _AirglowHeight * 1000.0;
                    float agShellRatio = PLANET_GROUND_RADIUS / agShellRadius;
                    float agDenom = 1.0 - agShellRatio * agShellRatio * agSinZSq;
                    float agVanRhijnRaw = 1.0 / sqrt(max(agDenom, 0.01));

                    float agVanRhijn = 1.0 + saturate(agVanRhijnRaw - 1.0) * 0.5;

                    // --- Atmospheric extinction below the emission layer ---
                    float agS2 = agCosZ * agCosZ;
                    float agS3 = agS2 * agCosZ;
                    float agAirmass = (1.002432 * agS2 + 0.148386 * agCosZ + 0.0096467)
                                    / (agS3 + 0.149864 * agS2 + 0.0102963 * agCosZ + 0.000303978);

                    float3 agZenithTau = RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT;
                    float3 agExtinction = exp(-agZenithTau * min(agAirmass, 40.0));

                    // --- Combine ---
                    float3 agEmission = _AirglowColor.rgb * _AirglowIntensity
                                    * agVanRhijn * agExtinction;

                    agEmission = min(agEmission, 0.002);
                    
                    agEmission *= smoothstep(-0.02, 0.02, direction.y);
                    agEmission *= smoothstep(0.05, -0.12, sunHeight);

                    finalColor += agEmission;
                }

                // =============================================================
                //  3. MOON
                // =============================================================

                float3 moonDir = normalize(_MoonPosition);

                float3 moonRight = normalize(cross(
                    abs(moonDir.y) > 0.999 ? float3(1,0,0) : float3(0,1,0),
                    moonDir
                ));
                float3 moonUp = cross(moonDir, moonRight);

                float2 moonPlanarUV = float2(
                    dot(direction, moonRight),
                    dot(direction, moonUp)
                ) / _MoonSize;

                float3 sunInMoonSpace = float3(
                    dot(i.sunDirection, moonRight),
                    dot(i.sunDirection, moonUp),
                    dot(i.sunDirection, moonDir)
                );
                float3 moonLightDir = normalize(sunInMoonSpace);

                // Sun behind viewer (full moon): moonLightDir.z ≈ -1 → angle ≈ 0
                // Sun same side    (new moon):   moonLightDir.z ≈ +1 → angle ≈ π
                float moonPhaseAngleCos = clamp(-moonLightDir.z, -1.0, 1.0);
                float moonPhaseAngle    = acos(moonPhaseAngleCos);

                float moonPhaseFraction = saturate(0.5 - 0.5 * dot(i.sunDirection, moonDir));
                float moonPhaseBrightness = moonPhaseFraction * moonPhaseFraction * moonPhaseFraction;

                if (dot(direction, moonDir) > 0.0)
                {
                    float r2 = dot(moonPlanarUV, moonPlanarUV);
                    if (r2 < 1.0)
                    {
                        float z = sqrt(1.0 - r2);
                        float3 sphereNormal = float3(moonPlanarUV.x, moonPlanarUV.y, z);

                        float2 sphUV = float2(
                            0.5 + atan2(sphereNormal.x, sphereNormal.z) / (2.0 * PI),
                            0.5 + asin(sphereNormal.y) / PI
                        );
                        float4 moonTex = tex2Dlod(_MoonTex, float4(sphUV, 0, 0));

                        // ---- Surface normal in moonDir-aligned space ----
                        float3 moonN = float3(sphereNormal.xy, -sphereNormal.z);

                        float cosI = dot(moonN, moonLightDir);
                        float cosR = z;

                        // ---- Lommel-Seeliger BRDF ----
                        float moonLS = 0.0;
                        if (cosI > 0.0)
                        {
                            moonLS = cosI / (cosI + cosR + 0.0001);
                            moonLS *= smoothstep(-0.02, 0.04, cosI);
                        }

                        // ---- Opposition surge ----
                        float oppositionSurge = 1.0 + 0.5
                            * exp(-moonPhaseAngle * moonPhaseAngle * 80.0);
                        moonLS *= oppositionSurge;

                        // ---- Earthshine ----
                        float phi = moonPhaseAngle;
                        float earthshineEm = max(0.0,
                            -0.0061 * phi*phi*phi
                        +  0.0289 * phi*phi
                        -  0.0105 * sin(phi));

                        float3 earthshineContrib = EARTHSHINE_TINT * earthshineEm;

                        // ---- Combine surface reflectance ----
                        float3 moonAlbedo = moonTex.rgb * _MoonColor.rgb * LUNAR_DUST_TINT;
                        float3 moonLit = moonAlbedo * (moonLS + earthshineContrib);

                        // ---- Atmospheric extinction ----
                        float moonAltAngle = dot(moonDir, up);
                        float moonExtPath  = 1.0 / max(moonAltAngle, 0.035);
                        float3 moonAtmosTint = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT
                                                * min(moonExtPath, 40.0));

                        // ---- Final brightness + horizon visibility ----
                        float moonBrightness = 0.08 * smoothstep(-0.05, 0.15, moonAltAngle);

                        float3 moonVisual = moonLit * moonBrightness * moonAtmosTint;
                        float  moonAlpha  = smoothstep(1.0, 0.92, r2) * moonTex.a * _MoonColor.a;

                        float3 moonComposite = max(finalColor, moonVisual);
                        finalColor = lerp(finalColor, moonComposite, moonAlpha);
                    }

                    // ---- Lunar halo / glare ----
                    float moonAngularDist = acos(clamp(dot(direction, moonDir), -1.0, 1.0));
                    float moonHalo = exp(-moonAngularDist * moonAngularDist * 3000.0) * 0.04
                                + exp(-moonAngularDist * 60.0) * 0.008;
                    moonHalo *= moonPhaseBrightness;

                    float moonHaloVisibility = smoothstep(-0.05, 0.1, dot(moonDir, up));
                    float3 moonHaloColor = _MoonColor.rgb * LUNAR_DUST_TINT
                                        * moonHalo * moonHaloVisibility * 0.01;
                    finalColor += moonHaloColor;
                }

                // =============================================================
                //  3.5 CIRRUS CLOUDS
                // =============================================================

                if (direction.y > 0.01 && _CirrusOpacity > 0.005 && _CirrusCoverage > 0.005)
                {
                    float projHeight = direction.y;
                    float2 cirrusBaseUV = direction.xz / max(projHeight, 0.05) * _CirrusScale * 0.12;

                    float2 uv1 = cirrusBaseUV + _CirrusWind;
                    float2 uv2 = cirrusBaseUV * 3.1 + _CirrusWind * 1.7 + direction.xz * 0.015;
                    float2 uv3 = cirrusBaseUV * 7.3 + _CirrusWind * 0.4 - direction.xz * 0.008;

                    float4 layer1 = tex2Dlod(_CirrusTex, float4(uv1, 0, 0));
                    float4 layer2 = tex2Dlod(_CirrusTex, float4(uv2, 0, 0));
                    float4 layer3 = tex2Dlod(_CirrusTex, float4(uv3, 0, 0));

                    float3 cirrusWorldPos = _WorldSpaceCameraPos + direction * (10000.0 / max(direction.y, 0.05));
                    float2 weatherUV = cirrusWorldPos.xz * 0.000025 * _CloudScale + (_CloudWind * 0.1);
                    float cumulusCoverage = tex2Dlod(_WeatherMapTex, float4(weatherUV, 0, 0)).r;
                    float cumulusMask = smoothstep(1.0 - _CloudCoverage - 0.1, 1.0 - _CloudCoverage + 0.2, cumulusCoverage);
                    float cirrusAllowed = 1.0 - cumulusMask * 0.8;

                    // --- Shape: patches × fibers × micro-detail ---
                    float maskShape = smoothstep(0.15, 0.55, layer1.r);
                    
                    float midFibers = layer2.g * 0.7 + 0.3;
                    
                    float microDetail = layer3.r * 0.4 + layer3.g * 0.3 + 0.3;

                    float rawShape = maskShape * midFibers * microDetail;
                    rawShape *= lerp(0.7, 1.0, layer1.b);
                    rawShape *= cirrusAllowed;

                    // --- Coverage ---
                    float coverageThresh = 1.0 - _CirrusCoverage;
                    float density = smoothstep(coverageThresh, coverageThresh + 0.4, rawShape);

                    // --- Path thickening ---
                    float pathLength = min(1.0 / max(direction.y, 0.1), 3.0);
                    density = saturate(density * lerp(1.0, pathLength, 0.25));

                    if (density > 0.001)
                    {
                        float2 sunShift = normalize(i.sunDirection.xz + 0.001) * 0.03;
                        float sunSideDensity = tex2Dlod(_CirrusTex, float4(uv1 + sunShift, 0, 0)).r;
                        float selfShadow = smoothstep(0.0, 0.25, sunSideDensity - layer1.r);

                        float cirrusSunVis = smoothstep(-0.05, 0.2, i.sunDirection.y);
                        cirrusSunVis *= smoothstep(-0.05, 0.25, i.sunDirection.y) * 0.8 + 0.2;

                        float moonVis = smoothstep(-0.1, 0.1, normalize(_MoonPosition).y);
                        float moonContrib = moonVis * moonPhaseBrightness * 0.015;
                        float lightStrength = max(cirrusSunVis, moonContrib);

                        float cosAngle = dot(direction, i.sunDirection);
                        float fwdScatter = pow(saturate(cosAngle * 0.5 + 0.5), 6.0) * 0.3 * cirrusSunVis;

                        float3 cirrusBrightColor = finalColor * (1.0 + 0.3 * lightStrength) + float3(0.02, 0.02, 0.02) * lightStrength;
                        cirrusBrightColor *= lerp(float3(1,1,1), _CirrusTint.rgb, 0.3);

                        float opacity = density * density;

                        cirrusBrightColor += finalColor * fwdScatter * opacity;
                        cirrusBrightColor *= lerp(1.0, 0.5, selfShadow * cirrusSunVis);

                        float extinctionPath = max(pathLength - 1.0, 0.0) * 0.06;
                        float3 extinction = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT * extinctionPath);
                        cirrusBrightColor *= extinction;

                        float horizonFade = smoothstep(0.01, 0.1, direction.y);
                        float cirrusAlpha = opacity * _CirrusOpacity * horizonFade;
                        cirrusAlpha = min(cirrusAlpha, 0.7);

                        finalColor = lerp(finalColor, cirrusBrightColor, cirrusAlpha);
                    }
                }

                // =============================================================
                //  4. VOLUMETRIC CLOUDS (Raymarched)
                //  Beer-Lambert extinction, multi-sample light march,
                //  Beer-Powder dark edge, dual HG phase.
                //  Full moon (sun opposite): dot ≈ -1 → phaseBrightness ≈ 1.0
                //  New moon (sun same side): dot ≈ +1 → phaseBrightness ≈ 0.0
                // =============================================================

                if (direction.y > 0.005)
                {
                    float cloudBottomRad = CLOUD_PLANET_RADIUS + (_CloudAltitude * 1000.0);
                    float cloudTopRad    = cloudBottomRad + CLOUD_THICKNESS;

                    float3 cloudPlanetCenter = float3(0, -CLOUD_PLANET_RADIUS, 0);
                    float3 camOrigin = _WorldSpaceCameraPos.xyz - cloudPlanetCenter;

                    float2 hitBottom = CloudRaySphere(camOrigin, direction, cloudBottomRad);
                    float2 hitTop    = CloudRaySphere(camOrigin, direction, cloudTopRad);

                    if (hitTop.y > 0)
                    {
                        float distToStart = max(0, hitBottom.y);
                        float distToEnd   = hitTop.y;
                        float maxDist = CLOUD_MAX_DISTANCE;

                        if (distToStart <= maxDist)
                        {
                            float rayLength = min(distToEnd - distToStart, maxDist);

                            float fineStep  = rayLength / float(CLOUD_STEPS);
                            float3 startPos = camOrigin + direction * distToStart;

                            bool anyCloud = false;
                            float preThresh = 1.0 - _CloudCoverage - 0.2;

                            float2 weatherDrift = float2(
                                sin(_CloudTime * 0.00008) * 0.04,
                                cos(_CloudTime * 0.00006) * 0.03
                            );

                            [unroll]
                            for (int pre = 0; pre < 5; pre++)
                            {
                                float3 prePos = startPos + direction * (rayLength * (float(pre) + 0.5) * 0.2);
                                float2 preUV = prePos.xz * 0.000025 * _CloudScale 
                                            + (_CloudWind * 0.1) + weatherDrift;
                                
                                if (tex2Dlod(_WeatherMapTex, float4(preUV, 0, 0)).r > preThresh)
                                {
                                    anyCloud = true;
                                    break;
                                }
                            }

                            if (anyCloud)
                            {
                                float2 ditherUV = fmod(i.vertex.xy, 64.0) / 64.0;
                                float dither = tex2Dlod(_BlueNoiseTex, float4(ditherUV, 0, 0)).r;
                                startPos += direction * fineStep * dither;

                                // --- Light source blending (sun ↔ moon) ---
                                float cloudAltMeters = _CloudAltitude * 1000.0;
                                float cloudSunsetAngle = -sqrt(2.0 * cloudAltMeters / CLOUD_PLANET_RADIUS);
                                float sunWeight  = smoothstep(cloudSunsetAngle, cloudSunsetAngle + 0.08, sunHeight);
                                float moonWeight = smoothstep(-0.1, 0.1, dot(moonDir, up));

                                float3 mainLightDir = lerp(moonDir, i.sunDirection, sunWeight);

                                float sunElevClamped = max(sunHeight, 0.005);
                                float atmosPath = min(1.0 / sunElevClamped, 40.0);
                                float3 sunAtmosExtinction = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT * atmosPath * 0.4);
                                float3 sunLightColor = sunAtmosExtinction * sunWeight;

                                float3 moonLightColor = _MoonColor.rgb * 0.008
                                                    * moonWeight * moonPhaseBrightness;
                                float3 activeLightColor = lerp(moonLightColor,
                                                            sunLightColor, sunWeight);

                                float3 atmosphereTint = saturate(finalColor);
                                float3 dayAmbient   = lerp(_CloudShadowColor.rgb * 0.5,
                                                        atmosphereTint, 0.4) * 0.3;
                                float3 nightAmbient = _CloudShadowColor.rgb
                                                    * lerp(0.0004, 0.0012, moonPhaseBrightness);
                                float3 activeAmbient = lerp(nightAmbient, dayAmbient, sunWeight);

                                float cloudCosTheta = dot(direction, mainLightDir);
                                float dayPhase   = DualHG(cloudCosTheta, 0.85, -0.3, 0.7);
                                float nightPhase = DualHG(cloudCosTheta, 0.3,  -0.1, 0.4);
                                float phaseVal   = lerp(nightPhase, dayPhase, sunWeight);

                                // --- Absorption coefficient ---
                                float absorptionCoeff = _CloudScatter * CLOUD_ABSORPTION_SCALE;

                                half3 accumColor = 0;
                                float  transmittance = 1.0;

                                float t = 0.0;
                                float coarseStep = fineStep * CLOUD_EMPTY_STEP_MUL;

                                [loop]
                                for (int j = 0; j < CLOUD_STEPS; j++)
                                {
                                    if (t >= rayLength || transmittance < CLOUD_TRANSMITTANCE_MIN) break;

                                    float3 pos = startPos + direction * t;
                                    float distAlongRay = distToStart + t;

                                    // --- Level 1: height bounds check ---
                                    float heightInfo = (length(pos) - cloudBottomRad)
                                                    / CLOUD_THICKNESS;

                                    if (heightInfo < -0.02 || heightInfo > 1.02)
                                    {
                                        t += coarseStep;
                                        continue;
                                    }
                                    heightInfo = saturate(heightInfo);

                                    // --- Level 2: weather-map pre-check ---
                                    float2 weatherUV = pos.xz * CLOUD_WEATHER_FREQ * _CloudScale
                                                    + (_CloudWind * 0.1) + weatherDrift;
                                    float4 wData = tex2Dlod(_WeatherMapTex,
                                                            float4(weatherUV, 0, 0));

                                    float macro = smoothstep(
                                        1.0 - _CloudCoverage - 0.15,
                                        1.0 - _CloudCoverage + 0.15,
                                        wData.r
                                    );

                                    if (macro < 0.01)
                                    {
                                        t += coarseStep;
                                        continue;
                                    }

                                    // --- Full density sample ---
                                    CloudWeather weather;
                                    weather.coverage = wData.r;
                                    weather.type     = saturate(wData.g * 1.3 - 0.1);
                                    weather.erosion  = wData.b;
                                    weather.density  = wData.a;
                                    weather.macro    = macro;

                                    float lod = saturate(distAlongRay / 60000.0) * 4.0;
                                    float dens = SampleCloudDensityUnified(pos, heightInfo, lod, weather, true);

                                    // --- Level 3: density zero ---
                                    if (dens < 0.001)
                                    {
                                        t += fineStep;
                                        continue;
                                    }

                                    // ---- Light march ----
                                    float lightOpticalDepth = 0.0;
                                    float totalLightDens = 0.0;

                                    [unroll]
                                    for (int k = 0; k < CLOUD_LIGHT_STEPS; k++)
                                    {
                                        float t_k = (float)k;
                                        float t0 = CLOUD_THICKNESS * (t_k * t_k)
                                                / LIGHT_DENOM;
                                        float t1 = CLOUD_THICKNESS
                                                * ((t_k + 1.0) * (t_k + 1.0))
                                                / LIGHT_DENOM;
                                        float lightDist = (t0 + t1) * 0.5;

                                        float3 lightSamplePos = pos
                                                            + mainLightDir * lightDist;
                                        float lightH = (length(lightSamplePos)
                                                    - cloudBottomRad) / CLOUD_THICKNESS;

                                        if (lightH >= 0.0 && lightH <= 1.0)
                                        {
                                            float ld = SampleCloudDensityUnified(lightSamplePos, lightH, 
                                                lod + 1.5, weather, false);
                                            totalLightDens += ld;
                                            lightOpticalDepth += ld * (t1 - t0)
                                                            * absorptionCoeff;
                                        }
                                    }

                                    // ---- Analytical tail ----
                                    float avgLightDens = totalLightDens * 0.25;
                                    float remainingThickness = CLOUD_THICKNESS
                                                            * (5.0 / LIGHT_DENOM);
                                    lightOpticalDepth += avgLightDens
                                                    * remainingThickness * 0.4
                                                    * absorptionCoeff;

                                    // ---- Beer-Lambert ----
                                    float beerTerm = exp(-lightOpticalDepth);

                                    // ---- Beer-Powder ----
                                    float powderTerm = 1.0
                                                    - exp(-lightOpticalDepth * 2.0);
                                    float powderWeight = lerp(0.8, 0.2,
                                        saturate(cloudCosTheta * 0.5 + 0.5));
                                    float beerPowder = beerTerm
                                        * lerp(1.0, powderTerm * 2.0, powderWeight);
                                    float nightBeer = lerp(beerPowder, 0.1, 0.5);
                                    beerPowder = lerp(nightBeer, beerPowder, sunWeight);

                                    // ---- Ambient from height ----
                                    float dayHeightGrad   = lerp(0.3, 1.0, heightInfo);
                                    float nightHeightGrad = lerp(0.7, 1.0, heightInfo);
                                    float heightGradient  = lerp(nightHeightGrad,
                                                                dayHeightGrad, sunWeight);

                                    // ---- Combine lighting ----
                                    half3 directLight  = activeLightColor * beerPowder
                                                        * phaseVal;
                                    half3 ambientLight = activeAmbient * heightGradient;
                                    half3 cloudPointColor = _CloudColor.rgb
                                                        * (directLight + ambientLight);

                                    // ---- View ray extinction ----
                                    float stepOpticalDepth = dens * fineStep
                                                        * absorptionCoeff;
                                    float stepTransmittance = exp(-stepOpticalDepth);
                                    float alphaStep = 1.0 - stepTransmittance;

                                    accumColor   += cloudPointColor * alphaStep
                                                * transmittance;
                                    transmittance *= stepTransmittance;

                                    t += fineStep;
                                }

                                // --- Post-processing ---
                                float distFade         = 1.0 - smoothstep(
                                    maxDist * 0.7, maxDist, distToStart);
                                float horizonAlphaFade = smoothstep(0.0, 0.12,
                                                                    direction.y);
                                float totalFade        = distFade * horizonAlphaFade;

                                accumColor   *= totalFade;
                                transmittance = lerp(1.0, transmittance, totalFade);

                                // Aerial perspective
                                float aerialFactor = smoothstep(
                                    maxDist * 0.2, maxDist * 0.9, distToStart) * 0.6;
                                float3 horizonAtmosColor = saturate(finalColor);
                                accumColor = lerp(accumColor,
                                    horizonAtmosColor * (1.0 - transmittance),
                                    aerialFactor);

                                finalColor = finalColor * transmittance + accumColor;
                            }
                        }
                    }
                }

                // =============================================================
                //  5. TONE MAPPING (ACES Filmic Approximation)
                // =============================================================

                finalColor *= _Exposure;
                finalColor = saturate(
                    (finalColor * (2.51 * finalColor + 0.03)) /
                    (finalColor * (2.43 * finalColor + 0.59) + 0.14)
                );

                // =============================================================
                //  6. HORIZON FOG (in LDR, post-tonemap)
                //  Fog color is specified as final display color.
                // =============================================================

                float fogHeightFactor = max(direction.y, 0.0);
                float horizonMask     = 1.0 - smoothstep(0.0, 0.25, fogHeightFactor);
                horizonMask = pow(horizonMask, 2.0);
                float finalFogMask = horizonMask * _HorizonFogBlend;

                float3 fogColor = lerp(_HorizonFogColor.rgb, finalColor, 0.5);
                finalColor = lerp(finalColor, fogColor, finalFogMask);

                return half4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Skybox/Procedural"
}
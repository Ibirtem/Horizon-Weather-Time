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
        [HideInInspector] _TwinkleDetail ("Twinkle Detail", Int) = 3
        [HideInInspector] _TwinkleSharpness ("Twinkle Sharpness", Float) = 5.0
        [HideInInspector] _TwinkleSpeed ("Twinkle Speed", Float) = 0.004
        [HideInInspector] _TwinkleStrength ("Twinkle Strength", Range(0.0, 2.0)) = 0.8

        [Header(Moon)]
        [NoScaleOffset] _MoonTex ("Moon Texture", 2D) = "white" {}
        _MoonColor ("Moon Color", Color) = (0.85, 0.85, 0.8, 1)
        _MoonSize ("Moon Size", Range(0.005, 0.05)) = 0.012
        [HideInInspector] _MoonPosition ("Moon Direction", Vector) = (0.0, -0.5, 0.0, 0.0)

        [Header(Volumetric Clouds)]
        [NoScaleOffset] _WeatherMapTex ("Weather Map (RGBA)", 2D) = "black" {}
        [NoScaleOffset] _CloudTex ("Cloud Noise (RGBA)", 2D) = "black" {}
        
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
            int   _TwinkleDetail;
            float _TwinkleSharpness;
            float _TwinkleSpeed;
            float _TwinkleStrength;

            // Moon
            sampler2D _MoonTex;
            float4    _MoonColor;
            float     _MoonSize;
            float3    _MoonPosition;

            // Volumetric Clouds
            sampler2D _WeatherMapTex;
            sampler2D _CloudTex;
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

            // --- Atmosphere Integration ---
            #define ATMOSPHERE_STEPS 16

            // --- Cloud Raymarching ---
            #define CLOUD_STEPS         32
            #define CLOUD_PLANET_RADIUS 600000.0
            #define CLOUD_THICKNESS     1500.0

            // =====================================================================
            //  UTILITY: 3D Gradient Noise & FBM
            // =====================================================================

            float3 hash(float3 p)
            {
                p = float3(
                    dot(p, float3(127.1, 311.7, 74.7)),
                    dot(p, float3(269.5, 183.3, 246.1)),
                    dot(p, float3(113.5, 271.9, 124.6))
                );
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(dot(hash(i + float3(0,0,0)), f - float3(0,0,0)),
                              dot(hash(i + float3(1,0,0)), f - float3(1,0,0)), f.x),
                         lerp(dot(hash(i + float3(0,1,0)), f - float3(0,1,0)),
                              dot(hash(i + float3(1,1,0)), f - float3(1,1,0)), f.x), f.y),
                    lerp(lerp(dot(hash(i + float3(0,0,1)), f - float3(0,0,1)),
                              dot(hash(i + float3(1,0,1)), f - float3(1,0,1)), f.x),
                         lerp(dot(hash(i + float3(0,1,1)), f - float3(0,1,1)),
                              dot(hash(i + float3(1,1,1)), f - float3(1,1,1)), f.x), f.y),
                    f.z) * 0.5 + 0.5;
            }

            float fbm3(float3 p)
            {
                float value = 0.0, amp = 0.5, freq = 1.0;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    value += amp * noise(p * freq);
                    amp *= 0.5;
                    freq *= 2.0;
                }
                return value;
            }

            // =====================================================================
            //  UTILITY: Interleaved Gradient Noise (IGN)
            // =====================================================================
            float IGN(float2 screenPos)
            {
                return frac(52.9829189 * frac(dot(screenPos, float2(0.06711056, 0.00583715))));
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
            //  Returns (near, far) distances. Both negative = no intersection.
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
            //  Replaces the expensive ComputeOpticalDepth integration loop.
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
                float3 totalInscatter    = 0.0;
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

                    float3 inscatterContribution = 0.0; 
                    
                    float sunTransLum = dot(sunTransmittance, float3(0.2126, 0.7152, 0.0722));
                    
                    if (sunTransLum > 0.00001) 
                    {
                        float3 scatterR = betaR * localDensity.x * phaseR;
                        float3 scatterM = betaM * localDensity.y * phaseM;
                        inscatterContribution = sunTransmittance * (scatterR + scatterM) * stepSize;
                    }

                    // Multiple scattering approximation (vanishes at night)
                    float msStrength = saturate(sunDir.y * 5.0 + 0.1);
                    msStrength *= msStrength;
                    if (msStrength > 0.001)
                    {
                        float3 msApprox = (betaR * localDensity.x + betaM * localDensity.y * 0.25) * stepSize;
                        float sunH01 = saturate(sunDir.y * 3.0 + 0.3);
                        float3 msColor = lerp(float3(0.01, 0.01, 0.02), float3(0.04, 0.06, 0.12), sunH01);
                        inscatterContribution += msApprox * msColor * 0.15 * msStrength;
                    }

                    totalInscatter += totalTransmittance * inscatterContribution;
                    totalTransmittance *= stepTransmittance;
                }

                float3 skyColor = totalInscatter;

                // Night floor: suppress residual atmospheric glow
                float nightDarkening = saturate(sunDir.y * 4.0 + 0.6);
                skyColor *= max(nightDarkening * nightDarkening, 0.001);

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
            //  Uses 2D weather map (RGBA) + noise texture for shape & erosion.
            //  Vertical profile blends Stratus / Cumulus / Cumulonimbus by type.
            // =====================================================================

            float SampleCloudDensity(float3 p, float heightFraction)
            {
                float2 weatherUV = p.xz * 0.000005 * _CloudScale + (_CloudWind * 0.1);
                float4 weather = tex2Dlod(_WeatherMapTex, float4(weatherUV, 0, 0));

                float weatherCoverage = weather.r;
                float cloudType       = weather.g;
                float erosionMask     = weather.b;
                float densityMod      = weather.a;

                float weatherThreshold = 1.0 - _CloudCoverage;
                float macroCoverage = smoothstep(weatherThreshold - 0.15, weatherThreshold + 0.15, weatherCoverage);
                if (macroCoverage < 0.01) return 0.0;

                float h = heightFraction;
                float stratusProfile = smoothstep(0.0, 0.1, h) * smoothstep(0.4, 0.2, h);
                float cumulusProfile = saturate(4.0 * h * (1.0 - h));
                float cbProfile      = smoothstep(0.0, 0.1, h) * smoothstep(1.0, 0.6, h);

                float t = cloudType * 2.0;
                float verticalProfile = (t < 1.0)
                    ? lerp(stratusProfile, cumulusProfile, t)
                    : lerp(cumulusProfile, cbProfile, t - 1.0);

                float2 baseUV = p.xz * 0.00002 * _CloudScale + _CloudWind;
                float2 wobble = float2(cos(h * 4.0), sin(h * 4.0)) * 0.015;
                float baseShape = tex2Dlod(_CloudTex, float4(baseUV + wobble, 0, 0)).r;

                float2 erosionUV = baseUV + float2(h, h * 0.8) * 0.3;
                float4 erosionData = tex2Dlod(_CloudTex, float4(erosionUV, 0, 0));

                float erosionAmount = _CloudDetail * erosionMask;
                baseShape -= erosionData.g * erosionAmount * lerp(0.5, 1.0, h);
                baseShape -= erosionData.b * _CloudWisp * 0.5;
                baseShape *= smoothstep(0.0, 0.15, h);

                float density = smoothstep(1.0 - macroCoverage, 1.01, baseShape);
                return saturate(density * verticalProfile * densityMod) * _CloudDensity;
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
                float starVisibility = _StarsFade * saturate(1.0 - skyLuminance * 50.0);
                starVisibility *= smoothstep(-0.02, 0.15, direction.y);

                if (starVisibility > 0.001)
                {
                    float3 spaceDir = RotateSphere(direction, _StarfieldRotation);

                    float3 starCol = texCUBE(_StarsCube, spaceDir).rgb * _StarsIntensity;
                    float3 mwRaw   = texCUBE(_MilkyWayCube, spaceDir).rgb;
                    
                    float mwLum = dot(mwRaw, float3(0.2126, 0.7152, 0.0722));
                    float3 mwCol = lerp(mwRaw, float3(mwLum, mwLum, mwLum), 0.4) * _MilkyWayIntensity;

                    float twinkleNoise = fbm3(
                        spaceDir * _TwinkleScale + float3(0, 0, frac(_Time.y * _TwinkleSpeed) * 100.0)
                    );
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
                //  3. MOON
                //  Dynamic phase from Sun-Moon geometry.
                //  Atmospheric extinction reddens the moon near horizon.
                // =============================================================

                float3 moonDir = normalize(_MoonPosition);

                // Stable tangent frame (avoids singularity at zenith)
                float3 moonRight = normalize(cross(
                    abs(moonDir.y) > 0.999 ? float3(1,0,0) : float3(0,1,0),
                    moonDir
                ));
                float3 moonUp = cross(moonDir, moonRight);

                float2 moonPlanarUV = float2(dot(direction, moonRight), dot(direction, moonUp)) / _MoonSize;

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

                        float3 sunInMoonSpace = float3(
                            dot(i.sunDirection, moonRight),
                            dot(i.sunDirection, moonUp),
                            dot(i.sunDirection, moonDir)
                        );
                        float phaseDot = dot(
                            float3(sphereNormal.xy, -sphereNormal.z),
                            normalize(sunInMoonSpace)
                        );
                        float phaseIllumination = smoothstep(-0.01, 0.05, phaseDot);

                        float earthshine = 0.003 * (1.0 - phaseIllumination);
                        float moonLighting = phaseIllumination + earthshine;

                        float moonAltAngle = dot(moonDir, up);
                        float moonExtPath  = 1.0 / max(moonAltAngle, 0.035);
                        float3 moonAtmosTint = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT * min(moonExtPath, 40.0));

                        float moonBrightness = 0.05 * smoothstep(-0.05, 0.15, moonAltAngle);

                        float3 moonVisual = moonTex.rgb * _MoonColor.rgb * moonLighting * moonBrightness * moonAtmosTint;
                        float  moonAlpha  = smoothstep(1.0, 0.92, r2) * moonTex.a * _MoonColor.a;

                        float3 moonComposite = max(finalColor, moonVisual);
                        finalColor = lerp(finalColor, moonComposite, moonAlpha);
                    }

                    // Lunar halo
                    float moonAngularDist = acos(clamp(dot(direction, moonDir), -1.0, 1.0));
                    float moonHalo = exp(-moonAngularDist * moonAngularDist * 3000.0) * 0.04
                                   + exp(-moonAngularDist * 60.0) * 0.008;
                    float moonHaloVisibility = smoothstep(-0.05, 0.1, dot(moonDir, up));
                    float3 moonHaloColor = _MoonColor.rgb * moonHalo * moonHaloVisibility * 0.01;
                    finalColor += moonHaloColor;
                }

                // =============================================================
                //  4. VOLUMETRIC CLOUDS (Raymarched)
                //  Beer-Lambert extinction, multi-sample light march,
                //  Beer-Powder dark edge, dual HG phase.
                //  Full moon (sun opposite): dot ≈ -1 → phaseBrightness ≈ 1.0
                //  New moon (sun same side): dot ≈ +1 → phaseBrightness ≈ 0.0
                // =============================================================
                
                float moonPhaseBrightness = saturate(0.5 - 0.5 * dot(i.sunDirection, moonDir));
                moonPhaseBrightness *= moonPhaseBrightness;

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
                        float maxDist     = 60000.0;

                        if (distToStart <= maxDist)
                        {
                            float rayLength = min(distToEnd - distToStart, maxDist);
                            float stepSize  = rayLength / float(CLOUD_STEPS);
                            float3 startPos = camOrigin + direction * distToStart;

                            float dither = IGN(i.vertex.xy + float2(0, fmod(_Time.y, 8.0) * 7.0));
                            startPos += direction * stepSize * dither;

                            // --- Light source blending (sun ↔ moon) ---
                            float sunWeight  = smoothstep(-0.04, 0.08, sunHeight);
                            float moonWeight = smoothstep(-0.1, 0.1, dot(moonDir, up));

                            float3 mainLightDir   = lerp(moonDir, i.sunDirection, sunWeight);
                            float3 sunLightColor  = lerp(float3(1, 0.6, 0.3), float3(1, 1, 1), saturate(sunHeight * 3.0));
                            sunLightColor *= lerp(0.0, 1.0, sunWeight);
                            float3 moonLightColor = _MoonColor.rgb * 0.008 * moonWeight * moonPhaseBrightness;
                            float3 activeLightColor = lerp(moonLightColor, sunLightColor, sunWeight);

                            float3 atmosphereTint = saturate(finalColor);
                            float3 dayAmbient   = lerp(_CloudShadowColor.rgb * 0.5, atmosphereTint, 0.4) * 0.3;
                            float3 nightAmbient = _CloudShadowColor.rgb * lerp(0.001, 0.003, moonPhaseBrightness);
                            float3 activeAmbient = lerp(nightAmbient, dayAmbient, sunWeight);

                            float cloudCosTheta = dot(direction, mainLightDir);
                            float dayPhase   = DualHG(cloudCosTheta, 0.85, -0.3, 0.7);
                            float nightPhase = DualHG(cloudCosTheta, 0.5, -0.2, 0.6);
                            float phaseVal   = lerp(nightPhase, dayPhase, sunWeight);

                            // --- Absorption coefficient ---
                            float absorptionCoeff = _CloudScatter * 0.002;

                            float3 accumColor    = 0;
                            float  transmittance = 1.0;

                            [loop]
                            for (int j = 0; j < CLOUD_STEPS; j++)
                            {
                                float3 pos = startPos + direction * (stepSize * j);
                                float heightInfo = (length(pos) - cloudBottomRad) / CLOUD_THICKNESS;

                                float heightMask = step(0.0, heightInfo) * step(heightInfo, 1.0);
                                if (heightMask < 0.5) continue;

                                float dens = SampleCloudDensity(pos + cloudPlanetCenter, heightInfo);
                                if (dens < 0.001) continue;

                                // ---- Light march: 6 steps toward sun ----
                                float lightOpticalDepth = 0.0;

                                [unroll]
                                for (int k = 0; k < 6; k++)
                                {
                                    // Step distances: ~25, ~75, ~150, ~250, ~375, ~525m (total ≈ CLOUD_THICKNESS)
                                    float t0 = CLOUD_THICKNESS * (float(k) * float(k)) / 36.0;
                                    float t1 = CLOUD_THICKNESS * (float(k + 1) * float(k + 1)) / 36.0;
                                    float lightStepSize = t1 - t0;
                                    float lightDist = (t0 + t1) * 0.5;

                                    float3 lightSamplePos = pos + mainLightDir * lightDist;
                                    float lightH = (length(lightSamplePos) - cloudBottomRad) / CLOUD_THICKNESS;

                                    float lightMask = step(0.0, lightH) * step(lightH, 1.0);
                                    float lightDens = SampleCloudDensity(lightSamplePos + cloudPlanetCenter, lightH) * lightMask;

                                    lightOpticalDepth += lightDens * lightStepSize * absorptionCoeff;
                                }

                                // ---- Beer-Lambert ----
                                float beerTerm = exp(-lightOpticalDepth);

                                // ---- Beer-Powder ----
                                float powderTerm = 1.0 - exp(-lightOpticalDepth * 2.0);
                                float beerPowder = beerTerm * lerp(1.0, powderTerm * 2.0, 0.5);

                                // ---- Ambient contribution from height ----
                                float heightGradient = lerp(0.3, 1.0, heightInfo);

                                // ---- Combine lighting ----
                                float3 directLight = activeLightColor * beerPowder * phaseVal;
                                float3 ambientLight = activeAmbient * heightGradient;
                                float3 cloudPointColor = _CloudColor.rgb * (directLight + ambientLight);

                                // ---- View ray extinction ----
                                float stepOpticalDepth = dens * stepSize * absorptionCoeff;
                                float stepTransmittance = exp(-stepOpticalDepth);
                                float alphaStep = 1.0 - stepTransmittance;

                                accumColor   += cloudPointColor * alphaStep * transmittance;
                                transmittance *= stepTransmittance;

                                if (transmittance < 0.01) break;
                            }

                            float distFade         = 1.0 - smoothstep(maxDist * 0.7, maxDist, distToStart);
                            float horizonAlphaFade = smoothstep(0.0, 0.12, direction.y);
                            float totalFade        = distFade * horizonAlphaFade;

                            accumColor   *= totalFade;
                            transmittance = lerp(1.0, transmittance, totalFade);

                            // Aerial perspective
                            float aerialFactor = smoothstep(maxDist * 0.2, maxDist * 0.9, distToStart) * 0.6;
                            float3 horizonAtmosColor = saturate(finalColor);
                            accumColor = lerp(accumColor, horizonAtmosColor * (1.0 - transmittance), aerialFactor);

                            finalColor = finalColor * transmittance + accumColor;
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
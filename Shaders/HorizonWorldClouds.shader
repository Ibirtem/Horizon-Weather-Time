Shader "Horizon/World Clouds"
{
    Properties
    {
        [Header(Cloud Geometry)]
        _CloudAltitude ("Cloud Altitude Base (km)", Float) = 2.0
        _CloudThickness ("Cloud Thickness (m)", Float) = 2500.0
        _CloudScale ("Cloud Scale", Float) = 1.0
        _CloudCoverage ("Coverage", Range(0.0, 1.0)) = 0.6
        _CloudDensity ("Density Multiplier", Float) = 1.0

        [Header(Lighting and Phase)]
        _CloudColor ("Cloud Lit Color", Color) = (0.9, 0.9, 0.9, 1.0)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.3, 0.3, 0.35, 1.0)
        _CloudDetail ("Erosion Amount", Range(0.0, 1.0)) = 0.5
        _CloudWisp ("Wispiness", Range(0.0, 1.0)) = 0.3
        _CloudScatter ("Light Absorption", Float) = 0.5
        _Exposure ("Exposure", Float) = 15.0

        [Header(Atmosphere)]
        _Rayleigh ("Rayleigh", Float) = 1.0
        _Turbidity ("Turbidity", Float) = 2.5
        _MieCoefficient ("Mie Coefficient", Float) = 0.005
        _MieDirectionalG ("Mie Directional G", Float) = 0.76
        [NoScaleOffset] _TransmittanceLUT ("Transmittance LUT", 2D) = "white" {}

        [Header(Textures)]
        [NoScaleOffset] _CloudNoise3D ("Cloud Noise 3D (RGBA)", 3D) = "black" {}
        [NoScaleOffset] _WeatherMapTex ("Weather Map (RGBA)", 2D) = "black" {}
        [NoScaleOffset] _BlueNoiseTex ("Blue Noise (R)", 2D) = "black" {}
        [NoScaleOffset] _CurlNoiseTex ("Curl Noise (RG)", 2D) = "gray" {}

        [HideInInspector] _CloudWind ("Cloud Offset", Vector) = (0,0,0,0)
        [HideInInspector] _CloudTime ("Cloud Time (UTC)", Float) = 0.0
        [HideInInspector] _SunPosition ("Sun Direction", Vector) = (0, 1, 0, 0)
        [HideInInspector] _MoonPosition ("Moon Direction", Vector) = (0, -1, 0, 0)
        [HideInInspector] _MoonColor ("Moon Color", Color) = (0.85, 0.85, 0.8, 1.0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend One OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #pragma multi_compile_instancing

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            sampler3D _CloudNoise3D;
            sampler2D _WeatherMapTex;
            sampler2D _BlueNoiseTex;
            sampler2D _CurlNoiseTex;

            float4 _CloudColor;
            float4 _CloudShadowColor;
            float  _CloudAltitude;
            float  _CloudThickness;
            float  _CloudScale;
            float  _CloudCoverage;
            float  _CloudDensity;
            float  _CloudDetail;
            float  _CloudWisp;
            float  _CloudScatter;
            float  _Exposure;
            float2 _CloudWind;
            float  _CloudTime;

            sampler2D _TransmittanceLUT;
            float _Rayleigh;
            float _Turbidity;
            float _MieCoefficient;
            float _MieDirectionalG;

            float3 _SunPosition; // Direction TO sun
            float3 _MoonPosition;
            float4 _MoonColor;

            #define PI   3.14159265358979
            #define PLANET_GROUND_RADIUS 6371000.0
            #define ATMOSPHERE_THICKNESS 100000.0
            #define ATMOSPHERE_RADIUS    (PLANET_GROUND_RADIUS + ATMOSPHERE_THICKNESS)

            static const float3 RAYLEIGH_BETA = float3(5.8e-6, 13.5e-6, 33.1e-6);
            static const float  RAYLEIGH_SCALE_HEIGHT = 8500.0;
            #define MIE_BETA_BASE 21e-6
            static const float MIE_SCALE_HEIGHT = 1200.0;
            static const float3 OZONE_BETA = float3(0.65e-6, 1.88e-6, 0.085e-6);
            #define OZONE_CENTER_ALT 25000.0
            #define OZONE_HALF_WIDTH 15000.0
            #define SUN_ANGULAR_COS 0.99998

            #define ATMOSPHERE_STEPS 16
            #define MS_TWILIGHT_RANGE 1.8
            #define MS_TWILIGHT_OFFSET 0.5
            #define MS_INTENSITY 0.2
            #define STARLIGHT_AMBIENT float3(0.00006, 0.00012, 0.0003)
            #define TWILIGHT_STEEPNESS 1.0
            #define TWILIGHT_OFFSET 1.0

            #define CLOUD_NOISE_FREQ        0.0003
            #define CLOUD_WEATHER_FREQ      0.00004
            #define CLOUD_VERTICAL_SCALE    2.0
            #define CLOUD_CURL_UV_SCALE     0.0004
            #define CLOUD_CURL_STRENGTH     0.08

            #define CLOUD_MAX_STEPS         80
            #define CLOUD_MIN_STEPS         36
            #define CLOUD_PLANET_RADIUS     600000.0
            #define CLOUD_MAX_DISTANCE      60000.0
            #define CLOUD_TRANSMITTANCE_MIN 0.01

            #define CLOUD_LIGHT_STEPS       4
            #define LIGHT_DENOM             21.0
            #define CLOUD_ABSORPTION_SCALE  0.0038

            // === ATMOSPHERE HELPERS (COPIED EXACTLY) ===
            float Remap(float value, float inMin, float inMax, float outMin, float outMax)
            {
                return outMin + saturate((value - inMin) / (inMax - inMin)) * (outMax - outMin);
            }

            float InterleavedGradientNoise(float2 pixelPos)
            {
                return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
            }
            
            float RayleighPhase(float cosTheta) { return (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta); }
            float CornetteShanksPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                float num = 3.0 * (1.0 - g2) * (1.0 + cosTheta * cosTheta);
                float denom = (8.0 * PI) * (2.0 + g2) * pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5);
                return num / max(denom, 1e-10);
            }

            float3 AtmosphereDensity(float altitude)
            {
                float rayleigh = exp(-altitude / RAYLEIGH_SCALE_HEIGHT);
                float mie      = exp(-altitude / MIE_SCALE_HEIGHT);
                float ozone    = max(0.0, 1.0 - abs(altitude - OZONE_CENTER_ALT) / OZONE_HALF_WIDTH);
                return float3(rayleigh, mie, ozone);
            }

            float2 AtmosphereRayIntersect(float3 origin, float3 dir, float radius)
            {
                float b = dot(origin, dir);
                float c = dot(origin, origin) - radius * radius;
                float discriminant = b * b - c;
                if (discriminant < 0.0) return float2(-1.0, -1.0);
                float sqrtDisc = sqrt(discriminant);
                return float2(-b - sqrtDisc, -b + sqrtDisc);
            }

            float3 SampleOpticalDepthLUT(float altitude, float cosTheta)
            {
                float v = sqrt(saturate(altitude / ATMOSPHERE_THICKNESS));
                float absCos = abs(cosTheta);
                float u = 0.5 + sign(cosTheta) * 0.5 * sqrt(absCos);
                return tex2Dlod(_TransmittanceLUT, float4(u, v, 0, 0)).rgb;
            }

            float4 ComputeAtmosphere(float3 viewDir, float3 sunDir, float turbidity, float rayleighMul, float mieMul, float mieG)
            {
                float3 origin = float3(0, PLANET_GROUND_RADIUS, 0);
                float2 atmosHit  = AtmosphereRayIntersect(origin, viewDir, ATMOSPHERE_RADIUS);
                if (atmosHit.y < 0.0) return 0.0;

                float2 groundHit = AtmosphereRayIntersect(origin, viewDir, PLANET_GROUND_RADIUS);
                bool hitsGround  = (groundHit.x > 0.0);

                float rayStart  = max(0.0, atmosHit.x);
                float rayEnd    = hitsGround ? groundHit.x : atmosHit.y;
                float rayLength = rayEnd - rayStart;
                if (rayLength <= 0.0) return 0.0;

                float3 betaR = RAYLEIGH_BETA * rayleighMul;
                float  mieBeta = MIE_BETA_BASE * turbidity * mieMul;
                float3 betaM = float3(mieBeta, mieBeta, mieBeta);
                float3 betaO = OZONE_BETA;

                float cosTheta = dot(viewDir, sunDir);
                float phaseR   = RayleighPhase(cosTheta);
                float phaseM   = CornetteShanksPhase(cosTheta, mieG);

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

                    float3 stepExtinction = (betaR * localDensity.x + betaM * localDensity.y + betaO * localDensity.z) * stepSize;
                    float3 stepTransmittance = exp(-stepExtinction);

                    float sunCosTheta = dot(normalize(samplePos), sunDir);
                    float3 sunOpticalDepth = SampleOpticalDepthLUT(altitude, sunCosTheta);
                    float3 sunTransmittance = exp(-(betaR * sunOpticalDepth.x + betaM * sunOpticalDepth.y + betaO * sunOpticalDepth.z));

                    half3 inscatterContribution = 0.0; 
                    float sunTransLum = dot(sunTransmittance, float3(0.2126, 0.7152, 0.0722));
                    
                    if (sunTransLum > 0.00001) 
                    {
                        float3 scatterR = betaR * localDensity.x * phaseR;
                        float3 scatterM = betaM * localDensity.y * phaseM;
                        inscatterContribution = sunTransmittance * (scatterR + scatterM) * stepSize;
                    }

                    float msStrength = saturate(sunDir.y * MS_TWILIGHT_RANGE + MS_TWILIGHT_OFFSET);
                    msStrength *= msStrength;
                    if (msStrength > 0.001)
                    {
                        float3 msApprox = (betaR * localDensity.x + betaM * localDensity.y * 0.25) * stepSize;
                        float sunH01 = saturate(sunDir.y * 2.5 + 0.5);
                        float3 msColor = lerp(float3(0.015, 0.018, 0.04), float3(0.05, 0.07, 0.13), sunH01);
                        float msHorizonMask = smoothstep(0.0, 0.15, abs(viewDir.y));
                        inscatterContribution += msApprox * msColor * MS_INTENSITY * msStrength * msHorizonMask;
                    }

                    totalInscatter += totalTransmittance * inscatterContribution;
                    totalTransmittance *= stepTransmittance;
                }

                float3 skyColor = totalInscatter;
                float nightDarkening = saturate(sunDir.y * TWILIGHT_STEEPNESS + TWILIGHT_OFFSET);
                skyColor *= (nightDarkening * nightDarkening);

                float viewZenith = max(0.0, viewDir.y);
                float starlightPath = 1.0 / (viewZenith + 0.4); 
                skyColor += STARLIGHT_AMBIENT * starlightPath;

                if (hitsGround)
                {
                    float3 groundPoint  = origin + viewDir * rayEnd;
                    float3 groundNormal = normalize(groundPoint);
                    float  groundNdotL  = max(0.0, dot(groundNormal, sunDir));
                    skyColor += totalTransmittance * float3(0.1, 0.1, 0.1) * groundNdotL * 0.05;
                }
                return float4(skyColor, 1.0);
            }

            // === CLOUD HELPERS (COPIED EXACTLY) ===
            float2 CloudRaySphere(float3 rayOrigin, float3 rayDir, float sphereRadius)
            {
                float t = dot(-rayOrigin, rayDir);
                float3 p = rayOrigin + rayDir * t;
                float d2 = dot(p, p);
                if (d2 > sphereRadius * sphereRadius) return float2(-1, -1);
                float x = sqrt(sphereRadius * sphereRadius - d2);
                return float2(t - x, t + x);
            }

            float DualHG(float cosTheta, float g1, float g2, float blend)
            {
                float g1Sq = g1 * g1; float g2Sq = g2 * g2;
                float denom1 = max(1e-5, 1.0 + g1Sq - 2.0 * g1 * cosTheta);
                float denom2 = max(1e-5, 1.0 + g2Sq - 2.0 * g2 * cosTheta);
                float p1 = (1.0 - g1Sq) / (4.0 * PI * pow(denom1, 1.5));
                float p2 = (1.0 - g2Sq) / (4.0 * PI * pow(denom2, 1.5));
                return lerp(p1, p2, blend);
            }

            float EvaluateMultiScattering(float opticalDepth, float cosTheta, float sunWeight)
            {
                const float a = 0.55; const float b = 0.50; const float c = 0.50;
                float g1 = lerp(0.3, 0.82, sunWeight);
                float g2 = lerp(-0.1, -0.25, sunWeight);
                float blend = lerp(0.4, 0.7, sunWeight);
                float powderWeight = lerp(0.8, 0.2, saturate(cosTheta * 0.5 + 0.5));
                float msAccum = 0.0; float curA = 1.0; float curB = 1.0; float curC = 1.0;
                const float isotropicPhase = 1.0 / (4.0 * PI);

                [unroll]
                for (int oct = 0; oct < 3; oct++)
                {
                    float tau = opticalDepth * curB;
                    float beer = exp(-tau);
                    float bounceAttenuation = (oct == 0) ? beer * lerp(1.0, (1.0 - exp(-tau * 2.0)) * 2.0, powderWeight) : beer;
                    float phaseHG = DualHG(cosTheta, g1 * curC, g2 * curC, blend);
                    float phase = lerp(phaseHG, isotropicPhase * (4.0 * PI * 0.45), 1.0 - curC);
                    msAccum += curA * bounceAttenuation * phase;
                    curA *= a; curB *= b; curC *= c;
                }
                float nightFloor = lerp(msAccum, 0.08 * DualHG(cosTheta, 0.3, -0.1, 0.4), 0.5);
                return lerp(nightFloor, msAccum, sunWeight);
            }

            struct CloudWeather { float coverage; float type; float erosion; float density; float macro; };

            float CloudHeightGradient(float h, float cloudType, float localCoverage)
            {
                float lclBase = lerp(0.12, 0.02, saturate(localCoverage * 1.3));
                float baseCut = smoothstep(lclBase, lclBase + 0.07, h);
                float ceiling = lerp(0.60, 0.95, saturate(cloudType * 0.6 + localCoverage * 0.4));
                float topCut  = smoothstep(ceiling, ceiling - 0.28, h);
                float cumulusEnvelope = baseCut * topCut;
                float stratusEnvelope = smoothstep(0.01, 0.05, h) * smoothstep(0.35, 0.15, h);
                return lerp(stratusEnvelope, cumulusEnvelope, saturate(cloudType * 1.5 + 0.2));
            }

            float SampleCloudDensityUnified(float3 p, float heightFraction, float lod, CloudWeather weather, bool applyErosion, float detailFade)
            {
                float h = heightFraction;
                float horizFreq = CLOUD_NOISE_FREQ * _CloudScale;
                float shear = pow(h, 1.3) * 0.30;
                float2 altitudeShear = float2(shear, shear * 0.5);

                float2 curlOffset = float2(0, 0);
                if (applyErosion && detailFade > 0.001)
                {
                    float2 curlUV = p.xz * CLOUD_CURL_UV_SCALE + _CloudWind * 0.05;
                    curlOffset = (tex2Dlod(_CurlNoiseTex, float4(curlUV, 0, 0)).rg * 2.0 - 1.0) * (CLOUD_CURL_STRENGTH * detailFade);
                }

                float3 noiseUVW = float3(p.x * horizFreq + _CloudWind.x + altitudeShear.x + curlOffset.x, h * CLOUD_VERTICAL_SCALE, p.z * horizFreq + _CloudWind.y + altitudeShear.y + curlOffset.y);
                float4 noise3D = tex3Dlod(_CloudNoise3D, float4(noiseUVW, lod));

                float covThreshold = (1.0 - _CloudCoverage) * 0.85;
                float weatherCoverage = smoothstep(covThreshold, covThreshold + 0.35, weather.coverage);
                if (weatherCoverage < 0.001) return 0.0;

                float baseNoise = noise3D.r;

                float heightGrad = CloudHeightGradient(h, weather.type, weatherCoverage);
                float shapedNoise = saturate(baseNoise - (1.0 - heightGrad) * 0.85);
                
                float cloudThreshold = (1.0 - weatherCoverage) * 0.60;
                float baseShape = saturate((shapedNoise - cloudThreshold) / 0.32);

                if (baseShape < 0.001) return 0.0;
                baseShape = pow(baseShape, 0.85) * weatherCoverage;
                if (baseShape < 0.001) return 0.0;
                baseShape = pow(baseShape, 0.80) * weatherCoverage;

                if (applyErosion && detailFade > 0.001)
                {
                    float3 detailUVW = noiseUVW * 3.0;
                    float4 detail3D  = tex3Dlod(_CloudNoise3D, float4(detailUVW, lod + 0.5));
                    float detailFBM  = dot(detail3D.gba, float3(0.5, 0.3, 0.2));
                    float wispyBottom = 1.0 - detailFBM;
                    float billowTop   = detailFBM;
                    float cottonDetail = lerp(wispyBottom, billowTop, saturate(h * 2.2 - 0.2));
                    float edgeMask = smoothstep(0.0, 0.45, baseShape) * smoothstep(0.98, 0.40, baseShape);
                    float erosionDepth = _CloudDetail * (0.24 + _CloudWisp * 0.22) * edgeMask * detailFade;
                    baseShape = saturate((baseShape - cottonDetail * erosionDepth) / max(0.001, 1.0 - cottonDetail * erosionDepth));
                }
                return baseShape * weather.density * _CloudDensity;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 camPos = _WorldSpaceCameraPos.xyz;
                float3 rayDir = normalize(i.worldPos - camPos);

                float2 uv = i.screenPos.xy / max(1e-5, i.screenPos.w);
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float sceneDepth = LinearEyeDepth(rawDepth);

                #if defined(UNITY_REVERSED_Z)
                bool isSky = (rawDepth <= 0.0);
                #else
                bool isSky = (rawDepth >= 1.0);
                #endif

                float rayCos = max(1e-4, i.screenPos.w / max(1e-4, length(i.worldPos - camPos)));
                float maxSceneDist = isSky ? 1e9 : (sceneDepth / rayCos);

                float3 cloudPlanetCenter = float3(0, -CLOUD_PLANET_RADIUS, 0);
                float3 camOrigin = camPos - cloudPlanetCenter;

                float cloudThicknessMeters = max(500.0, _CloudThickness);
                float cloudBottomRad = CLOUD_PLANET_RADIUS + (_CloudAltitude * 1000.0);
                float cloudTopRad    = cloudBottomRad + cloudThicknessMeters;

                float2 hitBottom = CloudRaySphere(camOrigin, rayDir, cloudBottomRad);
                float2 hitTop    = CloudRaySphere(camOrigin, rayDir, cloudTopRad);

                if (hitTop.y <= 0.0) discard;

                float horizonScale = saturate(rayDir.y * 3.5);
                float maxDist = lerp(32000.0, CLOUD_MAX_DISTANCE, horizonScale);

                float distToStart = 0.0;
                float distToEnd = min(hitTop.y, maxDist);

                if (camOrigin.y < cloudBottomRad)
                {
                    if (hitBottom.y <= 0.0) discard;
                    distToStart = hitBottom.y;
                }
                else if (camOrigin.y > cloudTopRad)
                {
                    if (hitTop.x <= 0.0) discard;
                    distToStart = hitTop.x;
                    if (hitBottom.x > 0.0) distToEnd = min(distToEnd, hitBottom.x);
                }
                else
                {
                    distToStart = 0.0;
                    if (hitBottom.x > 0.0) distToEnd = min(distToEnd, hitBottom.x);
                }

                distToEnd = min(distToEnd, maxSceneDist);
                float rayLength = max(0.0, distToEnd - distToStart);
                if (distToStart >= distToEnd || rayLength <= 10.0) discard;

                float3 startPos = camOrigin + rayDir * distToStart;

                bool anyCloud = false;
                float preThresh = 1.0 - _CloudCoverage - 0.2;
                float2 weatherDrift = float2(sin(_CloudTime * 0.00008) * 0.04, cos(_CloudTime * 0.00006) * 0.03);

                [unroll]
                for (int pre = 0; pre < 5; pre++)
                {
                    float3 prePos = startPos + rayDir * (rayLength * ((float)pre + 0.5) * 0.2);
                    float2 preUV = prePos.xz * 0.000025 * _CloudScale + (_CloudWind * 0.1) + weatherDrift;
                    if (tex2Dlod(_WeatherMapTex, float4(preUV, 0, 0)).r > preThresh) { anyCloud = true; break; }
                }
                if (!anyCloud) discard;

                float3 sunDir = normalize(_SunPosition);
                float3 moonDir = normalize(_MoonPosition);
                float3 up = float3(0, 1, 0);

                float sunHeight = dot(sunDir, up);
                float cloudAltMeters = _CloudAltitude * 1000.0;
                float cloudSunsetAngle = -sqrt(2.0 * cloudAltMeters / PLANET_GROUND_RADIUS);

                float sunWeight = smoothstep(cloudSunsetAngle - 0.02, cloudSunsetAngle + 0.04, sunHeight);
                float moonWeight = smoothstep(-0.1, 0.1, dot(moonDir, up));

                float3 mainLightDir = lerp(moonDir, sunDir, sunWeight);

                float sunOptPath = 1.0 / max(0.015, sunHeight + 0.035);
                float3 sunDirectExtinction = exp(-RAYLEIGH_BETA * RAYLEIGH_SCALE_HEIGHT * min(sunOptPath, 42.0));

                float sunHorizonDimming = smoothstep(-0.03, 0.06, sunHeight) * 0.75 + 0.25;
                float3 sunLightColor = sunDirectExtinction * (sunHorizonDimming * sunWeight);

                float moonPhaseFraction = saturate(0.5 - 0.5 * dot(sunDir, moonDir));
                float moonPhaseBrightness = moonPhaseFraction * moonPhaseFraction * moonPhaseFraction;

                float3 moonLightColor = _MoonColor.rgb * 0.008 * moonWeight * moonPhaseBrightness;
                float3 activeLightColor = lerp(moonLightColor, sunLightColor, sunWeight);

                float4 atmosResult = ComputeAtmosphere(rayDir, sunDir, _Turbidity, _Rayleigh, _MieCoefficient, _MieDirectionalG);
                float3 atmosphereTint = saturate(atmosResult.rgb);

                float3 dayAmbient = lerp(_CloudShadowColor.rgb, atmosphereTint, 0.30);
                float3 nightAmbient = _CloudShadowColor.rgb * lerp(0.002, 0.008, moonPhaseBrightness);

                float skyLuma = dot(atmosphereTint, float3(0.2126, 0.7152, 0.0722));
                float twilightTransition = saturate(sunHeight * 12.0 + 0.5);
                float3 twilightAmbient = lerp(atmosphereTint * 0.65, dayAmbient, twilightTransition);

                float3 activeAmbient = lerp(nightAmbient, twilightAmbient, saturate(skyLuma * 18.0));
                float cloudCosTheta = dot(rayDir, mainLightDir);

                float absorptionCoeff = _CloudScatter * CLOUD_ABSORPTION_SCALE;

                float elev = saturate(rayDir.y);
                int dynamicSteps = (int)lerp((float)CLOUD_MAX_STEPS, (float)CLOUD_MIN_STEPS, sqrt(elev));
                float stepFraction = 1.0 / (float)dynamicSteps;

                float dither = InterleavedGradientNoise(i.pos.xy);

                half3 accumColor = 0;
                float transmittance = 1.0;
                float lastT = 0.0;

                [loop]
                for (int j = 0; j < CLOUD_MAX_STEPS; j++)
                {
                    if (j >= dynamicSteps) break;

                    float normT_End = (float)(j + 1) * stepFraction;
                    float targetT   = normT_End * sqrt(normT_End) * rayLength;
                    float currentStep = targetT - lastT;

                    float jitteredNormT = ((float)j + dither) * stepFraction;
                    float t = jitteredNormT * sqrt(jitteredNormT) * rayLength;

                    if (t >= rayLength || transmittance < CLOUD_TRANSMITTANCE_MIN) break;

                    float3 pos = startPos + rayDir * t;
                    float distAlongRay = distToStart + t;
                    float lod = saturate(distAlongRay / 60000.0) * 4.0;

                    float heightInfo = (length(pos) - cloudBottomRad) / cloudThicknessMeters;
                    if (heightInfo < -0.02 || heightInfo > 1.02) { lastT = targetT; continue; }
                    heightInfo = saturate(heightInfo);

                    float2 weatherUV = pos.xz * CLOUD_WEATHER_FREQ * _CloudScale + (_CloudWind * 0.1) + weatherDrift;
                    float4 wData = tex2Dlod(_WeatherMapTex, float4(weatherUV, 0, lod));

                    float macro = smoothstep(1.0 - _CloudCoverage - 0.15, 1.0 - _CloudCoverage + 0.15, wData.r);
                    if (macro < 0.01) { lastT = targetT; continue; }

                    float detailFade = saturate(1.0 - (distAlongRay - 12000.0) / 10000.0);

                    CloudWeather weather;
                    weather.coverage = wData.r; weather.type = saturate(wData.g * 1.3 - 0.1);
                    weather.erosion = wData.b; weather.density = wData.a; weather.macro = macro;

                    float dens = SampleCloudDensityUnified(pos, heightInfo, lod, weather, true, detailFade);
                    if (dens < 0.001) { lastT = targetT; continue; }

                    float lightOpticalDepth = 0.0;
                    float totalLightDens = 0.0;

                    [unroll]
                    for (int k = 0; k < CLOUD_LIGHT_STEPS; k++)
                    {
                        float t_k = (float)k;
                        float t0 = cloudThicknessMeters * (t_k * t_k) / LIGHT_DENOM;
                        float t1 = cloudThicknessMeters * ((t_k + 1.0) * (t_k + 1.0)) / LIGHT_DENOM;
                        float lightDist = (t0 + t1) * 0.5;

                        float3 lightSamplePos = pos + mainLightDir * lightDist;
                        float lightH = (length(lightSamplePos) - cloudBottomRad) / cloudThicknessMeters;

                        if (lightH >= 0.0 && lightH <= 1.0)
                        {
                            float ld = SampleCloudDensityUnified(lightSamplePos, lightH, lod + 1.5, weather, false, 0.0);
                            totalLightDens += ld;
                            lightOpticalDepth += ld * (t1 - t0) * absorptionCoeff;
                        }
                    }

                    float avgLightDens = totalLightDens * 0.25;
                    float remainingThickness = cloudThicknessMeters * (5.0 / LIGHT_DENOM);
                    lightOpticalDepth += avgLightDens * remainingThickness * 0.4 * absorptionCoeff;

                    float pDotL = dot(pos, sunDir);
                    float pLenSq = dot(pos, pos);
                    float dMinSq = pLenSq - pDotL * pDotL;

                    float rayAltAboveGround = (pDotL < 0.0) ? (sqrt(max(0.0, dMinSq)) - CLOUD_PLANET_RADIUS) : 100000.0;
                    float earthShadow = smoothstep(-1500.0, 6500.0, rayAltAboveGround);

                    float msFactor = EvaluateMultiScattering(lightOpticalDepth, cloudCosTheta, sunWeight) * 0.75;
                    half3 directLight = activeLightColor * (msFactor * earthShadow);

                    if (cloudCosTheta > 0.90 && earthShadow > 0.6)
                    {
                        float sunBleach = smoothstep(0.90, 0.99, cloudCosTheta) * smoothstep(1.2, 2.5, msFactor);
                        directLight = lerp(directLight, float3(1.0, 0.96, 0.90) * length(directLight), sunBleach * 0.4);
                    }

                    float dayHeightGrad   = lerp(0.35, 1.0, heightInfo);
                    float nightHeightGrad = lerp(0.65, 1.0, heightInfo);
                    float heightGradient  = lerp(nightHeightGrad, dayHeightGrad, sunWeight);

                    float ambientOcclusion = lerp(0.40, 1.0, exp(-dens * 0.5));
                    half3 ambientLight = activeAmbient * (heightGradient * ambientOcclusion);

                    half3 cloudPointColor = _CloudColor.rgb * (directLight + ambientLight);

                    float stepOpticalDepth = dens * currentStep * absorptionCoeff;
                    float stepTransmittance = exp(-stepOpticalDepth);
                    float alphaStep = 1.0 - stepTransmittance;

                    accumColor   += cloudPointColor * alphaStep * transmittance;
                    transmittance *= stepTransmittance;

                    lastT = targetT;
                }

                float distFade = 1.0 - smoothstep(maxDist * 0.7, maxDist, distToStart);
                float horizonAlphaFade = (camOrigin.y > cloudBottomRad) ? 1.0 : smoothstep(-0.02, 0.08, rayDir.y);
                float totalFade = distFade * horizonAlphaFade;

                accumColor *= totalFade;
                transmittance = lerp(1.0, transmittance, totalFade);

                float finalAlpha = 1.0 - transmittance;
                
                float3 cloudColorLDR = accumColor;
                cloudColorLDR *= _Exposure;
                cloudColorLDR = saturate((cloudColorLDR * (2.51 * cloudColorLDR + 0.03)) / (cloudColorLDR * (2.43 * cloudColorLDR + 0.59) + 0.14));

                return fixed4(cloudColorLDR * finalAlpha, finalAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
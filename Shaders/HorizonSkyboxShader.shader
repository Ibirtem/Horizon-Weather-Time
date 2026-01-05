Shader "Horizon/Procedural Skybox"
{
    Properties
    {
        [Header(Stars)]
        [NoScaleOffset] _StarsTex ("Stars Texture (LatLong)", 2D) = "black" {}
        [HideInInspector] _StarsRotation ("Stars Rotation", Range(0.0, 360.0)) = 0.0
        [HideInInspector] _StarsFade ("Stars Fade", Range(0.0, 1.0)) = 1.0

        [Header(Stars Twinkle)]
        [HideInInspector] _TwinkleScale ("Twinkle Scale", Float) = 150.0
        [HideInInspector] _TwinkleDetail ("Twinkle Detail", Int) = 3
        [HideInInspector] _TwinkleSharpness ("Twinkle Sharpness", Float) = 5.0
        [HideInInspector] _TwinkleSpeed ("Twinkle Speed", Float) = 0.7
        [HideInInspector] _TwinkleStrength ("Twinkle Strength", Range(0.0, 2.0)) = 0.6

        [Header(Moon)]
        [NoScaleOffset] _MoonTex ("Moon Texture", 2D) = "white" {}
        _MoonColor ("Moon Color", Color) = (1, 1, 1, 1)
        _MoonSize ("Moon Size", Range(0.001, 0.1)) = 0.02
        [HideInInspector] _MoonPosition ("Moon Position", Vector) = (0.0, -0.5, 0.0, 0.0)

        [Header(Volumetric Clouds)]
        [NoScaleOffset] _CloudTex ("Cloud Noise (RGBA)", 2D) = "black" {}
        _CloudColor ("Cloud Lit Color", Color) = (0.9,0.9,0.9,1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.3,0.3,0.35,1)
        _CloudAltitude("Altitude Base (km)", Float) = 3.0
        _CloudScale ("Noise Scale", Float) = 1.0
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.5
        _CloudDensity ("Density Multiplier", Float) = 1.0
        _CloudDetail ("Erosion Amount", Range(0, 1)) = 0.5
        _CloudWisp ("Wispiness", Range(0, 1)) = 0.3
        _CloudScatter ("Light Absorption", Float) = 0.5
        [HideInInspector] _CloudWind ("Cloud Offset", Vector) = (0,0,0,0)

        [Header(Atmosphere)]
        [HideInInspector] _Turbidity ("Turbidity", Range(1.0, 10.0)) = 2.0
        [HideInInspector] _Rayleigh ("Rayleigh", Range(0.0, 5.0)) = 1.0
        [HideInInspector] _MieCoefficient ("Mie Coefficient", Range(0.0, 0.1)) = 0.005
        [HideInInspector] _MieDirectionalG ("Mie Directional G", Range(0.0, 1.0)) = 0.8
        [HideInInspector] _Exposure ("Exposure", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _SunPosition ("Sun Position", Vector) = (0.0, 0.5, 0.0, 0.0)
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
            
            // Stars
            sampler2D _StarsTex;
            float _StarsRotation;
            float _StarsFade;
            float _TwinkleScale;
            int _TwinkleDetail;
            float _TwinkleSharpness;
            float _TwinkleSpeed;
            float _TwinkleStrength;

            // Moon
            sampler2D _MoonTex;
            float4 _MoonColor;
            float _MoonSize;
            float3 _MoonPosition;

            // Clouds
            sampler2D _CloudTex;
            float4 _CloudColor, _CloudShadowColor;
            float _CloudAltitude, _CloudScale, _CloudCoverage, _CloudDensity;
            float _CloudDetail, _CloudWisp, _CloudScatter;
            float2 _CloudWind;

            // Atmosphere
            float _Turbidity;
            float _Rayleigh;
            float _MieCoefficient;
            float _MieDirectionalG;
            float _Exposure;
            float3 _SunPosition;

            #define PI 3.14159265358
            #define PI_2 1.57079632679
            #define E 2.71828182845

            static const float3 totalRayleigh = float3(5.804542996261093E-6, 1.3562911419845635E-5, 3.0265902468824876E-5);
            static const float3 MieConst = float3(1.8399918514433978E14, 2.7798023919660528E14, 4.0790479543861094E14);
            static const float cutoffAngle = 1.6110731556870734;
            static const float steepness = 1.5;
            static const float EE = 1000.0;
            static const float rayleighZenithLength = 8.4E3;
            static const float mieZenithLength = 1.25E3;
            static const float sunAngularDiameterCos = 0.9998;
            #define THREE_OVER_SIXTEENPI 0.05968310365946075
            #define ONE_OVER_FOURPI 0.07957747154594767

            // --- Noise Utils ---
            float3 hash(float3 p) {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            float noise(float3 p) {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(dot(hash(i + float3(0,0,0)), f - float3(0,0,0)), dot(hash(i + float3(1,0,0)), f - float3(1,0,0)), f.x),
                                 lerp(dot(hash(i + float3(0,1,0)), f - float3(0,1,0)), dot(hash(i + float3(1,1,0)), f - float3(1,1,0)), f.x), f.y),
                            lerp(lerp(dot(hash(i + float3(0,0,1)), f - float3(0,0,1)), dot(hash(i + float3(1,0,1)), f - float3(1,0,1)), f.x),
                                 lerp(dot(hash(i + float3(0,1,1)), f - float3(0,1,1)), dot(hash(i + float3(1,1,1)), f - float3(1,1,1)), f.x), f.y), f.z) * 0.5 + 0.5;
            }
            float fbm(float3 p, int octaves) {
                float value = 0.0; float amp = 0.5; float freq = 1.0;
                for (int i = 0; i < octaves; i++) { value += amp * noise(p * freq); amp *= 0.5; freq *= 2.0; }
                return value;
            }

            // --- Atmosphere Physics Utils ---
            float sunIntensity(float zenithAngleCos) {
                if (zenithAngleCos < -0.1) return 0.0;
                return EE * max(0.0, 1.0 - pow(E, -((cutoffAngle - acos(clamp(zenithAngleCos, -0.1, 1.0))) / steepness)));
            }
            float3 totalMie(float T) { return 0.434 * (0.2 * T) * 10E-18 * MieConst; }
            float rayleighPhase(float cosTheta) { return THREE_OVER_SIXTEENPI * (1.0 + cosTheta * cosTheta); }
            float hgPhase(float cosTheta, float g) {
                float g2 = g * g;
                return ONE_OVER_FOURPI * ((1.0 - g2) / pow(1.0 - 2.0 * g * cosTheta + g2, 1.5));
            }

            // --- Cloud Volumetrics ---
            float2 RaySphereIntersection(float3 rayOrigin, float3 rayDir, float sphereRadius) {
                float t = dot(-rayOrigin, rayDir);
                float3 p = rayOrigin + rayDir * t;
                float y = dot(p, p);
                if (y > sphereRadius * sphereRadius) return float2(-1, -1);
                float x = sqrt(sphereRadius * sphereRadius - y);
                return float2(t - x, t + x);
            }

            // Silver Lining Phase Function
            float DualHG(float costheta, float g1, float g2, float mix) {
                float p1 = (1.0 - g1 * g1) / (4.0 * PI * pow(1.0 + g1 * g1 - 2.0 * g1 * costheta, 1.5));
                float p2 = (1.0 - g2 * g2) / (4.0 * PI * pow(1.0 + g2 * g2 - 2.0 * g2 * costheta, 1.5));
                return lerp(p1, p2, mix);
            }

            float rand(float2 uv) {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float SampleCloudDensity(float3 p, float heightFraction) {
                float2 baseUV = p.xz * 0.00002 * _CloudScale + _CloudWind;
                
                float2 wobble = float2(cos(heightFraction * 3.0), sin(heightFraction * 3.0)) * 0.02;
                
                float baseShape = tex2Dlod(_CloudTex, float4(baseUV + wobble, 0, 0)).r;
                
                float2 erosionUV = baseUV + (float2(heightFraction, heightFraction * 0.9) * 0.2); 
                float4 erosionData = tex2Dlod(_CloudTex, float4(erosionUV, 0, 0));
                
                float erosion = erosionData.g;
                float detail = erosionData.b;

                float verticalProfile = saturate(4.0 * heightFraction * (1.0 - heightFraction));

                baseShape -= erosion * _CloudDetail * lerp(0.5, 1.0, heightFraction);
                baseShape -= detail * _CloudWisp * 0.5;

                float bottomFade = smoothstep(0.0, 0.15, heightFraction);
                baseShape *= bottomFade;

                float threshold = 1.0 - _CloudCoverage;
                
                float density = smoothstep(threshold - 0.2, threshold + 0.2, baseShape);
                
                return saturate(density * verticalProfile * _CloudDensity);
            }

            struct appdata { float4 vertex : POSITION; };
            struct v2f {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 sunDirection : TEXCOORD1;
                float  sunfade : TEXCOORD2;
                float3 betaR : TEXCOORD3;
                float3 betaM : TEXCOORD4;
                float  sunE : TEXCOORD5;
                float  haloVisibility : TEXCOORD6;
            };

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sunDirection = normalize(_SunPosition);

                float sunHeight = dot(o.sunDirection, float3(0,1,0));
                o.sunE = sunIntensity(sunHeight);
                o.sunfade = smoothstep(-0.12, 0.12, sunHeight);
                o.haloVisibility = smoothstep(-0.01, 0.15, sunHeight);

                float rayleighCoefficient = _Rayleigh - (1.0 * (1.0 - o.sunfade));
                o.betaR = totalRayleigh * rayleighCoefficient;
                o.betaM = totalMie(_Turbidity) * _MieCoefficient;
                return o;
            }

            // --- CONSTANTS ---
            #define STEPS 32
            #define PLANET_RADIUS 600000.0 
            #define CLOUD_THICKNESS 1500.0

            half4 frag(v2f i) : SV_Target {
                float3 up = float3(0,1,0);
                float3 direction = normalize(i.worldPos - _WorldSpaceCameraPos.xyz);

                // --- 1. Atmosphere Rendering ---
                float zenithAngle = acos(max(0.0, dot(up, direction)));
                float inv = 1.0 / (cos(zenithAngle) + 0.15 * pow(93.885 - ((zenithAngle * 180.0) / PI), -1.253));
                float sR = rayleighZenithLength * inv;
                float sM = mieZenithLength * inv;
                float3 Fex = exp(-(i.betaR * sR + i.betaM * sM));
                float cosTheta = dot(direction, i.sunDirection);
                float3 Lin = pow(i.sunE * ((i.betaR * rayleighPhase(cosTheta*0.5+0.5) + i.betaM * hgPhase(cosTheta, _MieDirectionalG)) / (i.betaR + i.betaM)) * (1.0 - Fex), 1.5);
                Lin *= lerp(1.0, pow(i.sunE * ((i.betaR * rayleighPhase(cosTheta*0.5+0.5) + i.betaM * hgPhase(cosTheta, _MieDirectionalG)) / (i.betaR + i.betaM)) * Fex, 0.5), clamp(pow(1.0 - dot(up, i.sunDirection), 5.0), 0.0, 1.0));
                
                float sundisk = smoothstep(sunAngularDiameterCos, sunAngularDiameterCos + 0.00001, cosTheta);
                float3 L0 = float3(0.001, 0.001, 0.001) * Fex + (i.sunE * 6000.0 * Fex) * sundisk * i.haloVisibility;
                Lin *= i.sunfade;
                float3 skyColor = (Lin + L0) * 0.04 + float3(0.0, 0.00005, 0.00005);
                float3 finalColor = pow(skyColor, 1.0 / (1.2 + (1.2 * i.sunfade)));

                // --- 2. Stars ---
                if (_StarsFade > 0.0) {
                    float rotRad = _StarsRotation * (PI / 180.0);
                    float s = sin(rotRad); float c = cos(rotRad);
                    float3 rotatedDir = direction;
                    float oldX = rotatedDir.x;
                    rotatedDir.x = oldX * c - rotatedDir.z * s;
                    rotatedDir.z = oldX * s + rotatedDir.z * c;

                    float2 uv = float2(0.5 + atan2(rotatedDir.z, rotatedDir.x) / (2 * PI), 0.5 - asin(rotatedDir.y) / PI);
                    float3 starCol = tex2D(_StarsTex, uv).rgb;
                    
                    float softNoise = fbm(rotatedDir * _TwinkleScale + float3(0, 0, _Time.w * _TwinkleSpeed), _TwinkleDetail);
                    float halfWidth = 0.5 / _TwinkleSharpness;
                    float twinkleMask = smoothstep(0.5 - halfWidth, 0.5 + halfWidth, softNoise) * _TwinkleStrength;
                    
                    finalColor += starCol * (1.0 - twinkleMask) * _StarsFade;
                }

                // --- 3. Moon ---
                float3 moonDir = normalize(_MoonPosition);
                float3 moonRight = normalize(cross(float3(0,1,0), moonDir + float3(0.00001,0,0)));
                float3 moonUp = cross(moonDir, moonRight);
                
                float x = dot(direction, moonRight);
                float y = dot(direction, moonUp);
                float2 planarUV = float2(x, y) / _MoonSize;
                
                if (dot(direction, moonDir) > 0.0) {
                    float r2 = dot(planarUV, planarUV);
                    if (r2 < 1.0) {
                        float z = sqrt(1.0 - r2);
                        float3 sphereNormal = float3(planarUV.x, planarUV.y, z);
                        float2 sphUV;
                        sphUV.x = 0.5 + atan2(sphereNormal.x, sphereNormal.z) / (2.0 * PI);
                        sphUV.y = 0.5 + asin(sphereNormal.y) / PI;

                        float4 moonTex = tex2Dlod(_MoonTex, float4(sphUV, 0, 0));
                        float3 moonVisual = moonTex.rgb * _MoonColor.rgb;
                        float edgeSoftness = smoothstep(1.0, 0.96, r2);

                        float moonSunDot = dot(moonDir, i.sunDirection);
                        float eclipseFactor = smoothstep(0.999, 1.0, moonSunDot);
                        float finalAlpha = max(edgeSoftness * moonTex.a * _MoonColor.a, eclipseFactor);
                        finalColor = lerp(finalColor, moonVisual, finalAlpha); 
                    }
                }

                // --- 4. VOLUMETRIC CLOUDS (RAYMARCHED) ---
                if (direction.y > 0.01)
                {
                    float cloudBottomRad = PLANET_RADIUS + (_CloudAltitude * 1000.0);
                    float cloudTopRad = cloudBottomRad + CLOUD_THICKNESS;
                    
                    float3 planetCenter = float3(0, -PLANET_RADIUS, 0);
                    float3 camPos = float3(0, 10, 0); 
                    float3 camOrigin = camPos - planetCenter;
                    
                    float2 hitBottom = RaySphereIntersection(camOrigin, direction, cloudBottomRad);
                    float2 hitTop = RaySphereIntersection(camOrigin, direction, cloudTopRad);
                    
                    if (hitTop.y > 0) {
                        float distToStart = max(0, hitBottom.y);
                        float distToEnd = hitTop.y;
                        
                        float maxDist = 60000.0; 
                        if (distToStart <= maxDist) {
                            float rayLength = min(distToEnd - distToStart, maxDist);
                            float stepSize = rayLength / float(STEPS);
                            float3 startPos = direction * distToStart; 

                            float dither = rand(i.vertex.xy * _Time.y);
                            startPos += direction * stepSize * dither;
                            
                            // --- LIGHTING ---
                            float sunH = dot(i.sunDirection, up);
                            float sunWeight = smoothstep(-0.04, 0.08, sunH); 
                            float moonH = dot(moonDir, up);
                            float moonWeight = smoothstep(-0.1, 0.1, moonH);

                            float3 mainLightDir = lerp(moonDir, i.sunDirection, sunWeight);
                            
                            float3 sunLightColor = lerp(float3(1.0, 0.6, 0.3), float3(1.0, 1.0, 1.0), sunWeight) * lerp(0.0, 5.0, sunWeight);
                            float3 moonLightColor = _MoonColor.rgb * 0.008 * moonWeight;

                            float3 activeLightColor = lerp(moonLightColor, sunLightColor, sunWeight);
                            
                            float3 atmosphereTint = min(finalColor, float3(0.8, 0.8, 0.8));

                            float3 dayAmbient = lerp(_CloudShadowColor.rgb, atmosphereTint, 0.6); 
                            
                            float3 nightAmbient = lerp(_CloudShadowColor.rgb * 0.05, atmosphereTint * 0.5, 0.5); 
                            
                            float3 activeAmbient = lerp(nightAmbient, dayAmbient, sunWeight);

                            float activeCosTheta = dot(direction, mainLightDir);
                            float phaseVal = DualHG(activeCosTheta, 0.6, -0.3, 0.7); 
                            
                            float3 accumColor = 0;
                            float transmittance = 1.0;
                            
                            [loop]
                            for (int j = 0; j < STEPS; j++) {
                                float3 pos = startPos + direction * (stepSize * j);
                                float heightInfo = (length(pos - planetCenter) - cloudBottomRad) / CLOUD_THICKNESS;
                                
                                if(heightInfo < 0 || heightInfo > 1) continue;

                                float dens = SampleCloudDensity(pos, heightInfo);
                                
                                if (dens > 0.001) {
                                    float3 lightOffset = mainLightDir * (40.0 / _CloudScale); 
                                    float3 lightPos = pos + lightOffset;
                                    float lightHeightInfo = heightInfo + (lightOffset.y / CLOUD_THICKNESS);
                                    
                                    float lightDens = 0.0;
                                    if(lightHeightInfo > 0.0 && lightHeightInfo < 1.0)
                                    {
                                         lightDens = SampleCloudDensity(lightPos, lightHeightInfo);
                                    }

                                    float densityMod = lerp(1.0, 0.5, sunWeight);
                                    float effectiveLightDens = lightDens * densityMod;

                                    float lightAtten = 1.0 / (1.0 + effectiveLightDens * _CloudScatter * 1.0);
                                    float heightAmbient = lerp(0.5, 1.0, heightInfo); 
                                    float lookAtSun = dot(direction, mainLightDir);
                                    float backlightFactor = smoothstep(0.0, 1.0, lookAtSun);
                                    float finalPhase = max(phaseVal, 0.6) * (1.0 + backlightFactor * 0.5);

                                    // "General" cloud illumination 
                                    // lerp(night, day, ...)
                                    float baseAdd = lerp(0.1, 0.4, sunWeight);
                                    float3 litColor = _CloudColor.rgb * activeLightColor * (lightAtten + baseAdd) * finalPhase * heightAmbient;

                                    float3 mixedShadowColor = lerp(_CloudShadowColor.rgb, activeAmbient, 0.25);
                                    float shadowBrightness = lerp(0.01, 3.5, sunWeight); 
                                    float3 shadowColor = mixedShadowColor * shadowBrightness * heightAmbient;

                                    float3 cloudPointColor = lerp(shadowColor, litColor, lightAtten);

                                    float viewAbsorb = exp(-dens * stepSize * 0.01 * _CloudDensity); 
                                    float alphaStep = (1.0 - viewAbsorb);
                                    
                                    accumColor += cloudPointColor * alphaStep * transmittance;
                                    transmittance *= viewAbsorb;
                                    
                                    if (transmittance < 0.01) break; 
                                }
                            }

                            // Horizon Fade
                            float distFade = 1.0 - smoothstep(maxDist * 0.7, maxDist, distToStart);
                            float horizonAlphaFade = smoothstep(0.0, 0.1, direction.y);
                            
                            float totalFade = distFade * horizonAlphaFade;

                            accumColor *= totalFade;
                            transmittance = lerp(1.0, transmittance, totalFade); 

                            finalColor = finalColor * transmittance + accumColor;
                        }
                    }
                }

                finalColor *= _Exposure;
                return half4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Skybox/Procedural"
}
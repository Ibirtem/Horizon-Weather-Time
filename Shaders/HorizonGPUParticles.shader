Shader "Horizon/GPU Particles"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _SystemLightingColor ("System Lighting Color", Color) = (1,1,1,1)
        
        [Header(Volume Settings)]
        _HorizonBounds ("Volume Size (XYZ)", Vector) = (40, 40, 40, 0)
        _HorizonParticleSize ("Particle Size", Float) = 0.03
        _Stretch ("Vertical Stretch", Float) = 1.0
        _HorizonDensity ("Density (0 to 1)", Range(0.0, 1.0)) = 1.0

        [Header(Occlusion)]
        [NoScaleOffset] _WeatherOcclusionTex ("Occlusion Depth", 2D) = "white" {}
        [HideInInspector] _OcclusionCamPos ("Cam Pos", Vector) = (0,0,0,0)
        [HideInInspector] _OcclusionOrthoSize ("Ortho Size", Float) = 20.0
        [HideInInspector] _OcclusionFarClip ("Far Clip", Float) = 120.0

        [Header(Physics)]
        _Wind ("Wind & Gravity", Vector) = (1, -1.5, 0.5, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "UnityCG.cginc"

            sampler2D _WeatherOcclusionTex;
            float4 _WeatherOcclusionTex_TexelSize;
            float3 _OcclusionCamPos;
            float _OcclusionOrthoSize;
            float _OcclusionFarClip;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half4 _SystemLightingColor;
            
            float3 _HorizonBounds;
            float _HorizonParticleSize;
            float _Stretch;
            float _HorizonDensity;
            float3 _Wind;

            inline float3 wrap_pos(float3 pos, float3 bounds) 
            {
                return (frac(pos / bounds) * bounds); 
            }

            #define CULL_VERTEX(o) { o.vertex = float4(0,0,0,0); o.uv = 0; o.color = 0; return o; }

            v2f vert (appdata v)
            {
                v2f o;
                
                if (v.uv1.w > _HorizonDensity) CULL_VERTEX(o);

                float3 camPos = _WorldSpaceCameraPos;
                
                float3 localPos = v.uv1.xyz * _HorizonBounds + (_Wind * _Time.y);
                float3 halfBounds = _HorizonBounds * 0.5;
                
                float3 posRelToCam = localPos - camPos;
                
                float3 wrappedOffset = (frac((posRelToCam + halfBounds) / _HorizonBounds) * _HorizonBounds) - halfBounds;
                
                float3 particleWorldCenter = camPos + wrappedOffset;

                float3 toPart = particleWorldCenter - camPos;

                float zDist = dot(toPart, -UNITY_MATRIX_V[2].xyz); 

                if (zDist < 0.1) CULL_VERTEX(o);

                float2 occUV;
                float invOrthoSize = 1.0 / (_OcclusionOrthoSize * 2.0);
                
                occUV.x = (particleWorldCenter.x - _OcclusionCamPos.x) * invOrthoSize + 0.5;
                occUV.y = (particleWorldCenter.z - _OcclusionCamPos.z) * invOrthoSize + 0.5;

                if (occUV.x > 0.005 && occUV.x < 0.995 && 
                    occUV.y > 0.005 && occUV.y < 0.995)
                {
                    half linearDepth = tex2Dlod(_WeatherOcclusionTex, float4(occUV, 0, 0)).r;
                    
                    if (linearDepth < 0.99)
                    {
                        float surfaceWorldY = _OcclusionCamPos.y - (linearDepth * _OcclusionFarClip);
                        
                        if (particleWorldCenter.y < (surfaceWorldY - 0.2))
                        {
                            CULL_VERTEX(o);
                        }
                    }
                }

                float windLen = length(_Wind);
                float3 fallDir = windLen > 0.001 ? (_Wind / windLen) : float3(0, -1, 0);
                
                float3 camToParticle = normalize(particleWorldCenter - camPos);

                float3 sideDir = cross(fallDir, camToParticle);
                float sideLen = length(sideDir);
                
                if (sideLen < 0.001) 
                {
                    sideDir = float3(1, 0, 0);
                } 
                else 
                {
                    sideDir /= sideLen;
                }

                float2 quadOffset = v.uv0 - 0.5;
                
                float3 vertexWorldPos = particleWorldCenter 
                                      + sideDir * quadOffset.x * _HorizonParticleSize 
                                      + fallDir * quadOffset.y * _HorizonParticleSize * _Stretch;

                o.vertex = mul(UNITY_MATRIX_VP, float4(vertexWorldPos, 1.0));
                
                o.uv = TRANSFORM_TEX(v.uv0, _MainTex);
                
                float3 distFromCenter = abs(wrappedOffset) / halfBounds;
                float maxDist = max(max(distFromCenter.x, distFromCenter.y), distFromCenter.z);
                
                half edgeFade = 1.0 - smoothstep(0.75, 1.0, maxDist);

                o.color = _Color * _SystemLightingColor;
                o.color.a *= edgeFade;

                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv) * i.color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
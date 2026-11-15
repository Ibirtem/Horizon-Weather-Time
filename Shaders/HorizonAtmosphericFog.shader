Shader "Horizon/Atmospheric Fog"
{
    // Rendered after all opaque geometry (Transparent-60 = queue 2940) but before
    // HorizonWorldClouds (Transparent-50 = 2950) and regular transparency (3000),
    // so water/glass keep their own blend order and never get double-fogged.
    SubShader
    {
        Tags { "Queue"="Transparent-60" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Includes/HorizonFogCommon.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 rayVec    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // Proxy cube centered on current camera (works in VR eyes, SceneView, Mirrors)
                float3 worldPos = _WorldSpaceCameraPos.xyz + v.vertex.xyz * 10.0;
                o.pos       = UnityWorldToClipPos(worldPos);
                o.screenPos = ComputeScreenPos(o.pos);
                o.rayVec    = worldPos - _WorldSpaceCameraPos.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Skip orthographic cameras (e.g. minimaps / occlusion cams) and disabled fog
                if (unity_OrthoParams.w > 0.5 || _UdonHorizonFogHorizonColor.a < 0.5)
                    discard;

                float2 uv = i.screenPos.xy / max(1e-5, i.screenPos.w);
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);

                #if defined(UNITY_REVERSED_Z)
                bool isSky = (rawDepth <= 0.0000001);
                #else
                bool isSky = (rawDepth >= 0.9999999);
                #endif

                // Skybox and clouds handle sky fog via HorizonFogCommon.cginc
                if (isSky) discard;

                float sceneDepth = LinearEyeDepth(rawDepth);
                float3 hitWorldPos = _WorldSpaceCameraPos.xyz + i.rayVec * (sceneDepth / max(1e-5, i.screenPos.w));

                float3 fogColor;
                float  fogAlpha = HorizonEvaluateFog(_WorldSpaceCameraPos.xyz, hitWorldPos, fogColor);

                if (fogAlpha <= 0.002) discard;

                return fixed4(fogColor, fogAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}

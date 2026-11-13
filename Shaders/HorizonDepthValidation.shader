Shader "Horizon/Debug/DepthValidation"
{
    Properties
    {
        _SplitScreen ("Split Screen (0 = Fullscreen, 0.5 = Half)", Range(0.0, 1.0)) = 0.5
        _MaxDistance ("Max Distance Metric (Meters)", Float) = 100.0
    }
    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float _SplitScreen;
            float _MaxDistance;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            /**
             * Renders a false-color depth ramp verifying _CameraDepthTexture validity in Single Pass Stereo.
             *
             * @param i Interpolated vertex data with SPI stereo eye index.
             * @return RGBA color representing linear scene depth.
             */
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.screenPos.xy / max(1e-5, i.screenPos.w);

                // Discard pixels on the right side if split-screen is active
                if (_SplitScreen > 0.01 && uv.x > (1.0 - _SplitScreen))
                {
                    discard;
                }

                // Sample hardware depth buffer using VR-aware macro
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float linearDist = LinearEyeDepth(rawDepth);

                // Skybox or unwritten depth (returns raw depth 0.0 on reverse-Z or 1.0 on standard)
                #if defined(UNITY_REVERSED_Z)
                bool isSky = (rawDepth <= 0.00001);
                #else
                bool isSky = (rawDepth >= 0.99999);
                #endif

                if (isSky)
                {
                    // Skybox: dark magenta indicator
                    return fixed4(0.15, 0.0, 0.15, 0.8);
                }

                // Metric false-color gradient:
                // 0m - 2m:   RED     (Near clipping / Hands / Cockpit)
                // 2m - 10m:  GREEN   (Near terrain / Furniture / Avatars)
                // 10m - 50m: BLUE    (Mid-range structures)
                // 50m - Max: WHITE   (Distant mountains / Geometry)
                fixed3 color = fixed3(0, 0, 0);

                if (linearDist < 2.0)
                {
                    color = lerp(fixed3(1.0, 0.0, 0.0), fixed3(1.0, 0.8, 0.0), linearDist / 2.0);
                }
                else if (linearDist < 10.0)
                {
                    color = lerp(fixed3(1.0, 0.8, 0.0), fixed3(0.0, 1.0, 0.0), (linearDist - 2.0) / 8.0);
                }
                else if (linearDist < 50.0)
                {
                    color = lerp(fixed3(0.0, 1.0, 0.0), fixed3(0.0, 0.4, 1.0), (linearDist - 10.0) / 40.0);
                }
                else
                {
                    float t = saturate((linearDist - 50.0) / max(1.0, _MaxDistance - 50.0));
                    color = lerp(fixed3(0.0, 0.4, 1.0), fixed3(1.0, 1.0, 1.0), t);
                }

                return fixed4(color, 0.85);
            }
            ENDCG
        }
    }
    FallBack Off
}
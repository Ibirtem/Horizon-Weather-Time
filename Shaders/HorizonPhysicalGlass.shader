Shader "Horizon/Physical Glass"
{
    Properties
    {
        [Header(Optical Absorption)]
        _AbsorptionTint ("Absorption Color Tint", Color) = (0.82, 0.94, 0.90, 1.0)
        _Thickness ("Wall Physical Thickness", Range(0.01, 3.0)) = 0.25

        [Header(Refraction and Dispersion)]
        _RefractionDistortion ("Refraction Strength", Range(0.0, 0.2)) = 0.04
        [Toggle(_DEPTH_REJECTION_ON)] _DepthRejection ("Foreground Halo Rejection", Float) = 1
        [Toggle(_DISPERSION_ON)] _EnableDispersion ("Chromatic Dispersion (3x Grab)", Float) = 0
        _Dispersion ("Cauchy Dispersion Spread", Range(0.0, 0.1)) = 0.025

        [Header(Surface and Reflection)]
        _Smoothness ("Surface Smoothness", Range(0.0, 1.0)) = 0.98
        _FresnelF0 ("Dielectric Base Reflectivity F0", Range(0.01, 0.2)) = 0.04
        _SpecularTint ("Direct Specular Tint", Color) = (1.0, 1.0, 1.0, 1.0)

        [Header(Thin Film Iridescence)]
        [Toggle(_IRIDESCENCE_ON)] _EnableIridescence ("Enable Thin Film Iridescence", Float) = 0
        _IridescenceStrength ("Iridescence Intensity", Range(0.0, 1.0)) = 0.1
        _IridescenceFrequency ("Spectral Phase Frequency", Range(1.0, 15.0)) = 5.0

        [Header(Microfacet Normals)]
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Map Scale", Range(0.0, 2.0)) = 0.5

        [Header(Rasterizer State)]
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Culling Mode", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Lighting.cginc"
    #include "AutoLight.cginc"
    #include "Includes/HorizonFogCommon.cginc"

    fixed4 _AbsorptionTint;
    half _Thickness;
    half _RefractionDistortion;
    half _Dispersion;
    half _Smoothness;
    half _FresnelF0;
    fixed4 _SpecularTint;
    half _IridescenceStrength;
    half _IridescenceFrequency;
    sampler2D _NormalMap;
    float4 _NormalMap_ST;
    half _NormalStrength;

    /**
     * Computes physical optical path length through the glass shell,
     * clamped at grazing angles to prevent solid dark outlines.
     *
     * @param thickness Base thickness of the glass pane.
     * @param absNdotV Absolute cosine between normal and view direction.
     * @return Effective optical traversal distance.
     */
    inline float CalculateOpticalDepth(float thickness, float absNdotV)
    {
        return thickness / max(absNdotV, 0.25);
    }

    /**
     * Evaluates Beer-Lambert spectral absorption across RGB channels.
     *
     * @param baseColor Linear transmission tint at unit thickness.
     * @param opticalDepth Effective traversal length.
     * @return Spectral transmittance factor in [0, 1].
     */
    inline float3 EvaluateBeerLambert(float3 baseColor, float opticalDepth)
    {
        return pow(saturate(baseColor), opticalDepth);
    }

    /**
     * Computes thin-film interference spectral phase shift (Belcour-Barla style).
     *
     * @param NdotV Cosine between surface normal and view direction.
     * @param frequency Angular spectral oscillation frequency.
     * @param intensity Blend factor between monochrome and spectral Fresnel.
     * @return Spectral RGB reflectance multiplier.
     */
    inline float3 EvaluateThinFilmIridescence(float NdotV, float frequency, float intensity)
    {
        float phase = (1.0 - saturate(NdotV)) * frequency;
        float3 spectralPhase = 0.5 + 0.5 * cos(phase + float3(0.0, 2.094, 4.188));
        return lerp(float3(1.0, 1.0, 1.0), spectralPhase, intensity);
    }

    /**
     * Applies box projection to reflection vectors when a Reflection Probe supports it.
     *
     * @param reflectDir World-space reflection vector.
     * @param worldPos World-space surface position.
     * @return Box-corrected cubemap sampling direction.
     */
    inline float3 BoxProjectReflectDir(float3 reflectDir, float3 worldPos)
    {
    #if defined(UNITY_SPECCUBE_BOX_PROJECTION)
        if (unity_SpecCube0_ProbePosition.w > 0.0)
        {
            float3 nrdir = normalize(reflectDir);
            float3 rbmax = (unity_SpecCube0_BoxMax.xyz - worldPos) / nrdir;
            float3 rbmin = (unity_SpecCube0_BoxMin.xyz - worldPos) / nrdir;
            float3 rbminmax = (nrdir > 0.0) ? rbmax : rbmin;
            float fa = min(min(rbminmax.x, rbminmax.y), rbminmax.z);
            float3 posOnBox = worldPos + nrdir * fa;
            reflectDir = posOnBox - unity_SpecCube0_ProbePosition.xyz;
        }
    #endif
        return reflectDir;
    }

    /**
     * Evaluates HDR environment reflections from the active Reflection Probe with
     * perceptual roughness mipmapping and energy conservation.
     *
     * @param reflectDir World-space reflection direction.
     * @param worldPos World-space surface position for box projection.
     * @param perceptualRoughness Linear perceptual roughness (1 - smoothness).
     * @return Decoded HDR environment reflection color.
     */
    inline float3 EvaluateEnvironmentReflection(float3 reflectDir, float3 worldPos, float perceptualRoughness)
    {
        float3 dir = BoxProjectReflectDir(reflectDir, worldPos);
        float mipLevel = perceptualRoughness * (1.7 - 0.7 * perceptualRoughness) * 6.0;
        half4 rgbm = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, dir, mipLevel);
        float3 env = DecodeHDR(rgbm, unity_SpecCube0_HDR);

        float roughness2 = perceptualRoughness * perceptualRoughness;
        float surfaceReduction = 1.0 / (roughness2 * roughness2 + 1.0);
        return env * surfaceReduction;
    }

    /**
     * Evaluates Trowbridge-Reitz GGX microfacet specular term with Smith visibility
     * and NdotL incidence gating, avoiding sub-pixel aliasing in VR.
     *
     * @param NdotL Clamped cosine between normal and light direction.
     * @param NdotV Clamped cosine between normal and view direction.
     * @param NdotH Clamped cosine between normal and half-vector.
     * @param perceptualRoughness Linear perceptual roughness (1 - smoothness).
     * @return Normalized GGX specular radiance factor.
     */
    inline float EvaluateGGXSpecular(float NdotL, float NdotV, float NdotH, float perceptualRoughness)
    {
        if (NdotL <= 0.0001) return 0.0;

        float a = max(perceptualRoughness * perceptualRoughness, 0.004);
        float a2 = a * a;

        float d = (NdotH * a2 - NdotH) * NdotH + 1.0;
        float D = a2 / max(UNITY_PI * d * d, 1e-7);

        float visV = NdotL * (NdotV * (1.0 - a) + a);
        float visL = NdotV * (NdotL * (1.0 - a) + a);
        float V = 0.5 / max(visV + visL, 1e-5);

        return min(D * V * NdotL * UNITY_PI, 64.0);
    }

    /**
     * Reconstructs world-space surface normal with safe tangent handling and double-sided orientation.
     *
     * @param uv Normal map UV coordinates.
     * @param rawNormal Interpolated geometric world normal.
     * @param rawTangent Interpolated world tangent.
     * @param rawBitangent Interpolated world bitangent.
     * @param facing VFACE fragment sign.
     * @return Normalized world-space shading normal.
     */
    inline float3 ResolveWorldNormal(float2 uv, float3 rawNormal, float3 rawTangent, float3 rawBitangent, fixed facing)
    {
        float faceSign = (facing >= 0.0) ? 1.0 : -1.0;
        float3 baseNormal = normalize(rawNormal) * faceSign;

        float tLenSq = dot(rawTangent, rawTangent);
        float bLenSq = dot(rawBitangent, rawBitangent);
        if (tLenSq < 1e-6 || bLenSq < 1e-6) return baseNormal;

        float3 tangent = rawTangent * rsqrt(tLenSq);
        float3 bitangent = rawBitangent * rsqrt(bLenSq);
        float3x3 tbn = float3x3(tangent, bitangent, baseNormal);

        half3 normalMapSample = UnpackNormal(tex2D(_NormalMap, uv));
        normalMapSample.xy *= _NormalStrength;
        return normalize(mul(normalMapSample, tbn));
    }
    ENDCG

    // =========================================================================
    // SUBSHADER 1 (LOD 200): Full Physical Refraction + Depth Halo Rejection
    // =========================================================================
    SubShader
    {
        LOD 200
        Tags
        {
            // Renders after HorizonAtmosphericFog (2940) and HorizonWorldClouds (2950),
            // but before HorizonGPUParticles (3000) so weather drops fly in front of glass.
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        GrabPass
        {
            "_GlassGrab"
        }

        Cull [_CullMode]
        ZWrite Off
        Blend One Zero

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing
            #pragma shader_feature_local _DEPTH_REJECTION_ON
            #pragma shader_feature_local _DISPERSION_ON
            #pragma shader_feature_local _IRIDESCENCE_ON

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_GlassGrab);
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos            : SV_POSITION;
                float3 uvAndEyeDepth  : TEXCOORD0;
                float3 worldNormal    : TEXCOORD1;
                float3 worldPos       : TEXCOORD2;
                float4 grabPos        : TEXCOORD3;
                float4 screenPos      : TEXCOORD4;
                float3 worldTangent   : TEXCOORD5;
                float3 worldBitangent : TEXCOORD6;
                UNITY_SHADOW_COORDS(7)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /**
             * Clamps screen-space UVs safely inside the active eye bounds in Single-Pass Stereo.
             *
             * @param uv Screen-space UV coordinates.
             * @return Clamped UV coordinates inside [0, 1] or current stereo eye viewport.
             */
            inline float2 ClampStereoScreenUV(float2 uv)
            {
                uv = saturate(uv);
            #if defined(UNITY_SINGLE_PASS_STEREO)
                uv = UnityStereoClamp(uv, unity_StereoScaleOffset[unity_StereoEyeIndex]);
            #endif
                return uv;
            }

            /**
             * Samples the shared GrabPass buffer in Single-Pass Stereo Instanced mode,
             * optionally evaluating 3-wavelength Cauchy chromatic dispersion.
             *
             * @param baseUV Projected grab-pass screen UV.
             * @param offset Screen-space refractive displacement.
             * @param dispersion Dispersion spread factor.
             * @return Refracted background RGB color.
             */
            inline float3 SampleGrabBackground(float2 baseUV, float2 offset, float dispersion)
            {
            #if defined(_DISPERSION_ON)
                float2 uvR = ClampStereoScreenUV(baseUV + offset * (1.0 - dispersion));
                float2 uvG = ClampStereoScreenUV(baseUV + offset);
                float2 uvB = ClampStereoScreenUV(baseUV + offset * (1.0 + dispersion));

                float r = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GlassGrab, uvR).r;
                float g = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GlassGrab, uvG).g;
                float b = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GlassGrab, uvB).b;
                return float3(r, g, b);
            #else
                float2 uv = ClampStereoScreenUV(baseUV + offset);
                return UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GlassGrab, uv).rgb;
            #endif
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * (v.tangent.w * unity_WorldTransformParams.w);

                o.uvAndEyeDepth.xy = TRANSFORM_TEX(v.uv, _NormalMap);
                COMPUTE_EYEDEPTH(o.uvAndEyeDepth.z);

                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.screenPos = ComputeScreenPos(o.pos);
                TRANSFER_SHADOW(o);

                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 worldNormal = ResolveWorldNormal(
                    i.uvAndEyeDepth.xy, i.worldNormal, i.worldTangent, i.worldBitangent, facing);
                float glassEyeDepth = i.uvAndEyeDepth.z;

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float rawNdotV = dot(worldNormal, viewDir);
                float NdotV = saturate(rawNdotV);
                float absNdotV = max(abs(rawNdotV), 0.001);

                // 1. Beer-Lambert spectral absorption
                float opticalDepth = CalculateOpticalDepth(_Thickness, absNdotV);
                float3 transmittance = EvaluateBeerLambert(_AbsorptionTint.rgb, opticalDepth);

                // 2. View-space refraction with foreground depth halo rejection
                float2 grabUV = i.grabPos.xy / max(i.grabPos.w, 1e-5);
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, worldNormal);
                float2 viewRefract = viewNormal.xy * (_RefractionDistortion / (1.0 + glassEyeDepth * 0.5));

            #if defined(UNITY_SINGLE_PASS_STEREO)
                viewRefract.x *= 0.5;
            #endif

            #if UNITY_UV_STARTS_AT_TOP
                float2 grabOffset = float2(viewRefract.x, -viewRefract.y);
            #else
                float2 grabOffset = viewRefract;
            #endif

            #if defined(_DEPTH_REJECTION_ON)
                float2 depthUV = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                float2 depthOffset = float2(viewRefract.x, viewRefract.y * _ProjectionParams.x);
                float2 shiftedDepthUV = ClampStereoScreenUV(depthUV + depthOffset);

                float rawShiftedDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, shiftedDepthUV);
                float shiftedSceneDepth = LinearEyeDepth(rawShiftedDepth);

                // Zero out refraction if the shifted sample hits geometry in front of the glass
                float validRefraction = smoothstep(-0.02, 0.08, shiftedSceneDepth - glassEyeDepth);
                grabOffset *= validRefraction;
            #endif

                float3 background = SampleGrabBackground(grabUV, grabOffset, _Dispersion);
                float3 transmittedLight = background * transmittance;

                // 3. Schlick Fresnel + optional thin-film iridescence
                float fresnel = _FresnelF0 + (1.0 - _FresnelF0) * pow(1.0 - NdotV, 5.0);
                float3 specularFresnel = float3(fresnel, fresnel, fresnel);
            #if defined(_IRIDESCENCE_ON)
                specularFresnel *= EvaluateThinFilmIridescence(NdotV, _IridescenceFrequency, _IridescenceStrength);
            #endif

                // 4. Environment probe IBL reflection (box-projected)
                float perceptualRoughness = 1.0 - _Smoothness;
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float3 envReflection = EvaluateEnvironmentReflection(reflectDir, i.worldPos, perceptualRoughness) * specularFresnel;

                // 5. GGX direct specular with NdotL and real-time shadow attenuation
                float3 lightDir = Unity_SafeNormalize(UnityWorldSpaceLightDir(i.worldPos));
                float3 halfVec = Unity_SafeNormalize(lightDir + viewDir);
                float NdotL = saturate(dot(worldNormal, lightDir));
                float NdotH = saturate(dot(worldNormal, halfVec));
                float LdotH = saturate(dot(lightDir, halfVec));

                float directFresnel = _FresnelF0 + (1.0 - _FresnelF0) * pow(1.0 - LdotH, 5.0);
                float directSpec = EvaluateGGXSpecular(NdotL, NdotV, NdotH, perceptualRoughness);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 directSpecularColor = _LightColor0.rgb * (directSpec * directFresnel * atten) * _SpecularTint.rgb;

                // 6. Energy-conserving composite
                float3 glassComposite = transmittedLight * saturate(1.0 - specularFresnel) + envReflection + directSpecularColor;

                // 7. Horizon Atmospheric Fog integration:
                // Since `background` from GrabPass already contains background fog,
                // blend from `background` toward `glassComposite` by `(1 - fogAlpha)` to
                // avoid double-fogging the scenery behind windows while fading tint/reflections.
                float3 horizonFogCol;
                float fogAlpha = HorizonEvaluateFog(_WorldSpaceCameraPos.xyz, i.worldPos, horizonFogCol);
                float3 finalRGB = lerp(background, glassComposite, 1.0 - fogAlpha);

                return fixed4(finalRGB, 1.0);
            }
            ENDCG
        }
    }

    // =========================================================================
    // SUBSHADER 2 (LOD 100): Fast Mobile / Quest Fallback (No GrabPass)
    // =========================================================================
    SubShader
    {
        LOD 100
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull [_CullMode]
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardBaseMobile"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vertMobile
            #pragma fragment fragMobile
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing

            struct appdataMobile
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2fMobile
            {
                float4 pos            : SV_POSITION;
                float2 uv             : TEXCOORD0;
                float3 worldNormal    : TEXCOORD1;
                float3 worldPos       : TEXCOORD2;
                float3 worldTangent   : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                UNITY_SHADOW_COORDS(5)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2fMobile vertMobile(appdataMobile v)
            {
                v2fMobile o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2fMobile, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * (v.tangent.w * unity_WorldTransformParams.w);
                o.uv = TRANSFORM_TEX(v.uv, _NormalMap);
                TRANSFER_SHADOW(o);

                return o;
            }

            fixed4 fragMobile(v2fMobile i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 worldNormal = ResolveWorldNormal(
                    i.uv, i.worldNormal, i.worldTangent, i.worldBitangent, facing);

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float rawNdotV = dot(worldNormal, viewDir);
                float NdotV = saturate(rawNdotV);
                float absNdotV = max(abs(rawNdotV), 0.001);

                float opticalDepth = CalculateOpticalDepth(_Thickness, absNdotV);
                float3 transmittance = EvaluateBeerLambert(_AbsorptionTint.rgb, opticalDepth);
                float absorptionAlpha = 1.0 - dot(transmittance, float3(0.2126, 0.7152, 0.0722));

                float fresnel = _FresnelF0 + (1.0 - _FresnelF0) * pow(1.0 - NdotV, 5.0);
                float perceptualRoughness = 1.0 - _Smoothness;
                float3 reflectDir = reflect(-viewDir, worldNormal);
                float3 envReflection = EvaluateEnvironmentReflection(reflectDir, i.worldPos, perceptualRoughness) * fresnel;

                float3 lightDir = Unity_SafeNormalize(UnityWorldSpaceLightDir(i.worldPos));
                float3 halfVec = Unity_SafeNormalize(lightDir + viewDir);
                float NdotL = saturate(dot(worldNormal, lightDir));
                float NdotH = saturate(dot(worldNormal, halfVec));
                float LdotH = saturate(dot(lightDir, halfVec));

                float directFresnel = _FresnelF0 + (1.0 - _FresnelF0) * pow(1.0 - LdotH, 5.0);
                float directSpec = EvaluateGGXSpecular(NdotL, NdotV, NdotH, perceptualRoughness);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 directSpecularColor = _LightColor0.rgb * (directSpec * directFresnel * atten) * _SpecularTint.rgb;

                float alpha = saturate(absorptionAlpha + fresnel);
                float3 tintColor = _AbsorptionTint.rgb * absorptionAlpha * 0.25;
                float3 surfaceRGB = (tintColor + envReflection + directSpecularColor) / max(alpha, 0.001);

                float4 col = float4(surfaceRGB, alpha);
                HORIZON_APPLY_FOG_WORLD(i.worldPos, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/VertexLit"
}
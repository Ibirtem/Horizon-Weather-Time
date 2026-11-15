#ifndef HORIZON_FOG_COMMON_INCLUDED
#define HORIZON_FOG_COMMON_INCLUDED

// =====================================================================
//  HORIZON ATMOSPHERIC FOG - SINGLE SOURCE OF TRUTH
//
//  Shared by the screen-space fog pass, the procedural skybox, the
//  world-space clouds and the GPU weather particles, so terrain peaks,
//  clouds and rain streaks always dissolve into the exact same haze.
//
//  Global uniforms are published once per frame by FogManager through
//  VRCShader.SetGlobalVector (VRChat requires the "_Udon" prefix for
//  script-driven globals) or Shader.SetGlobalVector in plain Unity.
//
//  Params0: x = baseDensity, y = heightDensity, z = heightFalloff,
//           w = heightBaseOffset
//  Params1: x = startDistance, y = maxOpacity, z = skyHorizonHeight,
//           w = skyBlendIntegrity
//
//  HorizonColor: rgb = horizon color, a = fog enabled (0 or 1)
//  ZenithColor:  rgb = upper/zenith fog color, a = sun scatter power
//  SunColor:     rgb = sun/moon inscatter tint, a = sun scatter intensity
//  SunDir:       xyz = normalized direction TO dominant light, w = unused
// =====================================================================

float4 _UdonHorizonFogParams0;
float4 _UdonHorizonFogParams1;

float4 _UdonHorizonFogHorizonColor;
float4 _UdonHorizonFogZenithColor;
float4 _UdonHorizonFogSunColor;
float4 _UdonHorizonFogSunDir;

/**
 * Computes the analytical optical depth integral for a single exponential height layer
 * clipped to the ray segment [t0, dist] relative to a base plane.
 *
 * @param y0 Height at segment start relative to the layer base.
 * @param y1 Height at segment end relative to the layer base.
 * @param L Segment length in meters.
 * @param density Base extinction density at or below the layer base.
 * @param falloff Vertical exponential decay rate (1 / scaleHeight).
 * @return Optical depth contribution for this layer.
 */
float HorizonIntegrateExpLayer(float y0, float y1, float L, float density, float falloff)
{
    if (density <= 0.000001 || L <= 0.0001) return 0.0;

    float yMin = min(y0, y1);
    float yMax = max(y0, y1);
    float span = yMax - yMin;

    // Piecewise split at y = 0: constant density below base, exponential decay above
    float fBelow = (yMin < 0.0) ? saturate(-yMin / max(1e-5, span)) : 0.0;
    float LBelow = L * fBelow;
    float LAbove = L - LBelow;

    float yMinPos = max(0.0, yMin);
    float yMaxPos = max(0.0, yMax);
    float delta   = falloff * (yMaxPos - yMinPos);

    float expInt = (delta < 1e-4)
        ? (1.0 - 0.5 * delta)
        : ((1.0 - exp(-delta)) / delta);

    return density * (LBelow + LAbove * exp(-falloff * yMinPos) * expInt);
}

/**
 * Computes the analytical optical depth integral for combined atmospheric haze
 * (bounded by skyHorizonHeight scale height) and dense ground height mist.
 *
 * @param camPos World-space camera origin.
 * @param rayDir Normalized world-space ray direction from camera.
 * @param dist Total world-space distance from camera to surface.
 * @return Accumulated optical depth (tau >= 0).
 */
float HorizonComputeFogOpticalDepth(float3 camPos, float3 rayDir, float dist)
{
    float baseDensity   = _UdonHorizonFogParams0.x;
    float heightDensity = _UdonHorizonFogParams0.y;
    float heightFalloff = max(0.0001, _UdonHorizonFogParams0.z);
    float heightBase    = _UdonHorizonFogParams0.w;
    float startDistance = _UdonHorizonFogParams1.x;
    float skyHorizonH   = saturate(_UdonHorizonFogParams1.z);

    float t0 = min(dist, startDistance);
    float L  = max(0.0, dist - t0);
    if (L <= 0.0001) return 0.0;

    float y0 = (camPos.y + rayDir.y * t0) - heightBase;
    float y1 = (camPos.y + rayDir.y * dist) - heightBase;

    // Broad atmospheric haze decays with a physical ceiling derived from skyHorizonHeight,
    // so distant sky rays converge monotonically with distant terrain peaks.
    float atmosScaleH  = lerp(80.0, 520.0, skyHorizonH);
    float atmosFalloff = 1.0 / atmosScaleH;

    float tauAtmos  = HorizonIntegrateExpLayer(y0, y1, L, baseDensity, atmosFalloff);
    float tauHeight = HorizonIntegrateExpLayer(y0, y1, L, heightDensity, heightFalloff);

    return tauAtmos + tauHeight;
}

/**
 * Evaluates view-dependent atmospheric fog scattering color using a horizon-to-zenith
 * vertical gradient and directional forward Mie-like in-scattering.
 *
 * @param rayDir Normalized world-space view direction.
 * @return Linear RGB scattering color.
 */
float3 HorizonComputeFogColor(float3 rayDir)
{
    float3 horizonCol = _UdonHorizonFogHorizonColor.rgb;
    float3 zenithCol  = _UdonHorizonFogZenithColor.rgb;
    float  scatterPow = max(1.0, _UdonHorizonFogSunDir.w);
    float3 sunCol     = _UdonHorizonFogSunColor.rgb;
    float  scatterInt = _UdonHorizonFogSunColor.a;
    float3 sunDir     = _UdonHorizonFogSunDir.xyz;

    float elev = saturate(rayDir.y);
    float3 baseCol = lerp(horizonCol, zenithCol, pow(elev, 0.65));

    float cosTheta  = saturate(dot(rayDir, sunDir));
    float inscatter = pow(cosTheta, scatterPow) * scatterInt;

    return baseCol + sunCol * inscatter;
}

/**
 * Evaluates fog opacity and scattering color for a world-space geometry or particle position.
 *
 * @param camPos World-space camera position.
 * @param worldPos World-space target position.
 * @param fogColor Output scattering RGB color.
 * @return Fog opacity factor in [0, maxOpacity].
 */
float HorizonEvaluateFog(float3 camPos, float3 worldPos, out float3 fogColor)
{
    if (_UdonHorizonFogHorizonColor.a < 0.5)
    {
        fogColor = 0.0;
        return 0.0;
    }

    float3 rayVec = worldPos - camPos;
    float  dist   = length(rayVec);
    float3 rayDir = rayVec / max(dist, 1e-5);

    float tau      = HorizonComputeFogOpticalDepth(camPos, rayDir, dist);
    float maxOpac  = _UdonHorizonFogParams1.y;
    float fogAlpha = saturate(1.0 - exp(-tau)) * maxOpac;

    fogColor = HorizonComputeFogColor(rayDir);
    return fogAlpha;
}

/**
 * Evaluates fog blending for skybox rays as the far-distance limit of the exact
 * same height integral used by terrain, preventing sharp mountain-to-sky seams.
 *
 * @param camPos World-space camera position.
 * @param rayDir Normalized world-space view direction.
 * @param fogColor Output scattering RGB color.
 * @return Sky fog blend factor in [0, 1].
 */
float HorizonEvaluateSkyFog(float3 camPos, float3 rayDir, out float3 fogColor)
{
    if (_UdonHorizonFogHorizonColor.a < 0.5)
    {
        fogColor = 0.0;
        return 0.0;
    }

    float maxOpac       = _UdonHorizonFogParams1.y;
    float skyBlendInteg = _UdonHorizonFogParams1.w;

    float tau      = HorizonComputeFogOpticalDepth(camPos, rayDir, 12000.0);
    float skyAlpha = saturate(1.0 - exp(-tau)) * maxOpac * skyBlendInteg;

    fogColor = HorizonComputeFogColor(rayDir);
    return skyAlpha;
}

/**
 * Applies Horizon atmospheric fog directly to an RGBA color in custom or transparent shaders.
 */
#define HORIZON_APPLY_FOG_WORLD(worldPos, col) \
{ \
    float3 _hfCol; \
    float  _hfAlpha = HorizonEvaluateFog(_WorldSpaceCameraPos.xyz, (worldPos), _hfCol); \
    (col).rgb = lerp((col).rgb, _hfCol, _hfAlpha); \
}

#endif

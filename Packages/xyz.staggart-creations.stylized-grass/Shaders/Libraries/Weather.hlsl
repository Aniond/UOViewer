#ifndef GRASS_WEATHER_INCLUDED
#define GRASS_WEATHER_INCLUDED

//If using any other "global snow/rain" shader implementation, integrate it here

#define WET_METALICNESS 0.2
#define WET_SMOOTHNESS 0.8
#define SNOW_COLOR half3(1,1,1)

float GetSnowMask(in half3 normalWS, in float mask, in float threshold, in float falloff)
{
    if (!_SnowReceiving || _GrassSnowCoverage == 0) return 0.0;
    
    half snowFactor = saturate(dot(half3(0,1,0), normalWS.xyz));

    //threshold *= 1.0-_GrassSnowCoverage;
    //threshold = saturate(threshold);
    
    //Gradually grow the treshold along the mask
    mask = smoothstep(threshold, 1.0 + threshold, saturate(mask));
    
    half gradient = saturate(1-mask);
    half snow = smoothstep(gradient, gradient + falloff, _GrassSnowCoverage * snowFactor);
    
    return snow;
}

void ApplySnow(inout SurfaceData surfaceData, inout half3 normalWS, in half3 vertexNormal, in float mask, in float threshold, in float falloff)
{
    if (!_SnowReceiving || _GrassSnowCoverage > 0)
    {
        half snow = GetSnowMask(normalWS, mask, threshold, falloff);
        surfaceData.albedo.rgb = lerp(surfaceData.albedo.rgb, SNOW_COLOR, snow);
        
        //Cancel out normal map with snow
        normalWS = lerp(normalWS, vertexNormal, snow);
    }
}

void ApplyWetness(inout SurfaceData surfaceData, half mask)
{
    surfaceData.metallic = lerp(0, WET_METALICNESS, _GrassWetness);
    half smoothness = lerp(surfaceData.smoothness, WET_SMOOTHNESS, _GrassWetness);
    surfaceData.smoothness = lerp(0.0, smoothness, mask);
    surfaceData.albedo *= lerp(1.0, 0.8, _GrassWetness * mask);
}

//Shader Graph
void ApplySnow_float(in half3 normalWS, in float mask, float threshold, float falloff, out float3 albedo, out half3 normalTS)
{
    SurfaceData surfaceData = (SurfaceData)0;
    
    ApplySnow(surfaceData, normalWS, normalWS, mask, threshold, falloff);
    
    albedo = surfaceData.albedo.rgb;
    normalTS = surfaceData.normalTS;
}

void ApplyWetness_float(in half metallic, in half smoothness, in half mask, out half metallicB, out half smoothnessB)
{
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.metallic = metallic;
    surfaceData.smoothness = smoothness;
    
    ApplyWetness(surfaceData, mask);
    
    metallicB = surfaceData.metallic;
    smoothnessB = surfaceData.smoothness;
}
#endif
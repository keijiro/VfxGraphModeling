// Recolor from Kino post processing effect suite

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

// Uniforms given from Recolor.cs

TEXTURE2D(_ColorTexture);
TEXTURE2D(_NormalTexture);
TEXTURE2D(_DepthTexture);

float4 _EdgeColor;
float2 _EdgeThresholds;
float _FillOpacity;
float4 _BgColor;

float4 _ColorKey0;
float4 _ColorKey1;
float4 _ColorKey2;
float4 _ColorKey3;
float4 _ColorKey4;
float4 _ColorKey5;
float4 _ColorKey6;
float4 _ColorKey7;

// Load a normal vector from the normal texture.
float3 LoadNormal(uint2 positionSS)
{
    float4 v = LOAD_TEXTURE2D(_NormalTexture, positionSS);
    float2 oct = Unpack888ToFloat2(v.xyz);
    return UnpackNormalOctQuadEncode(oct * 2 - 1);
}

// Vertex shader (procedural fullscreen triangle)
void Vertex(
    uint vertexID : SV_VertexID,
    out float4 positionCS : SV_POSITION,
    out float2 texcoord : TEXCOORD0
)
{
    positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    texcoord = GetFullScreenTriangleTexCoord(vertexID);
}

// Fragment shader
float4 Fragment(
    float4 positionCS : SV_POSITION,
    float2 texcoord : TEXCOORD0
) : SV_Target
{
    // Four sample points of the roberts cross operator
    uint2 p0 = texcoord * _ScreenSize.xy;                 // TL
    uint2 p1 = min(p0 + uint2(1, 1), _ScreenSize.xy - 1); // BR
    uint2 p2 = min(p0 + uint2(1, 0), _ScreenSize.xy - 1); // TR
    uint2 p3 = min(p0 + uint2(0, 1), _ScreenSize.xy - 1); // BL

    // Source color/depth
    float4 c0 = LOAD_TEXTURE2D(_ColorTexture, p0);
    float d0 = LOAD_TEXTURE2D(_DepthTexture, p0).r;

#ifdef RECOLOR_EDGE_COLOR

    // Color samples
    float4 c1 = LOAD_TEXTURE2D(_ColorTexture, p1);
    float4 c2 = LOAD_TEXTURE2D(_ColorTexture, p2);
    float4 c3 = LOAD_TEXTURE2D(_ColorTexture, p3);

    // Roberts cross operator
    float3 g1 = c1.rgb - c0.rgb;
    float3 g2 = c3.rgb - c2.rgb;
    float g = sqrt(dot(g1, g1) + dot(g2, g2)) * 10;

#endif

#ifdef RECOLOR_EDGE_DEPTH

    // Depth samples
    float d1 = LOAD_TEXTURE2D(_DepthTexture, p1).r;
    float d2 = LOAD_TEXTURE2D(_DepthTexture, p2).r;
    float d3 = LOAD_TEXTURE2D(_DepthTexture, p3).r;

    // Roberts cross operator
    float g = length(float2(d1 - d0, d3 - d2)) * 100;

#endif

#ifdef RECOLOR_EDGE_NORMAL

    // Normal samples
    float3 n0 = LoadNormal(p0);
    float3 n1 = LoadNormal(p1);
    float3 n2 = LoadNormal(p2);
    float3 n3 = LoadNormal(p3);

    // Roberts cross operator
    float3 g1 = n1 - n0;
    float3 g2 = n3 - n2;
    float g = sqrt(dot(g1, g1) + dot(g2, g2));

#endif

    // Gradient sample
    float lum = Luminance(LinearToSRGB(c0.rgb));
    float3 fill = _ColorKey0.rgb;
#ifdef RECOLOR_GRADIENT_LERP
    fill = lerp(fill, _ColorKey1.rgb, saturate((lum - _ColorKey0.w) / (_ColorKey1.w - _ColorKey0.w)));
    fill = lerp(fill, _ColorKey2.rgb, saturate((lum - _ColorKey1.w) / (_ColorKey2.w - _ColorKey1.w)));
    fill = lerp(fill, _ColorKey3.rgb, saturate((lum - _ColorKey2.w) / (_ColorKey3.w - _ColorKey2.w)));
  #ifdef RECOLOR_GRADIENT_EXT
    fill = lerp(fill, _ColorKey4.rgb, saturate((lum - _ColorKey3.w) / (_ColorKey4.w - _ColorKey3.w)));
    fill = lerp(fill, _ColorKey5.rgb, saturate((lum - _ColorKey4.w) / (_ColorKey5.w - _ColorKey4.w)));
    fill = lerp(fill, _ColorKey6.rgb, saturate((lum - _ColorKey5.w) / (_ColorKey6.w - _ColorKey5.w)));
    fill = lerp(fill, _ColorKey7.rgb, saturate((lum - _ColorKey6.w) / (_ColorKey7.w - _ColorKey6.w)));
  #endif
#else
    fill = lum > _ColorKey0.w ? _ColorKey1.rgb : fill;
    fill = lum > _ColorKey1.w ? _ColorKey2.rgb : fill;
    fill = lum > _ColorKey2.w ? _ColorKey3.rgb : fill;
  #ifdef RECOLOR_GRADIENT_EXT
    fill = lum > _ColorKey3.w ? _ColorKey4.rgb : fill;
    fill = lum > _ColorKey4.w ? _ColorKey5.rgb : fill;
    fill = lum > _ColorKey5.w ? _ColorKey6.rgb : fill;
    fill = lum > _ColorKey6.w ? _ColorKey7.rgb : fill;
  #endif
#endif

    // Blending
    float edge = smoothstep(_EdgeThresholds.x, _EdgeThresholds.y, g);
    float3 cb = lerp(c0.rgb, fill, _FillOpacity);
    float3 co = lerp(cb, _EdgeColor.rgb, edge * _EdgeColor.a);
    co = lerp(co, _BgColor.rgb, (d0 == 0) * _BgColor.a);

    return float4(co, c0.a);
}

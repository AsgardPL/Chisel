#if OPENGL
#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "Common.fxh"

struct VertexIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

struct VertexOut
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
    float4 ScreenPos : TEXCOORD1;
};

texture MainTex;
sampler2D textureSampler = sampler_state
{
    Texture = (MainTex);
};

texture LightmapB1;
texture LightmapB2;
texture LightmapB3;
float2 LightmapSize;
sampler2D lightmapB1Sampler : register(s1) = sampler_state
{
    Texture = (LightmapB1);
};
sampler2D lightmapB2Sampler : register(s2) = sampler_state
{
    Texture = (LightmapB2);
};
sampler2D lightmapB3Sampler : register(s3) = sampler_state
{
    Texture = (LightmapB3);
};

static const float3 LM_B1 = float3(0.8164966, 0.0, 0.5773503);
static const float3 LM_B2 = float3(-0.4082483, 0.7071068, 0.5773503);
static const float3 LM_B3 = float3(-0.4082483, -0.7071068, 0.5773503);

float4x4 World;
float4x4 View;
float4x4 Projection;

bool AlphaClip;
bool UseVertColor;

VertexOut MainVS(in VertexIn input)
{
    VertexOut output = (VertexOut) 0;
	
    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.ScreenPos = output.Position;
    output.Color = input.Color;
    output.UV = input.UV;

    return output;
}

float4 MainPS(VertexOut input) : SV_Target0
{
    float4 texColor = tex2Dbias(textureSampler, float4(input.UV,0,-1));
    texColor.rgb = ConvertSRGB(texColor.rgb);
    
    if (AlphaClip)
    {
        clip(texColor.a - 0.8);
        texColor.a = 1;
    }
    
    float4 lightColor = input.Color;
    return texColor * (UseVertColor ? input.Color : float4(1,1,1,1));
}

struct DecalVertexIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float3 Normal : NORMAL0;
    float2 UV : TEXCOORD0;
    float2 LightmapUV : TEXCOORD1;
    float3 Tangent : TANGENT0;
    float3 Binormal : BINORMAL0;
};

struct DecalVertexOut
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
    float2 LightmapUV : TEXCOORD1;
    float4 WorldPos : TEXCOORD2;
    float3 Normal : TEXCOORD3;
    float3 Basis1 : TEXCOORD4;
    float3 Basis2 : TEXCOORD5;
    float3 Basis3 : TEXCOORD6;
};

DecalVertexOut DecalVS(in DecalVertexIn input)
{
    DecalVertexOut output = (DecalVertexOut) 0;

    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.WorldPos = worldPosition;
    output.Color = input.Color;
    output.UV = input.UV;
    output.LightmapUV = input.LightmapUV;
    output.Normal = input.Normal;

    output.Basis1 = LM_B1.x * input.Tangent + LM_B1.y * input.Binormal + LM_B1.z * input.Normal;
    output.Basis2 = LM_B2.x * input.Tangent + LM_B2.y * input.Binormal + LM_B2.z * input.Normal;
    output.Basis3 = LM_B3.x * input.Tangent + LM_B3.y * input.Binormal + LM_B3.z * input.Normal;

    return output;
}

float4 DecalPS(DecalVertexOut input) : SV_Target0
{
    float4 texColor = tex2Dbias(textureSampler, float4(input.UV, 0, -1));
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!UseVertColor)
        return texColor;

    float3 lm1 = tex2D(lightmapB1Sampler, input.LightmapUV).rgb;
    float3 lm2 = tex2D(lightmapB2Sampler, input.LightmapUV).rgb;
    float3 lm3 = tex2D(lightmapB3Sampler, input.LightmapUV).rgb;

    float3 lightColor = lm1 * dot(input.Basis1, input.Normal)
                       + lm2 * dot(input.Basis2, input.Normal)
                       + lm3 * dot(input.Basis3, input.Normal);
    
    lightColor = ApplyRealtimeLights(lightColor, input.Normal, input.WorldPos.xyz).rgb;

    return float4(ApplyLight(texColor.rgb, lightColor), texColor.a);
}

technique Particles
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};

technique Decals
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL DecalVS();
        PixelShader = compile PS_SHADERMODEL DecalPS();
    }
};
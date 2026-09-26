#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "ModelCommon.fxh"

bool AlphaPass;

texture MainTex;
sampler2D textureSampler = sampler_state
{
    Texture = (MainTex);
};
texture SpecTex;
sampler2D specularSampler = sampler_state
{
    Texture = (SpecTex);
};
texture NormalTex;
sampler2D normalSampler = sampler_state
{
    Texture = (NormalTex);
};

struct MDLVertexShaderInput
{
    float4 Position : POSITION0;
    float4 Normal : NORMAL0;
	float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

struct MDLVertexShaderOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPos : TEXCOORD2;
    float3 Normal : TEXCOORD1;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
    float4 ScreenPos : TEXCOORD3;
};

MDLVertexShaderOutput MainVS(in MDLVertexShaderInput input)
{
    MDLVertexShaderOutput output = (MDLVertexShaderOutput) 0;
	
    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.ScreenPos = output.Position;
    output.Normal = input.Normal;
    output.WorldPos = worldPosition.xyz;
    output.Color = ApplyFog(input.Color, worldPosition.xyz);
    output.UV = input.UV;

    return output;
}
MDLVertexShaderOutput PrePassVS(in MDLVertexShaderInput input)
{
    MDLVertexShaderOutput output = (MDLVertexShaderOutput) 0;
	
    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = input.Normal;
    output.WorldPos = worldPosition.xyz;
    output.Color = input.Color;
    output.UV = input.UV;

    return output;
}

PS_OUTPUT MainPS(MDLVertexShaderOutput input)
{
    float4 texColor = tex2D(textureSampler, input.UV);
    texColor.rgb = ConvertSRGB(texColor.rgb);
    
    if (!Transparent)
    {
        clip(texColor.a - 0.5f);
        texColor.a = 1;
    }
    
    if (texColor.a > 0.6f)
        texColor.a = 1;
    
    float4 lightColor = input.Color;

    lightColor = ApplyRealtimeLights(lightColor.xyz, input.Normal, input.WorldPos);

    float4 finalColor = float4(ApplyLight(texColor.xyz, lightColor.xyz), texColor.a);

    return WriteSceneOutput(finalColor);
}
float4 PrePassPS(MDLVertexShaderOutput input) : COLOR
{
    float4 texColor = tex2D(textureSampler, input.UV);
    
    clip(texColor.a - 0.6f);
    texColor.a = 1;
    
    return texColor;
}


technique BasicColorDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
    pass P1
    {
        VertexShader = compile VS_SHADERMODEL PrePassVS();
        PixelShader = compile PS_SHADERMODEL PrePassPS();
    }
};
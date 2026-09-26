#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#include "Common.fxh"

float4x4 World;
float4x4 View;
float4x4 Projection;
bool Skybox3DView = false;
float3 SkyboxViewTranslation;
float3 basis1, basis2, basis3;

texture mainTexture;
sampler2D textureSampler = sampler_state
{
    Texture = (mainTexture);
};
texture normalTexture;
sampler2D normalSampler = sampler_state
{
    Texture = (normalTexture);
};
texture blendTexture;
sampler2D blendSampler = sampler_state
{
    Texture = (blendTexture);
};
texture terrainBlendMix;
sampler2D blendMixSampler = sampler_state
{
    Texture = (terrainBlendMix);
};
float2 LightmapSize;
texture LightmapB1;
texture LightmapB2;
texture LightmapB3;
sampler2D lightmapB1Sampler : register(s3) = sampler_state
{
    Texture = (LightmapB1);
};
sampler2D lightmapB2Sampler : register(s4) = sampler_state
{
    Texture = (LightmapB2);
};
sampler2D lightmapB3Sampler : register(s5) = sampler_state
{
    Texture = (LightmapB3);
};

static const float3 VALVE_B1 = float3(0.81649661, 0.0, 0.57735026);
static const float3 VALVE_B2 = float3(-0.40824831, 0.70710678, 0.57735026);
static const float3 VALVE_B3 = float3(-0.40824831, -0.70710678, 0.57735026);

struct TVertexShaderInput
{
	float4 Position : POSITION0;
    float4 Normal : NORMAL0;
    float3 TexCoord : TEXCOORD0;
    float2 LightmapCoord : TEXCOORD1;
    float4 Tangent : TANGENT0;
};

struct TVertexShaderOutput
{
    float4 Position : POSITION0;
    float4 Normal : TEXCOORD1;
    float4 Tangent : TEXCOORD5;
    float4 Binormal : TEXCOORD6;
    float3 B1 : TEXCOORD2;
    float3 B2 : TEXCOORD3;
    float3 B3 : TEXCOORD4;
    float4 WorldPos : COLOR1;
    float3 TexCoord : TEXCOORD0;
    float2 LightmapCoord : COLOR0;
    float4 ScreenPos : TEXCOORD7;
};
float4 cubic(float v)
{
    float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
    float4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return float4(x, y, z, w) * (1.0 / 6.0);
}
float4 textureBicubic(sampler2D samp, float2 texCoords)
{
    float2 texSize = LightmapSize;
    float2 invTexSize = 1.0 / texSize;
   
    texCoords = texCoords * texSize - 0.5;

   
    float2 fxy = frac(texCoords);
    texCoords -= fxy;

    float4 xcubic = cubic(fxy.x);
    float4 ycubic = cubic(fxy.y);

    float4 c = texCoords.xxyy + float2(-0.5, +1.5).xyxy;
    
    float4 s = float4(xcubic.xz + xcubic.yw, ycubic.xz + ycubic.yw);
    float4 offset = c + float4(xcubic.yw, ycubic.yw) / s;
    
    offset *= invTexSize.xxyy;
    
    float4 sample0 = tex2D(samp, offset.xz);
    float4 sample1 = tex2D(samp, offset.yz);
    float4 sample2 = tex2D(samp, offset.xw);
    float4 sample3 = tex2D(samp, offset.yw);

    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);

    return lerp(
       lerp(sample3, sample2, sx), lerp(sample1, sample0, sx)
    , sy);
}

TVertexShaderOutput MainVS(in TVertexShaderInput input)
{
	TVertexShaderOutput output = (TVertexShaderOutput)0;
    
    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    
    output.Position = mul(viewPosition, Projection);
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;
    output.ScreenPos = output.Position;
    
    if (Skybox3DView)
    {
        output.WorldPos = (worldPosition - float4(SkyboxViewTranslation, 0)) * 16;
        
        float4 fakeViewPosition = mul(output.WorldPos, View);
        
        output.WorldPos.w = mul(fakeViewPosition, Projection).w;
    }
    
    // Transform TBN into world space
    float3 N = normalize(mul(float4(input.Normal.xyz, 0), World).xyz);
    float3 T = normalize(mul(float4(input.Tangent.xyz, 0), World).xyz);
    float3 B = cross(N, T) * input.Tangent.w;

    // Rotate the canonical Valve basis into world space using this vertex's TBN.
    output.B1 = VALVE_B1.x * T + VALVE_B1.y * B + VALVE_B1.z * N;
    output.B2 = VALVE_B2.x * T + VALVE_B2.y * B + VALVE_B2.z * N;
    output.B3 = VALVE_B3.x * T + VALVE_B3.y * B + VALVE_B3.z * N;

    output.Normal = float4(N,0);
    output.TexCoord = input.TexCoord;
    output.LightmapCoord = input.LightmapCoord;
    output.Tangent = float4(T,0);
    output.Binormal = float4(B,0);

    return output;
}

PS_OUTPUT MainPS(TVertexShaderOutput input)
{
    float texBlendOffset = tex2D(blendMixSampler, input.TexCoord.xy).r;
    float4 textureColor = lerp(tex2D(blendSampler, input.TexCoord.xy),
                                 tex2D(textureSampler, input.TexCoord.xy),
                                 saturate(input.TexCoord.z + texBlendOffset));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);
    
    float3 bump = (2 * tex2D(normalSampler, input.TexCoord.xy).xyz) - float3(1, 1, 1);
    float3 bumpNormal = normalize(input.Normal.xyz + (bump.x * input.Tangent.xyz + bump.y * input.Binormal.xyz * -1));

    float3 lm1 = textureBicubic(lightmapB1Sampler, input.LightmapCoord).rgb;
    float3 lm2 = textureBicubic(lightmapB2Sampler, input.LightmapCoord).rgb;
    float3 lm3 = textureBicubic(lightmapB3Sampler, input.LightmapCoord).rgb;
    
    float w1 = saturate(dot(bumpNormal, normalize(input.B1)));
    float w2 = saturate(dot(bumpNormal, normalize(input.B2)));
    float w3 = saturate(dot(bumpNormal, normalize(input.B3)));

    float3 lightmapColor = lm1 * w1 + lm2 * w2 + lm3 * w3;
    
    float3 realtimeLit = ApplyRealtimeLights(lightmapColor, bumpNormal, input.WorldPos.xyz);
    float4 finalColor = ApplyFog(float4(ApplyLight(textureColor.rgb, realtimeLit), textureColor.a), input.WorldPos.xyz);
    return WriteSceneOutput(finalColor);
}

technique BasicColorDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#include "Common.fxh"

float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 InverseView;
float4x4 InverseProjection;

float3 SkyboxViewTranslation;
bool Skybox3DView = false;
float3 cubeSamplePos;

static const float3 WaterDeepColor = float3(55 / 255.0, 70 / 255.0, 82 / 255.0);

struct WVertexShaderInput
{
    float4 Position : POSITION0;
    float4 Normal : NORMAL0;
    float3 Tangent : TANGENT0;
    float3 Binormal : BINORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
    float2 LightmapCoordinate : TEXCOORD1;
};

struct WVertexShaderOutput
{
    float4 Position : POSITION0;
    float4 WorldPos : COLOR1;
    float3 Normal : TEXCOORD0;
    float2 TextureCoordinate : TEXCOORD1;
    float2 LightmapCoordinate : TEXCOORD5;
    float3 FaceTangent : TEXCOORD2;
    float3 FaceBinormal : TEXCOORD3;
    float4 ScreenPos : TEXCOORD4;
};

texture BrushSpec;
sampler2D specularSampler : register(s1) = sampler_state
{
    Texture = (BrushSpec);
};
texture BrushNorm;
sampler2D normalSampler : register(s2) = sampler_state
{
    Texture = (BrushNorm);
    AddressU = Wrap;
    AddressV = Wrap;
};
texture LightmapB1;
float2 LightmapSize;
sampler2D lightmapB1Sampler : register(s3) = sampler_state
{
    Texture = (LightmapB1);
};

texture LightmapB2;
sampler2D lightmapB2Sampler : register(s4) = sampler_state
{
    Texture = (LightmapB2);
};

texture LightmapB3;
sampler2D lightmapB3Sampler : register(s5) = sampler_state
{
    Texture = (LightmapB3);
};

texture RefractionTex;
sampler2D refractionSampler : register(s6) = sampler_state
{
    Texture = (RefractionTex);
};
texture RefractionDepth;
sampler2D sceneDepthSampler : register(s7) = sampler_state
{
    Texture = (RefractionDepth);
};
texture ReflectionTex;
sampler2D reflectionSampler : register(s8) = sampler_state
{
    Texture = (ReflectionTex);
};
texture dudv;
sampler2D DUDVSampler : register(s9) = sampler_state
{
    Texture = (dudv);
};

static const int AtlasW = 6;
static const int AtlasH = 5;
static const int AtlasFrames = AtlasW * AtlasH;

static const float DUDVFrameRate1 = 20.0;
static const float DUDVScale1 = 0.1;

static const float DUDVFrameRate2 = 10.0;
static const float DUDVScale2 = 0.037;

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 Hash22(float2 p)
{
    float n = Hash21(p);
    return float2(n, Hash21(p + n + 17.13));
}
float4 CalcReflection(float2 vTexCoord, float blurAmount, float2 jitter)
{
    float2 offset = (1.0 / screenSize) * blurAmount;
    float2 jitterOffset = jitter * offset;

    float4 result = 0;
    result += tex2D(reflectionSampler, vTexCoord + jitterOffset + float2(-offset.x, -offset.y));
    result += tex2D(reflectionSampler, vTexCoord + jitterOffset + float2(offset.x, -offset.y));
    result += tex2D(reflectionSampler, vTexCoord + jitterOffset + float2(-offset.x, offset.y));
    result += tex2D(reflectionSampler, vTexCoord + jitterOffset + float2(offset.x, offset.y));
    result *= 0.25;
    return result;
}

float2 SampleDUDV(float2 worldXZ, float scale, float frameRate)
{
    int frame = (int) fmod(time * frameRate, (float) AtlasFrames);
    float2 cellSize = float2(1.0 / AtlasW, 1.0 / AtlasH);
    float2 cellOrigin = float2(frame % AtlasW, frame / AtlasW) * cellSize;

    float2 tiledUV = frac(worldXZ * scale);
    float2 atlasUV = cellOrigin + tiledUV * cellSize;

    return tex2D(DUDVSampler, atlasUV).xy;
}

WVertexShaderOutput VertexShaderFunction(WVertexShaderInput input)
{
    WVertexShaderOutput output = (WVertexShaderOutput)0;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);

    output.Position = mul(viewPosition, Projection);
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    if (Skybox3DView)
    {
        output.WorldPos = (worldPosition - float4(SkyboxViewTranslation, 0)) * 16;
        float4 fakeViewPosition = mul(output.WorldPos, View);
        output.WorldPos.w = mul(fakeViewPosition, Projection).w;
    }

    output.Normal = input.Normal.xyz;
    output.TextureCoordinate = input.TextureCoordinate;
    output.LightmapCoordinate = input.LightmapCoordinate;
    output.FaceTangent = input.Tangent;
    output.FaceBinormal = input.Binormal;
    output.ScreenPos = output.Position;

    return output;
}

float4 PixelCore(WVertexShaderOutput input, int quality)
{
    float2 screenUV = (input.ScreenPos.xy / input.ScreenPos.w) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float2 pixelCoord = screenUV * screenSize;
    
    float4 lightmap = tex2D(lightmapB1Sampler, input.LightmapCoordinate) * 0.6 +
                      tex2D(lightmapB2Sampler, input.LightmapCoordinate) * 0.6 +
                      tex2D(lightmapB3Sampler, input.LightmapCoordinate) * 0.6;
    
    float2 ditherJitter = 0;
    ditherJitter = Hash22(pixelCoord + time * 60) - 0.5;
    
    float2 dudv1 = SampleDUDV(input.WorldPos.xz, DUDVScale1, DUDVFrameRate1);
    float2 dudv2 = SampleDUDV(input.WorldPos.xz, DUDVScale2, DUDVFrameRate2);

    float2 combinedOffset = ((dudv1 * 2 - 1) + (dudv2 * 2 - 1));
    float2 samplePoint = screenUV + combinedOffset * 0.025;
    
    float4 reflectionColor = CalcReflection(samplePoint, 2, ditherJitter);
    float4 refractionColor = tex2D(refractionSampler, samplePoint);
    float4 rawRefractionColor = tex2D(refractionSampler, screenUV);
    
    float sceneDepth = GetLinearDepth(tex2D(sceneDepthSampler, screenUV).r);
    float waterdist = input.ScreenPos.z;
    
    float3 normal = input.Normal.xyz;
    float3 dirToWater = normalize(input.WorldPos.xyz - cameraPos);
    bool viewingFromBelow = dot(dirToWater, normal) > 0.0;
    
    float viewDepth = distance(waterdist, sceneDepth);
    
    float verticalness = max(dot(normal, float3(0, 1, 0)), 0.05);
    float underwaterDepth = max(viewDepth * verticalness, 0);
    
    float waterDownView = dot(normal, -dirToWater);
    
    float3 litColor = float3(0,0,0);
    float3 waterColor = WaterDeepColor * lightmap.xyz;
    
    if (viewingFromBelow)
        return lerp(rawRefractionColor, refractionColor, saturate(viewDepth));
    
    refractionColor.xyz = lerp(refractionColor.xyz, waterColor, saturate(underwaterDepth * 0.24 * (1 - waterDownView * 0.6)));
    
    litColor = lerp(reflectionColor, refractionColor, waterDownView);
    litColor = lerp(rawRefractionColor.xyz, litColor, saturate(viewDepth));

    return float4(litColor, 1);
}

PS_OUTPUT PixelShaderFunctionHigh(WVertexShaderOutput input) : SV_Target0
{
    return WriteSceneOutput(PixelCore(input, 2));
}

PS_OUTPUT PixelShaderFunctionMed(WVertexShaderOutput input) : SV_Target0
{
    return WriteSceneOutput(PixelCore(input, 1));
}

PS_OUTPUT PixelShaderFunctionLow(WVertexShaderOutput input) : SV_Target0
{
    return WriteSceneOutput(PixelCore(input, 0));
}

technique High
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunctionHigh();
    }
}
technique Med
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunctionMed();
    }
}
technique Low
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunctionLow();
    }
}
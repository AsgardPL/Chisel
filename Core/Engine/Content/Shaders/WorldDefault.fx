#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#include "Common.fxh"

float4x4 World;
float4x4 View;
float4x4 Projection;

//Diffuse
float3 DiffuseLightDirection = float3(1, 0, 0);
float4 DiffuseColor = float4(1, 1, 1, 1);
float DiffuseIntensity = 1.0;

float3 SkyboxViewTranslation;

static const float3 LM_B1 = float3(0.8164966, 0.0,        0.5773503);
static const float3 LM_B2 = float3(-0.4082483, 0.7071068, 0.5773503);
static const float3 LM_B3 = float3(-0.4082483, -0.7071068, 0.5773503);

float lightmapSize;

bool DisableLighting = false;
bool ShowLightmap = false;
bool ExpandWireframe = false;
bool Skybox3DView = false;
bool transparent = false;

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
    float2 LightCoordinate : TEXCOORD2;
    float3 FaceTangent : TEXCOORD3;
    float3 FaceBinormal : TEXCOORD4;
    float3 Basis1 : TEXCOORD5;
    float3 Basis2 : TEXCOORD6;
    float3 Basis3 : TEXCOORD7;
    float4 ScreenPos : TEXCOORD8;
};

texture BrushTex;
sampler2D textureSampler : register(s0) = sampler_state
{
    Texture = (BrushTex);
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
sampler2D lightmapB3Sampler: register(s5) = sampler_state
{
    Texture = (LightmapB3);
};
float3 cubeSamplePos;

WVertexShaderOutput VertexShaderFunction(WVertexShaderInput input)
{
    WVertexShaderOutput output;

    float4 worldPosition = mul(ExpandWireframe ? input.Position + input.Normal * 0.0001 : input.Position, World);
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

	output.Normal = input.Normal.xyz;
    output.TextureCoordinate = input.TextureCoordinate;
	output.LightCoordinate = input.LightmapCoordinate;

    output.FaceTangent = input.Tangent;
    output.FaceBinormal = input.Binormal;

    output.Basis1 = LM_B1.x * input.Tangent + LM_B1.y * input.Binormal + LM_B1.z * input.Normal.xyz;
    output.Basis2 = LM_B2.x * input.Tangent + LM_B2.y * input.Binormal + LM_B2.z * input.Normal.xyz;
    output.Basis3 = LM_B3.x * input.Tangent + LM_B3.y * input.Binormal + LM_B3.z * input.Normal.xyz;

    return output;
}

WVertexShaderOutput VertexShaderFunction_DepthOnly(WVertexShaderInput input)
{
    WVertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    
    output.Position = mul(viewPosition, Projection);
    output.WorldPos = worldPosition;
    output.ScreenPos = output.Position;

    output.Normal = input.Normal.xyz;
    output.TextureCoordinate = input.TextureCoordinate;
    output.LightCoordinate = input.LightmapCoordinate;

    output.FaceTangent = input.Tangent;
    output.FaceBinormal = input.Binormal;

    // Pointless here, but they still need to be set
    output.Basis1 = input.Normal.xyz;
    output.Basis2 = input.Normal.xyz;
    output.Basis3 = input.Normal.xyz;

    return output;
}

// from http://www.java-gaming.org/index.php?topic=35123.0
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
float4 GetLightOutput(WVertexShaderOutput input, float3 bumpNormal)
{
    float4 lightNormColor = textureBicubic(lightmapB1Sampler, input.LightCoordinate);
    float4 lightTanColor = textureBicubic(lightmapB2Sampler, input.LightCoordinate);
    float4 lightBiColor = textureBicubic(lightmapB3Sampler, input.LightCoordinate);

    float4 lightColor = lightNormColor * (dot(input.Basis1, bumpNormal)) +
                        lightTanColor * (dot(input.Basis2, bumpNormal)) +
                        lightBiColor * (dot(input.Basis3, bumpNormal));

    if (!DisableLighting || ShowLightmap)
    {
        return float4((lightColor).xyz, 1);
    }
    else
    {
        return float4(1,1,1, 1);
    }
}
float4 GetLightOutput_lq(WVertexShaderOutput input, float3 bumpNormal)
{
    float4 lightNormColor = tex2D(lightmapB1Sampler, input.LightCoordinate);
    float4 lightTanColor = tex2D(lightmapB2Sampler, input.LightCoordinate);
    float4 lightBiColor = tex2D(lightmapB3Sampler, input.LightCoordinate);

    float4 lightColor = lightNormColor * (dot(input.Basis1, bumpNormal)) +
                        lightTanColor * (dot(input.Basis2, bumpNormal)) +
                        lightBiColor * (dot(input.Basis3, bumpNormal));

    if (!DisableLighting || ShowLightmap)
    {
        return float4((lightColor).xyz, 1);
    }
    else
    {
        return float4(1, 1, 1, 1);
    }
}
float4 GetLightOutput_elq(WVertexShaderOutput input, float3 bumpNormal)
{
    float4 lightNormColor = tex2D(lightmapB1Sampler, float2(input.LightCoordinate));
    float4 lightTanColor = tex2D(lightmapB2Sampler, float2(input.LightCoordinate));
    float4 lightBiColor = tex2D(lightmapB3Sampler, float2(input.LightCoordinate));

    float4 lightColor = lightNormColor * 0.6 +
                        lightTanColor * 0.6 +
                        lightBiColor * 0.6;

    if (!DisableLighting || ShowLightmap)
    {
        return float4((lightColor).xyz, 1);
    }
    else
    {
        return float4(1, 1, 1, 1);
    }
}
PS_OUTPUT PixelShaderFunctionCore(WVertexShaderOutput input, float4 textureColor)
{
    float3 bump = (2 * tex2D(normalSampler, input.TextureCoordinate).xyz) - float3(1, 1, 1);

    float3 bumpNormal = input.Normal + (bump.x * input.FaceTangent + bump.y * input.FaceBinormal * -1);
    float4 specularColor = tex2D(specularSampler, input.TextureCoordinate);

    float3 reflectionColor = float3(0, 0, 0);

    if (specularColor.b > 0.01f)
    {
        float3 viewDir = normalize(input.WorldPos.xyz - cubeSamplePos);
        float3 reflectionVector = reflect(viewDir, normalize(bumpNormal));
        float3 cubeColor = CubemapSample(reflectionVector);
        reflectionColor = cubeColor * specularColor.b;
    }

    float4 Color0 = textureColor + float4(reflectionColor, 0);
    float4 Color1 = float4(input.Normal.xyz, Color0.a);
    float4 Color2 = input.WorldPos.xyzw;

    float4 Color3 = float4(GetLightOutput(input, bumpNormal).xyz, 1);

    float3 lightTerm = ApplyRealtimeLights(Color3.xyz, bumpNormal, Color2.xyz).xyz;

    Color3 = ApplyFog(float4(ApplyLight(Color0.rgb, lightTerm), Color0.a), input.WorldPos.xyz);

    return WriteSceneOutput(Color3);
}

PS_OUTPUT PixelShaderFunction(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    clip(textureColor.a - 0.01f);

    return PixelShaderFunctionCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_NoClip(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    return PixelShaderFunctionCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_MedCore(WVertexShaderOutput input, float4 textureColor)
{
    float3 bump = (2 * tex2D(normalSampler, input.TextureCoordinate).xyz) - float3(1, 1, 1);
    float3 bumpNormal = input.Normal + (bump.x * input.FaceTangent + bump.y * input.FaceBinormal * -1);

    float4 Color0 = textureColor;
    float4 Color1 = float4(input.Normal.xyz, Color0.a);
    float4 Color2 = input.WorldPos.xyzw;

    float4 Color3 = float4(GetLightOutput_lq(input, bumpNormal).xyz, 1);

    float3 lightTerm = ApplyRealtimeLights(Color3.xyz, bumpNormal, Color2.xyz).xyz;

    Color3 = ApplyFog(float4(ApplyLight(Color0.rgb, lightTerm), Color0.a), input.WorldPos.xyz);
    
    return WriteSceneOutput(Color3);
}

PS_OUTPUT PixelShaderFunction_Med(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    clip(textureColor.a - 0.01f);

    return PixelShaderFunction_MedCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_Med_NoClip(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    return PixelShaderFunction_MedCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_LowCore(WVertexShaderOutput input, float4 textureColor)
{
    float4 Color0 = textureColor;
    float4 Color1 = float4(input.Normal.xyz, Color0.a);
    float4 Color2 = input.WorldPos.xyzw;
    float4 Color3 = float4(GetLightOutput_elq(input, input.Normal).xyz, 1);

    float3 lightTerm = ApplyRealtimeLights(Color3.xyz, input.Normal, Color2.xyz).xyz;

    Color3 = ApplyFog(float4(ApplyLight(Color0.rgb, lightTerm), Color0.a), input.WorldPos.xyz);
    
    return WriteSceneOutput(Color3);
}

PS_OUTPUT PixelShaderFunction_Low(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    clip(textureColor.a - 0.01f);

    return PixelShaderFunction_LowCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_Low_NoClip(WVertexShaderOutput input)
{
    float4 textureColor = tex2Dbias(textureSampler, float4(input.TextureCoordinate, 0, -0.5f));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    return PixelShaderFunction_LowCore(input, textureColor);
}

PS_OUTPUT PixelShaderFunction_DepthOnly(WVertexShaderOutput input)
{
    return WriteSceneOutput(float4(0,0,0,1));
}


technique High_AlphaClip
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}
technique Med_AlphaClip
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Med();
    }
}
technique Low_AlphaClip
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Low();
    }
}

technique High
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_NoClip();
    }
}
technique Med
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Med_NoClip();
    }
}
technique Low
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Low_NoClip();
    }
}

technique DepthOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction_DepthOnly();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_DepthOnly();
    }
}
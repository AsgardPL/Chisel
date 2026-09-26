#define MAXREALTIMELIGHTS 8
#define MAXCOOKIELIGHTS 4

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float4 Binormal : BINORMAL;
	float2 TextureCoordinate : TEXCOORD0; 
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 ScreenPosition : TEXCOORD4;
    float4 WorldPos : COLOR1;
    float4 Color : COLOR0;
    float3 Normal : TEXCOORD1;
    float3 Tangent : TEXCOORD2;
    float3 Binormal : TEXCOORD3;
    float2 TextureCoordinate : TEXCOORD0; 
};

struct PS_OUTPUT
{
    float4 Color : SV_Target0;
};

int realtimeLightCount;
float4 realtimeLightPositions[MAXREALTIMELIGHTS];
float4 realtimeLightColors[MAXREALTIMELIGHTS];
float4 realtimeLightSpotData[MAXREALTIMELIGHTS];

/*
int cookieLightCount;
texture cookieLightAtlas;
texture cookieShadowAtlas;
float4x4 cookieLightMatrices[MAXCOOKIELIGHTS];
float4 cookieLightPositions[MAXCOOKIELIGHTS];
float3 cookieLightColors[MAXCOOKIELIGHTS];
float4 cookieLightAtlasRect[MAXCOOKIELIGHTS]; // xy = tile offset, zw = tile scale, in 0-1 atlas space

sampler2D cookieLightAtlasSampler = sampler_state
{
    Texture = (cookieLightAtlas);
    MagFilter = Linear;
    MinFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
sampler2D cookieShadowAtlasSampler = sampler_state
{
    Texture = (cookieShadowAtlas);
    MagFilter = Point;
    MinFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};
*/

float cubemapSize;
texture cubemap;
samplerCUBE cubemapSampler = sampler_state
{
    Texture = (cubemap);
};

float cubemapBlendFactor;
texture cubemapBlend;
samplerCUBE cubemapBlendSampler = sampler_state
{
    Texture = (cubemapBlend);
};

float4 fogColor;
float fogIntensity;
float fogStart, fogEnd;
float time;

float3 cameraPos;
float cameraNear;
float cameraFar;

float3 sunDir;
float3 sunColor;

float2 screenSize;

float4 GetLightOutput(float3 inputColor, float3 AmbientColor)
{
    return float4((inputColor + AmbientColor), 1);
}

/*
float SampleCookieShadow(int slot, float2 uv)
{
    float2 atlasUV = cookieLightAtlasRect[slot].xy + uv * cookieLightAtlasRect[slot].zw;
    float texel = cookieLightAtlasRect[slot].z / 512.0;

    float shadow = 0;
    shadow += tex2D(cookieShadowAtlasSampler, atlasUV + float2(-texel, -texel)).r;
    shadow += tex2D(cookieShadowAtlasSampler, atlasUV + float2(texel, -texel)).r;
    shadow += tex2D(cookieShadowAtlasSampler, atlasUV + float2(-texel, texel)).r;
    shadow += tex2D(cookieShadowAtlasSampler, atlasUV + float2(texel, texel)).r;
    return shadow * 0.25;
}
*/

float4 ApplyRealtimeLights(float3 baseLight, float3 normal, float3 worldpos)
{
    float3 finalColor = baseLight;
    
    [loop]
    for (int i = 0; i < realtimeLightCount; i++)
    {
        float intensity = saturate((realtimeLightPositions[i].a - distance(worldpos, realtimeLightPositions[i].rgb)) / realtimeLightPositions[i].a);
        
        float3 lightDir = -normalize(worldpos - realtimeLightPositions[i].xyz);
        intensity *= dot(lightDir, normal)*0.5+0.5;

        float spotAng = realtimeLightSpotData[i].w;

        if (spotAng > 0)
        {
            float ang = acos(dot(-lightDir, realtimeLightSpotData[i].xyz));
            intensity *= sqrt(saturate(spotAng - ang) / spotAng);
        }
        
        finalColor += realtimeLightColors[i].rgb * realtimeLightColors[i].a * intensity;
    }
    return float4(finalColor, 1);
}

float3 ApplyLight(float3 baseColor, float3 lightColor)
{
    return baseColor * lightColor;
}

float4 ApplyFog(float4 baseColor, float3 worldPos)
{
    float dist = saturate((distance(worldPos, cameraPos) - fogStart) / (fogEnd - fogStart));
    
    return float4(lerp(baseColor.rgb, fogColor.rgb, dist * fogIntensity), baseColor.a);
}
float4 CubemapSample(float3 dir)
{
    float M = max(max(abs(dir.x), abs(dir.y)), abs(dir.z));
    float scale = (cubemapSize - 1) / cubemapSize;

    float3 notMax = float3(abs(dir.x) != M, abs(dir.y) != M, abs(dir.z) != M);
    dir = lerp(dir, dir * scale, notMax);

    return texCUBE(cubemapSampler, dir);
}

float3 ConvertSRGB(float3 c)
{
    return pow(c, 2.2f);
}

float GetLinearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2 * cameraNear * cameraFar) / (cameraFar + cameraNear - z * (cameraFar - cameraNear));
}

PS_OUTPUT WriteSceneOutput(float4 color)
{
    PS_OUTPUT output;
    output.Color = color;
    return output;
}

PS_OUTPUT WhitePixelShader()
{
    return WriteSceneOutput(float4(1,1,1,1));
}
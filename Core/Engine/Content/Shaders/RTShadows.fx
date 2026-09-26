#if OPENGL
#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

float4x4 World;
float4x4 View;
float4x4 Projection;

int shadowSize;
float2 TextureSize;
float3 ShadowColor;

texture2D Texture;
texture2D BlobTex;
sampler2D TextureSampler = sampler_state
{
    Texture = (Texture);
    magfilter = LINEAR;
    minfilter = LINEAR;
    mipfilter = LINEAR;
};
sampler2D BlobSampler = sampler_state
{
    Texture = (BlobTex);
    magfilter = POINT;
    minfilter = POINT;
    mipfilter = POINT;
};

struct VertexOutput
{
    float4 position : POSITION0;
    float2 texcoord : TEXCOORD0;
    float4 color : TEXCOORD1;
};

struct PS_OUTPUT
{
    float4 Color : SV_Target0;
    float4 Depth : SV_Target1;
    float4 LightOutput : SV_Target2;
};

VertexOutput VSShader(float4 position : POSITION0, float2 texcoord : TEXCOORD0, float4 color : COLOR0)
{
    VertexOutput o = (VertexOutput) 0;

    o.position = mul(mul(mul(position, World), View), Projection);
    o.texcoord = texcoord;
    o.color = color;
    
    return o;
}
float CalcShadowTermSoftPCF(float2 vTexCoord)
{
    float shadowMapSize = (1.f / TextureSize);
    
    float shadowIntensity = 0;
    shadowIntensity += tex2D(TextureSampler, vTexCoord + float2(-shadowMapSize, -shadowMapSize));
    shadowIntensity += tex2D(TextureSampler, vTexCoord + float2(shadowMapSize, -shadowMapSize));
    shadowIntensity += tex2D(TextureSampler, vTexCoord + float2(-shadowMapSize, shadowMapSize));
    shadowIntensity += tex2D(TextureSampler, vTexCoord + float2(shadowMapSize, shadowMapSize));
    shadowIntensity /= 4;
    return shadowIntensity;
}
PS_OUTPUT PSShader(VertexOutput input)
{
    float4 texcolor = CalcShadowTermSoftPCF(input.texcoord * float2(1,-1));
    float4 blobcolor = tex2D(BlobSampler,input.texcoord);
    float shadowDepth = 1-input.color.w;
    float shadowIntensity = ((1 - texcolor.r) * (blobcolor.a * 0.5f + 0.5f)) * shadowDepth;
    float3 shadowColor = lerp(ShadowColor,float3(1,1,1),1-shadowIntensity);
    
    PS_OUTPUT o;
    o.Color = float4(shadowColor, 1);
    o.Depth = float4(1, 1, 1, 1);
    o.LightOutput = float4(1, 1, 1, 1);
    
    return o;
}

technique Textured
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VSShader();
        PixelShader = compile PS_SHADERMODEL PSShader();
    }
}
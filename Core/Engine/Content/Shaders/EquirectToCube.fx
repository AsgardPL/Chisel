#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float3 FaceForward;
float3 FaceUp;
float3 FaceRight;

texture EquirectTexture;
sampler EquirectSampler = sampler_state
{
    Texture = <EquirectTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Clamp;
};

struct VSOutput
{
    float4 Position : SV_Position;
    float2 ClipPosition : TEXCOORD0;
};

VSOutput VSMain(float4 position : POSITION0)
{
    VSOutput output;
    output.Position = position;
    output.ClipPosition = position.xy;
    return output;
}

static const float PI = 3.14159265359;

float4 PSMain(VSOutput input) : COLOR0
{
    float3 direction = normalize(FaceForward + input.ClipPosition.x * FaceRight + input.ClipPosition.y * FaceUp);

    float2 uv;
    uv.x = 0.5 + atan2(direction.x, -direction.z) / (2.0 * PI);
    uv.y = 0.5 - asin(direction.y) / PI;

    return tex2D(EquirectSampler, uv);
}

technique EquirectToCube
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSMain();
        PixelShader = compile PS_SHADERMODEL PSMain();
    }
}
#if OPENGL
#define SV_POSITION POSITION
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

texture2D Group0;
sampler Group0Sampler = sampler_state
{
    Texture = <Group0>;
    Filter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
texture2D Group1;
sampler Group1Sampler = sampler_state
{
    Texture = <Group1>;
    Filter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
texture2D Group2;
sampler Group2Sampler = sampler_state
{
    Texture = <Group2>;
    Filter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
texture2D Group3;
sampler Group3Sampler = sampler_state
{
    Texture = <Group3>;
    Filter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

// xyz = style color, w = style intensity multiplier. w<=0 means the slot is unused.
float4 Tint0;
float4 Tint1;
float4 Tint2;
float4 Tint3;

struct VSOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct PSOutput
{
    float4 B1 : SV_Target0;
    float4 B2 : SV_Target1;
    float4 B3 : SV_Target2;
};

PSOutput MainPS(VSOutput input)
{
    PSOutput output;
    output.B1 = float4(0, 0, 0, 1);
    output.B2 = float4(0, 0, 0, 1);
    output.B3 = float4(0, 0, 0, 1);
    
    float3 g = tex2D(Group0Sampler, input.TexCoord).rgb;
    output.B1.rgb += g.r * Tint0.rgb * Tint0.w;
    output.B2.rgb += g.g * Tint0.rgb * Tint0.w;
    output.B3.rgb += g.b * Tint0.rgb * Tint0.w;
    
    g = tex2D(Group1Sampler, input.TexCoord).rgb;
    output.B1.rgb += g.r * Tint1.rgb * Tint1.w;
    output.B2.rgb += g.g * Tint1.rgb * Tint1.w;
    output.B3.rgb += g.b * Tint1.rgb * Tint1.w;
    
    g = tex2D(Group2Sampler, input.TexCoord).rgb;
    output.B1.rgb += g.r * Tint2.rgb * Tint2.w;
    output.B2.rgb += g.g * Tint2.rgb * Tint2.w;
    output.B3.rgb += g.b * Tint2.rgb * Tint2.w;
    
    g = tex2D(Group3Sampler, input.TexCoord).rgb;
    output.B1.rgb += g.r * Tint3.rgb * Tint3.w;
    output.B2.rgb += g.g * Tint3.rgb * Tint3.w;
    output.B3.rgb += g.b * Tint3.rgb * Tint3.w;

    return output;
}

technique Composite
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
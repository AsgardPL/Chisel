#define MAXSTATICLIGHTS 4
#define SHCOEFFICIENTS 9
#include "Common.fxh"

int static_lightaffectingcount;
float4 static_lightpositions[MAXSTATICLIGHTS];
float4 static_lightcolors[MAXSTATICLIGHTS];
float4 static_lightangles[MAXSTATICLIGHTS]; // xyz = heading vector, w = optional spotlight angle

float3 indirectSH[SHCOEFFICIENTS];

float3 cameraForward;
float4x4 World;
float4x4 WorldInverseTranspose;
float4x4 View;
float4x4 Projection;

bool ShadowPass;
bool Transparent;

float3 DiffuseLightDirection = float3(1, 0, 0);
float4 DiffuseColor = float4(1, 1, 1, 1);
float DiffuseIntensity = 1.0;

float shine = 0;

float Specular(float3 lightDir, float3 viewDir, float3 normal, float smul)
{
    float3 r = normalize(2.0 * dot(normal, lightDir) * normal - lightDir);
    float ndotl = max(0.0001f, dot(normal, lightDir));
    float rdotv = max(0.0f, dot(r, viewDir));
    return ndotl * pow(rdotv, smul * shine);
}

float4 CalculateSpecularLighting(float3 worldpos, float3 normal, float3 viewVector, float specularIntensity, float smul)
{
    float3 total = float3(0, 0, 0);

    total += saturate(specularIntensity * max(Specular(DiffuseLightDirection, viewVector, normal, smul), 0)
                     * DiffuseColor.rgb * DiffuseIntensity);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        float3 ldir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1 - saturate(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w);
        float3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        float theta = acos(dot(ldir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0) ? 0.0 : 1.0;

        total += saturate(specularIntensity * max(Specular(ldir, viewVector, normal, smul), 0) * lc);
    }

    [loop]
    for (int j = 0; j < realtimeLightCount; j++)
    {
        float3 ldir = normalize(realtimeLightPositions[j].xyz - worldpos);
        float att = saturate((realtimeLightPositions[j].a
            - distance(worldpos, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a);

        float3 lc = realtimeLightColors[j].rgb * realtimeLightColors[j].a * att * att;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            lc *= sqrt(saturate(spotAng - ang) / spotAng);
        }

        total += saturate(specularIntensity * max(Specular(ldir, viewVector, normal, smul), 0) * lc);
    }

    return float4(total, 0);
}
float3 CalculateStaticLighting(float3 worldpos, float3 worldnorm)
{
    float3 total = float3(0, 0, 0);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        float3 lightdir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1 - saturate(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w);
        float3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        lc *= dot(lightdir, worldnorm) * 0.5 + 0.5;

        float theta = acos(dot(lightdir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0) ? 0.0 : 1.0;

        total += lc;
    }
    return total;
}

float3 EvaluateSH(float3 coeffs[SHCOEFFICIENTS], float3 d)
{
    return coeffs[0] * 0.282095f
         + coeffs[1] * (0.488603f * d.y)
         + coeffs[2] * (0.488603f * d.z)
         + coeffs[3] * (0.488603f * d.x)
         + coeffs[4] * (1.092548f * d.x * d.y)
         + coeffs[5] * (1.092548f * d.y * d.z)
         + coeffs[6] * (0.315392f * (3.0f * d.z * d.z - 1.0f))
         + coeffs[7] * (1.092548f * d.x * d.z)
         + coeffs[8] * (0.546274f * (d.x * d.x - d.y * d.y));
}

float3 CalculateCombinedLighting(
    float3 worldpos, float3 N, float3 V,
    float specInt, float specSmul,
    out float3 diffuseOut)
{
    float3 diff = float3(0, 0, 0);
    float3 spec = float3(0, 0, 0);

    diff += (DiffuseColor.rgb * DiffuseIntensity * (dot(N, DiffuseLightDirection) * 0.5 + 0.5));
    spec += saturate(specInt * max(Specular(DiffuseLightDirection, V, N, specSmul), 0)
                     * DiffuseColor.rgb * DiffuseIntensity);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        float3 ldir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1 - saturate(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w);
        float3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        float theta = acos(dot(ldir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0) ? 0.0 : 1.0;

        diff += lc * ((dot(ldir, N) * 0.5 + 0.5) * (dot(ldir, N) * 0.5 + 0.5));
        spec += saturate(specInt * max(Specular(ldir, V, N, specSmul), 0) * lc);
    }

    [loop]
    for (int j = 0; j < realtimeLightCount; j++)
    {
        float3 ldir = normalize(realtimeLightPositions[j].xyz - worldpos);
        float att = saturate((realtimeLightPositions[j].a
            - distance(worldpos, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a);

        float3 lc = realtimeLightColors[j].rgb * realtimeLightColors[j].a * att * att;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            lc *= sqrt(saturate(spotAng - ang) / spotAng);
        }

        diff += lc * (dot(ldir, N) * 0.5 + 0.5);
        spec += saturate(specInt * max(Specular(ldir, V, N, specSmul), 0) * lc);
    }

    diffuseOut = diff;
    return spec;
}

PS_OUTPUT BasicModelPixelShaderCore(VertexShaderOutput input, bool ShadowPass, float4 texColor, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 specColor = tex2D(specularSampler, input.TextureCoordinate);

    float3 bump = (2.0 * tex2D(normalSampler, input.TextureCoordinate).xyz) - 1.0;
    float3 N = normalize(input.Normal + bump.x * input.Tangent + bump.y * input.Binormal);
    float3 V = normalize(cameraPos - input.WorldPos.xyz);

    float3 diffuse, specular;
    specular = CalculateCombinedLighting(input.WorldPos.xyz, N, V,
                                         specColor.r, specColor.g, diffuse);

    float3 ambient = EvaluateSH(indirectSH, N);

    float3 reflection = float3(0, 0, 0);

    if (specColor.b > 0.01f)
        reflection = CubemapSample(reflect(-V, N)) * specColor.b;

    float3 lit = ApplyLight(texColor.xyz, diffuse + ambient) + specular + reflection;

    return WriteSceneOutput(float4(ApplyFog(float4(lit, texColor.a), input.WorldPos.xyz).rgb, texColor.a));
}

PS_OUTPUT BasicModelPixelShader(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!Transparent)
    {
        clip(texColor.a - 0.8f);
        texColor.a = 1;
    }

    return BasicModelPixelShaderCore(input, ShadowPass, texColor, specularSampler, normalSampler);
}

PS_OUTPUT BasicModelPixelShader_NoClip(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    return BasicModelPixelShaderCore(input, ShadowPass, texColor, specularSampler, normalSampler);
}
PS_OUTPUT BasicModelPixelShader_MedCore(VertexShaderOutput input, bool ShadowPass, float4 texColor, float4 specColor)
{
    float3 N = input.Normal;
    float3 V = normalize(cameraPos - input.WorldPos.xyz);

    float3 diffuse, specular;
    specular = CalculateCombinedLighting(input.WorldPos.xyz, N, V,
                                         specColor.r, specColor.g, diffuse);

    float3 ambient = EvaluateSH(indirectSH, N);
    float3 specTerm = specular * length(input.Color.xyz);
    float3 lightTerm = diffuse + ambient + specTerm;
    float3 lit = ApplyLight(texColor.rgb, diffuse + ambient) + specTerm;

    return WriteSceneOutput(float4(ApplyFog(float4(lit, texColor.a), input.WorldPos.xyz).rgb, texColor.a));
}

PS_OUTPUT BasicModelPixelShader_Med(VertexShaderOutput input, bool ShadowPass,
    sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);
    float4 specColor = tex2D(specularSampler, input.TextureCoordinate);

    if (!Transparent)
    {
        clip(texColor.a - 0.8f);
        texColor.a = 1;
    }

    return BasicModelPixelShader_MedCore(input, ShadowPass, texColor, specColor);
}

PS_OUTPUT BasicModelPixelShader_Med_NoClip(VertexShaderOutput input, bool ShadowPass,
    sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);
    float4 specColor = tex2D(specularSampler, input.TextureCoordinate);

    return BasicModelPixelShader_MedCore(input, ShadowPass, texColor, specColor);
}

PS_OUTPUT BasicModelPixelShader_LowCore(VertexShaderOutput input, bool ShadowPass, float4 texColor)
{
    float3 light = input.Color.rgb;

    [loop]
    for (int j = 0; j < realtimeLightCount; j++)
    {
        float3 ldir = normalize(realtimeLightPositions[j].xyz - input.WorldPos.xyz);
        float att = saturate((realtimeLightPositions[j].a
            - distance(input.WorldPos.xyz, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a);
        att *= dot(ldir, input.Normal) * 0.5 + 0.5;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            att *= sqrt(saturate(spotAng - ang) / spotAng);
        }

        light += realtimeLightColors[j].rgb * realtimeLightColors[j].a * att;
    }

    float3 lit = ApplyLight(texColor.rgb, light);
    return WriteSceneOutput(float4(ApplyFog(float4(lit, texColor.a), input.WorldPos.xyz).rgb, texColor.a));
}

PS_OUTPUT BasicModelPixelShader_Low(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!Transparent)
    {
        clip(texColor.a - 0.8f);
        texColor.a = 1;
    }

    return BasicModelPixelShader_LowCore(input, ShadowPass, texColor);
}

PS_OUTPUT BasicModelPixelShader_Low_NoClip(VertexShaderOutput input, bool ShadowPass,
    sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler)
{
    float4 texColor = tex2D(textureSampler, input.TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    return BasicModelPixelShader_LowCore(input, ShadowPass, texColor);
}
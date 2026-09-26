#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "ModelCommon.fxh"

#define MAXBONES 200
#define MAXEYES 4

float4x3 Bones[MAXBONES];

float4 EyeCenterRadius[MAXEYES]; // xyz = eye center (world), w = eyeball radius
float3 EyeForward[MAXEYES];
float3 EyeRight[MAXEYES];
float3 EyeUp[MAXEYES];
float ThetaFOV[MAXEYES];

// Anatomy ratios: cornea vs. eyeball curvature and depth. Fixed across all eyes,
// same reasoning as CorneaEta below, these describe "an eye" not "this eye."
static const float IrisPlaneDepthRatio = 0.9;
static const float LimbusBlendRatio = 0.08;

// Refractive index ratio air -> cornea (~1.336)
static const float CorneaEta = 1.0 / 1.336;

// Fresnel is a property of the cornea material itself, not any individual eye, so these
// stay as plain globals rather than per-eye arrays.
const float FresnelPower = 2;
const float FresnelIntensity = 1;
float IrisSize;

texture MainTex;
sampler2D textureSampler = sampler_state
{
    Texture = (MainTex);
    AddressU = Clamp;
    AddressV = Clamp;
};
texture DataTex;
sampler2D dataSampler = sampler_state
{
    Texture = (DataTex);
    MagFilter = Linear;
    MinFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
struct VSInputNmTxWeights
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Binormal : BINORMAL;
    float2 TexCoord : TEXCOORD0;
    int4 Indices : BLENDINDICES0;
    int EyeIndex : BLENDINDICES1;
    float4 Weights : BLENDWEIGHT0;
};

struct EyeVertexShaderOutput
{
    float4 Position : POSITION0;
    float4 WorldPos : COLOR1;
    int EyeIndex : TEXCOORD0;
};

void Skin(inout VSInputNmTxWeights vin, uniform int boneCount)
{
    float4x3 skinning = 0;

    [unroll]
    for (int i = 0; i < boneCount; i++)
    {
        skinning += Bones[vin.Indices[i]] * vin.Weights[i];
    }

    vin.Position.xyz = mul(vin.Position, skinning);
    vin.Normal = mul(vin.Normal, (float3x3) skinning);
}

EyeVertexShaderOutput MainVS_Low(in VSInputNmTxWeights input)
{
    EyeVertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);

    output.EyeIndex = input.EyeIndex;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}
EyeVertexShaderOutput VSFourBone_Low(in VSInputNmTxWeights input)
{
    Skin(input, 4);

    EyeVertexShaderOutput output = MainVS_Low(input);

    return output;
}

PS_OUTPUT MainPS(EyeVertexShaderOutput input)
{
    float3 eyeCenter = lerp(EyeCenterRadius[0].xyz, EyeCenterRadius[1].xyz, input.EyeIndex);
    float eyeRadius = lerp(EyeCenterRadius[0].w, EyeCenterRadius[1].w, input.EyeIndex);
    
    if (eyeRadius <= 0.0001)
    {
        PS_OUTPUT invalid;
        invalid.Color = float4(0, 0, 0, 1);
        return invalid;
    }
    
    float3 eyeForward = normalize(lerp(EyeForward[0], EyeForward[1], input.EyeIndex));
    float3 eyeRight = normalize(lerp(EyeRight[0], EyeRight[1], input.EyeIndex));
    float3 eyeUp = normalize(lerp(EyeUp[0], EyeUp[1], input.EyeIndex));
    float thetaFOV = lerp(ThetaFOV[0], ThetaFOV[1], input.EyeIndex);

    float corneaLimbusAngle = atan(IrisSize * tan(thetaFOV));
    float limbusBlend = corneaLimbusAngle * LimbusBlendRatio;
    float CorneaRadiusRatio = IrisSize + 0.1f;
    float corneaRadius = eyeRadius * CorneaRadiusRatio;
    float irisPlaneDepth = eyeRadius * IrisPlaneDepthRatio;
    float irisPhysicalRadius = irisPlaneDepth * tan(corneaLimbusAngle);

    // Surface normal at this fragment, purely geometric, no view-dependence yet.
    float3 eyeNormal = normalize(input.WorldPos.xyz - eyeCenter);

    // View ray from camera to surface: everything view-dependent (refraction, Fresnel) is built off this.
    float3 V = normalize(input.WorldPos.xyz - cameraPos);

    // Whole-eye gnomonic projection, used for the sclera/data texture.
    // tan(theta) diverges as theta approaches 90 degrees, so projScale rescales things such that
    // ThetaFOV (the angular radius of the visible eye opening) maps exactly to the UV edge.
    float projScale = 1.0 / tan(thetaFOV);
    float denom = max(dot(eyeNormal, eyeForward), 0.0001);
    float x = dot(eyeNormal, eyeRight) * projScale / denom;
    float y = dot(eyeNormal, eyeUp) * projScale / denom;
    float2 eyeTexUV = float2(x, y) * 0.5 + 0.5;
    eyeTexUV.y = 1 - eyeTexUV.y;

    // Limbus boundary: how far into the iris/cornea region this fragment is.
    float theta = acos(saturate(dot(eyeNormal, eyeForward)));
    float irisMask = 1.0 - smoothstep(corneaLimbusAngle - limbusBlend, corneaLimbusAngle, theta);

    // A smaller-radius sphere reaches the same angular opening faster than the eyeball itself,
    // so the same physical point maps to a larger angle on the cornea than on the sclera.
    float sinTheta = sin(theta);
    float corneaCurvatureRatio = eyeRadius / corneaRadius;
    float thetaCornea = asin(saturate(sinTheta * corneaCurvatureRatio));

    float cosPhi = dot(eyeNormal, eyeRight) / max(sinTheta, 0.0001);
    float sinPhi = dot(eyeNormal, eyeUp) / max(sinTheta, 0.0001);

    float3 corneaNormal = eyeForward * cos(thetaCornea)
                        + (eyeRight * cosPhi + eyeUp * sinPhi) * sin(thetaCornea);

    // Refract the view ray through the cornea (Snell's law) and hit the flat iris plane.
    // This is what makes the iris look like a flat disc seen through curved glass, rather than
    // a texture wrapped onto the sphere: the UV now depends on view angle, not just surface position.
    float3 T = refract(V, corneaNormal, CorneaEta);

    // refract() returns exactly (0,0,0) on total internal reflection, and any
    // near-degenerate ray/plane angle blows tHit up toward Inf/NaN once divided
    // through. That used to be invisible when the eye had no real lighting
    // multiplying it, now it shows up as scattered garbage pixels. Detect it
    // and fall back to the plain sclera projection for that fragment.
    float3 irisPlanePoint = eyeCenter + eyeForward * irisPlaneDepth;
    float denomPlane = dot(T, eyeForward);

    bool validRefraction = dot(T, T) > 0.0001 && abs(denomPlane) > 0.0001;

    float tHit = dot(irisPlanePoint - input.WorldPos.xyz, eyeForward) / (validRefraction ? denomPlane : 1.0);
    float3 hitPoint = input.WorldPos.xyz + T * tHit;

    float3 localHit = hitPoint - irisPlanePoint;
    float2 irisUV = float2(dot(localHit, eyeRight), dot(localHit, eyeUp)) / irisPhysicalRadius;
    irisUV = irisUV * (IrisSize * 0.5) + 0.5;
    irisUV.y = 1 - irisUV.y;

    float safeIrisMask = validRefraction ? irisMask : 0.0;

    // Blend between the refracted iris lookup and the plain sclera lookup at the limbus.
    float2 finalUV = lerp(eyeTexUV, irisUV, safeIrisMask);

    float4 color = tex2D(textureSampler, finalUV);
    float4 eyeData = tex2D(dataSampler, finalUV);

    float2 bumpXY = eyeData.rg * 2.0 - 1.0;
    float bumpZ = sqrt(saturate(1.0 - dot(bumpXY, bumpXY)));
    float3 tangentBump = float3(bumpXY, bumpZ);
    float specularMask = eyeData.a;

    // Base shading normal: plain sphere on the sclera, tighter corneal curvature on the iris,
    // then bump-perturbed by the baked fiber/vein detail either way.
    float3 baseNormal = normalize(lerp(eyeNormal, corneaNormal, irisMask));
    float3 N = normalize(baseNormal + tangentBump.x * eyeRight + tangentBump.y * eyeUp);

    float3 diffuse;
    float3 specularBase = CalculateCombinedLighting(input.WorldPos.xyz, corneaNormal, -V, specularMask, 1.0, diffuse);

    float3 diffuseUnused;
    float3 specularDetail = CalculateCombinedLighting(input.WorldPos.xyz, N, -V, specularMask, 1.0, diffuseUnused);

    float3 ambient = EvaluateSH(indirectSH, N);

    float3 lit = ApplyLight(color.xyz, diffuse + ambient) + specularBase;

    float fresnel = pow(1.0 - saturate(dot(N, -V)), FresnelPower) * irisMask;
    lit += fresnel * FresnelIntensity;

    PS_OUTPUT p;
    
    p.Color = float4(ApplyFog(float4(lit, color.a), input.WorldPos.xyz).rgb, 1);
    return p;
}

technique High
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSFourBone_Low();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
using MapCompiler.Compilation;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MapCompiler
{
    public struct LightSample
    {
        public LightmapColor B1, B2, B3;
        public float ShadowLight, MinDist;
    }

    public static class LightCalculator
    {
        public static Color AmbientColor = Color.Black;
        public static float AmbientIntensity = 0f;

        private static readonly float[] Randoms = new float[256];

        static LightCalculator()
        {
            for (int i = 0; i < 256; i++)
                Randoms[i] = Random.Shared.NextSingle();
        }

        public static LightSample FromPointBrushShadowKnown(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            bool inShadow)
        {
            float dist = Vector3.Distance(point, light.position);
            if (dist > light.range || dist == 0f)
                return default;

            float shadowLight = inShadow ? 0f : 1f;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float dot = float.Clamp(Vector3.Dot(L, basis1), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(L, basis2), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(L, basis3), 0f, 1f);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 3f);
            float intensityTotal = light.intensity * dot * attn * shadowLight;
            float intensityTangent = light.intensity * tdot * attn * shadowLight;
            float intensityBinorm = light.intensity * bdot * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }

        public static LightSample FromDirectionalBrushShadowKnown(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            bool inShadow)
        {
            float dot = float.Clamp(Vector3.Dot(basis1, light.rotation), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(basis2, light.rotation), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(basis3, light.rotation), 0f, 1f);

            float shadowLight = inShadow ? 0f : light.intensity;

            var lCol = new LightmapColor(light.color.R * (shadowLight * dot), light.color.G * (shadowLight * dot), light.color.B * (shadowLight * dot));
            var tCol = new LightmapColor(light.color.R * (shadowLight * tdot), light.color.G * (shadowLight * tdot), light.color.B * (shadowLight * tdot));
            var bCol = new LightmapColor(light.color.R * (shadowLight * bdot), light.color.G * (shadowLight * bdot), light.color.B * (shadowLight * bdot));

            return new LightSample
            {
                B1 = new LightmapColor((lCol.R / 255f), (lCol.G / 255f), (lCol.B / 255f)),
                B2 = new LightmapColor((tCol.R / 255f), (tCol.G / 255f), (tCol.B / 255f)),
                B3 = new LightmapColor((bCol.R / 255f), (bCol.G / 255f), (bCol.B / 255f)),
            };
        }

        public static LightSample FromSpotBrushShadowKnown(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            bool inShadow)
        {
            float pdot = Vector3.Dot(Vector3.Normalize(point - light.position), -light.rotation);
            float angle = (float)Math.Acos(pdot);
            float maxAng = MathHelper.ToRadians(light.angle);
            float innerAng = MathHelper.ToRadians(light.innerAngle);
            float dist = Vector3.Distance(point, light.position);

            if (angle > maxAng || dist > light.range || dist == 0f)
                return default;

            float falloff = angle > innerAng
                ? ((maxAng - innerAng) - (angle - innerAng)) / (maxAng - innerAng)
                : 1f;
            float shadowLight = inShadow ? 0f : falloff;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float ndot = Vector3.Dot(basis1, L);
            float tdot = Vector3.Dot(basis2, L);
            float bdot = Vector3.Dot(basis3, L);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 3f);
            float intensityTotal = light.intensity * (Math.Abs(ndot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityTangent = light.intensity * (Math.Abs(tdot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityBinorm = light.intensity * (Math.Abs(bdot) * 0.4f + 0.6f) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }

        public static LightSample FromPointShadowKnown(
    Light light, Vector3 point,
    Vector3 basis1, Vector3 basis2, Vector3 basis3, bool inShadow)
        {
            float dist = Vector3.Distance(point, light.position);
            if (dist > light.range || dist == 0f) return default;

            float shadowLight = inShadow ? 0f : 1f;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float dot = float.Clamp(Vector3.Dot(L, basis1), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(L, basis2), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(L, basis3), 0f, 1f);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 2);
            float i1 = light.intensity * dot * attn * shadowLight;
            float i2 = light.intensity * tdot * attn * shadowLight;
            float i3 = light.intensity * bdot * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * i1, light.color.G * i1, light.color.B * i1),
                B2 = new LightmapColor(light.color.R * i2, light.color.G * i2, light.color.B * i2),
                B3 = new LightmapColor(light.color.R * i3, light.color.G * i3, light.color.B * i3),
            };
        }

        public static LightSample FromDirectionalShadowKnown(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3, bool inShadow)
        {
            float shadowLight = inShadow ? 0f : light.intensity;

            float dot = float.Clamp(Vector3.Dot(basis1, light.rotation), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(basis2, light.rotation), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(basis3, light.rotation), 0f, 1f);

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * (shadowLight * dot) / 255f, light.color.G * (shadowLight * dot) / 255f, light.color.B * (shadowLight * dot) / 255f),
                B2 = new LightmapColor(light.color.R * (shadowLight * tdot) / 255f, light.color.G * (shadowLight * tdot) / 255f, light.color.B * (shadowLight * tdot) / 255f),
                B3 = new LightmapColor(light.color.R * (shadowLight * bdot) / 255f, light.color.G * (shadowLight * bdot) / 255f, light.color.B * (shadowLight * bdot) / 255f),
            };
        }

        public static LightSample FromSpotShadowKnown(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3, bool inShadow)
        {
            float pdot = Vector3.Dot(Vector3.Normalize(point - light.position), -light.rotation);
            float angle = (float)Math.Acos(pdot);
            float maxAng = MathHelper.ToRadians(light.angle);
            float innerAng = MathHelper.ToRadians(light.innerAngle);
            float dist = Vector3.Distance(point, light.position);

            if (angle > maxAng || dist > light.range || dist == 0f) return default;

            float falloff = angle > innerAng
                ? ((maxAng - innerAng) - (angle - innerAng)) / (maxAng - innerAng)
                : 1f;
            float shadowLight = inShadow ? 0f : falloff;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float ndot = Vector3.Dot(basis1, L);
            float tdot = Vector3.Dot(basis2, L);
            float bdot = Vector3.Dot(basis3, L);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 1.5f);
            float i1 = light.intensity * (Math.Abs(ndot) * 0.4f + 0.6f) * attn * shadowLight;
            float i2 = light.intensity * (Math.Abs(tdot) * 0.4f + 0.6f) * attn * shadowLight;
            float i3 = light.intensity * (Math.Abs(bdot) * 0.4f + 0.6f) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * i1, light.color.G * i1, light.color.B * i1),
                B2 = new LightmapColor(light.color.R * i2, light.color.G * i2, light.color.B * i2),
                B3 = new LightmapColor(light.color.R * i3, light.color.G * i3, light.color.B * i3),
            };
        }

        public static LightSample FromPoint(
            Light light, Vector3 point,
            ref Brush[] brushes,
            int brushIdx, int faceIdx)
        {
            var minDist = float.MaxValue;
            var shadowLight = 0f;

            float dist = Vector3.Distance(point, light.position);
            if (dist > light.range || dist == 0f)
                return default;

            // Trace from surface point toward the light, excluding self-brush
            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist, default, brushIdx);
            if (hit.hit)
                minDist = Vector3.Distance(hit.point, point);

            bool inShadow = minDist < dist;

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, dist, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(brushIdx));
            }

            shadowLight = inShadow ? 0f : 1f;

            float dot = float.Clamp(Vector3.Dot(-Vector3.Normalize(point - light.position), brushes[brushIdx].faces[faceIdx].basis1), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(-Vector3.Normalize(point - light.position), brushes[brushIdx].faces[faceIdx].basis2), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(-Vector3.Normalize(point - light.position), brushes[brushIdx].faces[faceIdx].basis3), 0f, 1f);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 2);
            float intensityTotal = light.intensity * (dot) * attn * shadowLight;
            float intensityTangent = light.intensity * (tdot) * attn * shadowLight;
            float intensityBinorm = light.intensity * (bdot) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }

        public static LightSample FromDirectional(
            Light light, Vector3 point,
            ref Brush[] brushes,
            int brushIdx, int faceIdx)
        {
            var shadowLight = 0f;
            var minDist = float.MaxValue;

            var ray = new Ray(point, light.rotation);
            bool inShadow = true;

            BSPHit hit = BSPRoot.TraceRay(ray, 512f);
            if (hit.hit)
                inShadow = BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode;

            float dot = float.Clamp(Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis1, light.rotation), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis2, light.rotation), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis3, light.rotation), 0f, 1f);

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, minDist, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(brushIdx));
            }

            shadowLight = inShadow ? 0f : light.intensity;

            var lCol = new LightmapColor(light.color.R * (shadowLight * dot), light.color.G * (shadowLight * dot), light.color.B * (shadowLight * dot));
            var tCol = new LightmapColor(light.color.R * (shadowLight * tdot), light.color.G * (shadowLight * tdot), light.color.B * (shadowLight * tdot));
            var bCol = new LightmapColor(light.color.R * (shadowLight * bdot), light.color.G * (shadowLight * bdot), light.color.B * (shadowLight * bdot));

            return new LightSample
            {
                B1 = new LightmapColor((lCol.R / 255f), (lCol.G / 255f), (lCol.B / 255f)),
                B2 = new LightmapColor((tCol.R / 255f), (tCol.G / 255f), (tCol.B / 255f)),
                B3 = new LightmapColor((bCol.R / 255f), (bCol.G / 255f), (bCol.B / 255f)),
            };
        }

        public static LightSample FromSpot(
            Light light, Vector3 point,
            ref Brush[] brushes,
            int brushIdx, int faceIdx)
        {
            var minDist = float.MaxValue;
            var shadowLight = 0f;

            float pdot = Vector3.Dot(Vector3.Normalize(point - light.position), -light.rotation);
            float angle = (float)Math.Acos(pdot);
            float maxAng = MathHelper.ToRadians(light.angle);
            float innerAng = MathHelper.ToRadians(light.innerAngle);
            float dist = Vector3.Distance(point, light.position);

            if (angle > maxAng || dist > light.range || dist == 0f)
                return default;

            // Trace from surface point toward light, excluding self-brush
            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist, default, brushIdx);
            if (hit.hit)
                minDist = Vector3.Distance(hit.point, point);

            bool inShadow = minDist < dist;

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, dist, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(brushIdx));
            }

            float falloff = angle > innerAng
                ? ((maxAng - innerAng) - (angle - innerAng)) / (maxAng - innerAng)
                : 1f;
            shadowLight = inShadow ? 0f : falloff;

            float ndot = Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis1, -Vector3.Normalize(point - light.position));
            float tdot = Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis2, -Vector3.Normalize(point - light.position));
            float bdot = Vector3.Dot(brushes[brushIdx].faces[faceIdx].basis3, -Vector3.Normalize(point - light.position));

            float attn = (float)Math.Pow((light.range - dist) / light.range, 1.5f);
            float intensityTotal = light.intensity * (Math.Abs(ndot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityTangent = light.intensity * (Math.Abs(tdot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityBinorm = light.intensity * (Math.Abs(bdot) * 0.4f + 0.6f) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }

        public static LightSample FromPoint(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3, int excludeTerrain = -1)
        {
            float dist = Vector3.Distance(point, light.position);
            if (dist > light.range || dist == 0f) return default;

            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist);
            bool inShadow = hit.hit && Vector3.Distance(hit.point, point) < dist;

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, dist, excludeTerrain);
            }
            float shadowLight = inShadow ? 0f : 1f;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float dot = float.Clamp(Vector3.Dot(L, basis1), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(L, basis2), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(L, basis3), 0f, 1f);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 2);
            float i1 = light.intensity * dot * attn * shadowLight;
            float i2 = light.intensity * tdot * attn * shadowLight;
            float i3 = light.intensity * bdot * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * i1, light.color.G * i1, light.color.B * i1),
                B2 = new LightmapColor(light.color.R * i2, light.color.G * i2, light.color.B * i2),
                B3 = new LightmapColor(light.color.R * i3, light.color.G * i3, light.color.B * i3),
            };
        }

        public static LightSample FromDirectional(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3, int excludeTerrain = -1)
        {
            var ray = new Ray(point, light.rotation);
            BSPHit hit = BSPRoot.TraceRay(ray, 512f);
            bool inShadow = hit.hit && BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode;
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, 512f, excludeTerrain);
            }
            float shadowLight = inShadow ? 0f : light.intensity;

            float dot = float.Clamp(Vector3.Dot(basis1, light.rotation), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(basis2, light.rotation), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(basis3, light.rotation), 0f, 1f);

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * (shadowLight * dot),
                                       light.color.G * (shadowLight * dot),
                                       light.color.B * (shadowLight * dot)),
                B2 = new LightmapColor(light.color.R * (shadowLight * tdot),
                                       light.color.G * (shadowLight * tdot),
                                       light.color.B * (shadowLight * tdot)),
                B3 = new LightmapColor(light.color.R * (shadowLight * bdot),
                                       light.color.G * (shadowLight * bdot),
                                       light.color.B * (shadowLight * bdot)),
            };
        }

        public static LightSample FromSpot(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3, int excludeTerrain = -1)
        {
            float pdot = Vector3.Dot(Vector3.Normalize(point - light.position), -light.rotation);
            float angle = (float)Math.Acos(pdot);
            float maxAng = MathHelper.ToRadians(light.angle);
            float innerAng = MathHelper.ToRadians(light.innerAngle);
            float dist = Vector3.Distance(point, light.position);

            if (angle > maxAng || dist > light.range || dist == 0f) return default;

            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist);
            bool inShadow = hit.hit && Vector3.Distance(hit.point, point) < dist;

            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, 512f, excludeTerrain);
            }

            float falloff = angle > innerAng
                ? ((maxAng - innerAng) - (angle - innerAng)) / (maxAng - innerAng)
                : 1f;
            float shadowLight = inShadow ? 0f : falloff;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float ndot = Vector3.Dot(basis1, L);
            float tdot = Vector3.Dot(basis2, L);
            float bdot = Vector3.Dot(basis3, L);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 1.5f);
            float i1 = light.intensity * (Math.Abs(ndot) * 0.4f + 0.6f) * attn * shadowLight;
            float i2 = light.intensity * (Math.Abs(tdot) * 0.4f + 0.6f) * attn * shadowLight;
            float i3 = light.intensity * (Math.Abs(bdot) * 0.4f + 0.6f) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * i1, light.color.G * i1, light.color.B * i1),
                B2 = new LightmapColor(light.color.R * i2, light.color.G * i2, light.color.B * i2),
                B3 = new LightmapColor(light.color.R * i3, light.color.G * i3, light.color.B * i3),
            };
        }
        public static LightSample FromPointBrush(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            int excludeBrush)
        {
            var minDist = float.MaxValue;

            float dist = Vector3.Distance(point, light.position);
            if (dist > light.range || dist == 0f)
                return default;

            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist, default, excludeBrush);
            if (hit.hit)
                minDist = Vector3.Distance(hit.point, point);

            bool inShadow = minDist < dist;

            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, dist, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(excludeBrush));
            }

            float shadowLight = inShadow ? 0f : 1f;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float dot = float.Clamp(Vector3.Dot(L, basis1), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(L, basis2), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(L, basis3), 0f, 1f);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 3f);
            float intensityTotal = light.intensity * dot * attn * shadowLight;
            float intensityTangent = light.intensity * tdot * attn * shadowLight;
            float intensityBinorm = light.intensity * bdot * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }

        public static LightSample FromDirectionalBrush(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            int excludeBrush)
        {
            var ray = new Ray(point, light.rotation);
            bool inShadow = true;

            BSPHit hit = BSPRoot.TraceRay(ray, 512f, default, excludeBrush);
            if (hit.hit)
                inShadow = BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode;

            float dot = float.Clamp(Vector3.Dot(basis1, light.rotation), 0f, 1f);
            float tdot = float.Clamp(Vector3.Dot(basis2, light.rotation), 0f, 1f);
            float bdot = float.Clamp(Vector3.Dot(basis3, light.rotation), 0f, 1f);

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, 512f, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(excludeBrush));
            }

            float shadowLight = inShadow ? 0f : light.intensity;

            var lCol = new LightmapColor(light.color.R * (shadowLight * dot), light.color.G * (shadowLight * dot), light.color.B * (shadowLight * dot));
            var tCol = new LightmapColor(light.color.R * (shadowLight * tdot), light.color.G * (shadowLight * tdot), light.color.B * (shadowLight * tdot));
            var bCol = new LightmapColor(light.color.R * (shadowLight * bdot), light.color.G * (shadowLight * bdot), light.color.B * (shadowLight * bdot));

            return new LightSample
            {
                B1 = new LightmapColor((lCol.R / 255f), (lCol.G / 255f), (lCol.B / 255f)),
                B2 = new LightmapColor((tCol.R / 255f), (tCol.G / 255f), (tCol.B / 255f)),
                B3 = new LightmapColor((bCol.R / 255f), (bCol.G / 255f), (bCol.B / 255f)),
            };
        }

        public static LightSample FromSpotBrush(
            Light light, Vector3 point,
            Vector3 basis1, Vector3 basis2, Vector3 basis3,
            int excludeBrush)
        {
            var minDist = float.MaxValue;

            float pdot = Vector3.Dot(Vector3.Normalize(point - light.position), -light.rotation);
            float angle = (float)Math.Acos(pdot);
            float maxAng = MathHelper.ToRadians(light.angle);
            float innerAng = MathHelper.ToRadians(light.innerAngle);
            float dist = Vector3.Distance(point, light.position);

            if (angle > maxAng || dist > light.range || dist == 0f)
                return default;

            // Trace from surface point toward light, excluding self-brush
            var ray = new Ray(point, light.position - point);
            BSPHit hit = BSPRoot.TraceRay(ray, dist, default, excludeBrush);
            if (hit.hit)
                minDist = Vector3.Distance(hit.point, point);

            bool inShadow = minDist < dist;

            // Also check non-BSP occluders (terrains, detail brushes)
            if (!inShadow)
            {
                inShadow = TriangleOccluder.TraceRay(ray, dist, selfEntityGroup: TriangleOccluder.GetBrushEntityGroup(excludeBrush));
            }

            float falloff = angle > innerAng
                ? ((maxAng - innerAng) - (angle - innerAng)) / (maxAng - innerAng)
                : 1f;
            float shadowLight = inShadow ? 0f : falloff;

            Vector3 L = -Vector3.Normalize(point - light.position);
            float ndot = Vector3.Dot(basis1, L);
            float tdot = Vector3.Dot(basis2, L);
            float bdot = Vector3.Dot(basis3, L);

            float attn = (float)Math.Pow((light.range - dist) / light.range, 3f);
            float intensityTotal = light.intensity * (Math.Abs(ndot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityTangent = light.intensity * (Math.Abs(tdot) * 0.4f + 0.6f) * attn * shadowLight;
            float intensityBinorm = light.intensity * (Math.Abs(bdot) * 0.4f + 0.6f) * attn * shadowLight;

            return new LightSample
            {
                B1 = new LightmapColor(light.color.R * intensityTotal, light.color.G * intensityTotal, light.color.B * intensityTotal),
                B2 = new LightmapColor(light.color.R * intensityTangent, light.color.G * intensityTangent, light.color.B * intensityTangent),
                B3 = new LightmapColor(light.color.R * intensityBinorm, light.color.G * intensityBinorm, light.color.B * intensityBinorm),
            };
        }
        [ThreadStatic] private static int _threadRandIdx;
        [ThreadStatic] private static bool _threadRandInit;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetRandom()
        {
            if (!_threadRandInit)
            {
                _threadRandIdx = Thread.CurrentThread.ManagedThreadId * 37;
                _threadRandInit = true;
            }
            _threadRandIdx = (_threadRandIdx + 1) & 255;
            return Randoms[_threadRandIdx];
        }
        public static float CastCheckAmbient(Vector3 point, Vector3 normal)
        {
            const int NumSamples = 64;
            const float InvNumSamples = 1f / NumSamples;

            GeometryUtils.BuildOrthonormalBasis(normal, out Vector3 T, out Vector3 B);

            float jU = GetRandom();
            float jV = GetRandom();
            float visible = 0f;

            for (int i = 0; i < NumSamples; i++)
            {
                float u = (GeometryUtils.HaltonU[i] + jU) % 1f;
                float v = (GeometryUtils.HaltonV[i] + jV) % 1f;
                float phi = 2f * MathF.PI * u;
                float cosT = MathF.Sqrt(v);
                float sinT = MathF.Sqrt(1f - v);
                Vector3 dir = sinT * MathF.Cos(phi) * T
                            + sinT * MathF.Sin(phi) * B
                            + cosT * normal;

                BSPHit hit = BSPRoot.TraceRay(new Ray(point, dir), 256f);
                if (!hit.hit || BSPRoot.nodes[hit.node].nodeFlag == BSPNode.SkyboxNode)
                    visible += InvNumSamples;
            }

            return visible;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HashToUnitFloat(int x, int y, int salt)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2654435761u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;
        }

        public static float CastCheckDirtFine(Vector3 point, Vector3 normal, int seedX, int seedY)
        {
            const int NumSamples = 16;
            const float InvNumSamples = 1f / NumSamples;

            GeometryUtils.BuildOrthonormalBasis(normal, out Vector3 T, out Vector3 B);

            float jU = HashToUnitFloat(seedX, seedY, 0);
            float jV = HashToUnitFloat(seedX, seedY, 1);
            float visible = 0f;
            const float MaxDist = 3f;
            const float InvMaxDist = 1 / MaxDist;

            for (int i = 0; i < NumSamples; i++)
            {
                float u = (GeometryUtils.HaltonU[i] + jU) % 1f;
                float v = (GeometryUtils.HaltonV[i] + jV) % 1f;
                float phi = 2f * MathF.PI * u;
                float cosT = MathF.Sqrt(v);
                float sinT = MathF.Sqrt(1f - v);
                Vector3 dir = sinT * MathF.Cos(phi) * T
                            + sinT * MathF.Sin(phi) * B
                            + cosT * normal;

                BSPHit hit = BSPRoot.TraceRay(new Ray(point, dir), MaxDist);
                var nonBSPHit = TriangleOccluder.TraceRay(new Ray(point, dir), MaxDist, out var hitNonBSP);
                if(nonBSPHit)
                {
                    float dst = Vector3.Distance(hitNonBSP, point);

                    visible += InvNumSamples * ((MaxDist - dst) * InvMaxDist);
                }
                if ((hit.hit && BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode))
                {
                    float dst = Vector3.Distance(hit.point, point);

                    visible += InvNumSamples * ((MaxDist - dst) * InvMaxDist);
                }
            }

            return visible;
        }
        public static float CastCheckDirt(Vector3 point, Vector3 normal)
        {
            const int NumSamples = 32;
            const float InvNumSamples = 1f / NumSamples;

            GeometryUtils.BuildOrthonormalBasis(normal, out Vector3 T, out Vector3 B);

            float jU = GetRandom();
            float jV = GetRandom();
            float visible = 0f;
            const float MaxDist = 0.6f;
            const float InvMaxDist = 1 / MaxDist;

            for (int i = 0; i < NumSamples; i++)
            {
                float u = (GeometryUtils.HaltonU[i] + jU) % 1f;
                float v = (GeometryUtils.HaltonV[i] + jV) % 1f;
                float phi = 2f * MathF.PI * u;
                float cosT = MathF.Sqrt(v);
                float sinT = MathF.Sqrt(1f - v);
                Vector3 dir = sinT * MathF.Cos(phi) * T
                            + sinT * MathF.Sin(phi) * B
                            + cosT * normal;

                BSPHit hit = BSPRoot.TraceRay(new Ray(point, dir), MaxDist);
                var nonBSPHit = TriangleOccluder.TraceRay(new Ray(point, dir), MaxDist, out var hitNonBSP);
                if (nonBSPHit)
                {
                    float dst = Vector3.Distance(hitNonBSP, point);

                    visible += InvNumSamples * ((MaxDist - dst) * InvMaxDist);
                }
                if (hit.hit && BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode)
                {
                    float dst = Vector3.Distance(hit.point, point);

                    visible += InvNumSamples * ((MaxDist - dst) * InvMaxDist);
                }
            }

            return visible;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CastCheckAmbientDir(Vector3 point, Vector3 direction)
        {
            BSPHit hit = BSPRoot.TraceRay(new Ray(point, direction), 256f);
            return (!hit.hit || BSPRoot.nodes[hit.node].nodeFlag == BSPNode.SkyboxNode) ? 1f : 0f;
        }
        public static bool TestPointOcclusion(Light light, Vector3 pos)
        {
            switch (light.type)
            {
                case Light.LightType.Point:
                    {
                        float dist = Vector3.Distance(pos, light.position);
                        if (dist > light.range || dist == 0f) return false;
                        float mindist = light.range;
                        var r = new Ray(light.position, pos - light.position);
                        var hit = BSPRoot.TraceRay(r, mindist);
                        if (hit.hit) mindist = Vector3.Distance(hit.point, light.position);
                        var nonBSPhit = TriangleOccluder.TraceRay(r, mindist);
                        return mindist < dist && MathF.Abs(dist - mindist) > 0.1f && mindist > 0.1f || nonBSPhit;
                    }
                case Light.LightType.Directional:
                    {
                        var hit = BSPRoot.TraceRay(new Ray(pos, light.rotation), float.MaxValue);
                        var nonBSPhit = TriangleOccluder.TraceRay(new Ray(pos, light.rotation), Vector3.Distance(hit.point, pos));
                        return hit.hit && BSPRoot.nodes[hit.node].nodeFlag != BSPNode.SkyboxNode || nonBSPhit;
                    }
                case Light.LightType.SpotLight:
                    {
                        float dist = Vector3.Distance(pos, light.position);
                        float pdot = Vector3.Dot(Vector3.Normalize(pos - light.position), -light.rotation);
                        float angle = MathF.Acos(pdot);
                        if (dist > light.range || dist == 0f || angle > MathHelper.ToRadians(light.angle)) return false;
                        float mindist = light.range;
                        var r = new Ray(light.position, pos - light.position);
                        var hit = BSPRoot.TraceRay(r, mindist);
                        if (hit.hit) mindist = Vector3.Distance(hit.point, light.position);
                        var nonBSPhit = TriangleOccluder.TraceRay(r, mindist);
                        return mindist < dist && MathF.Abs(dist - mindist) > 0.1f && mindist > 0.1f || nonBSPhit;
                    }
            }
            return false;
        }
    }
}
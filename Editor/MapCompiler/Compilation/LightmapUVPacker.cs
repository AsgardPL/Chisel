using Microsoft.Xna.Framework;
using RectpackSharp;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapCompiler
{
    /// <summary>
    /// Responsible for generating and packing per-face lightmap UVs into a
    /// single atlas using a rectangle-packing algorithm.
    /// </summary>
    public static class LightmapUVPacker
    {
        /// <summary>
        /// Computes lightmap UVs for every drawn face in the brush array,
        /// packs them into a square atlas, and returns the atlas resolution
        /// and the raw texture-space maximum used for normalization.
        /// </summary>
        public static void PackUVs(
            ref Brush[] brushes,
            ref Terrain[] terrains,
            System.Drawing.Bitmap[] textures,
            float res,
            out int lightmapResolution,
            out float totalMax)
        {
            var packBounds = new List<PackingRectangle>();

            // Generate brush rects
            for (int b = 0; b < brushes.Length; b++)
            {
                var lightmapUvs = new List<Vector2>(brushes[b].lightmapUvs);
                var triangles = brushes[b].faces.SelectMany(f => f.indices.Select(i => (short)i)).ToArray();

                for (int f = 0; f < brushes[b].faces.Length; f++)
                {
                    var face = brushes[b].faces[f];
                    if (!face.drawn) continue;
                    foreach (int idx in face.indices)
                    {
                        var uv = brushes[b].lightmapUvs[idx];
                        uv *= new Vector2(MathF.Max(face.luxelScale, 1));
                        lightmapUvs[idx] = uv * res;
                    }
                }
                brushes[b].lightmapUvs = lightmapUvs.ToArray();

                if (brushes[b].isSkybox) continue;

                for (int f = 0; f < brushes[b].faces.Length; f++)
                {
                    var face = brushes[b].faces[f];
                    if (!face.drawn) continue;

                    Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
                    foreach (int idx in face.indices)
                    {
                        min = Vector2.Min(brushes[b].lightmapUvs[idx], min);
                        max = Vector2.Max(brushes[b].lightmapUvs[idx], max);
                    }
                    Vector2 size = Vector2.Max(max - min, Vector2.One);
                    packBounds.Add(new PackingRectangle(
                        0, 0,
                        (uint)MathF.Ceiling(size.X) + 10, (uint)MathF.Ceiling(size.Y) + 10,
                        (ushort)b << 16 | (ushort)f));
                }
            }

            // Generate terrain rects
            for (int t = 0; t < terrains.Length; t++)
            {
                for (int v = 0; v < terrains[t].lightmapUvs.Length; v++)
                    terrains[t].lightmapUvs[v] *= res;

                Vector2 tmin = new Vector2(float.MaxValue), tmax = new Vector2(float.MinValue);
                foreach (var uv in terrains[t].lightmapUvs)
                {
                    tmin = Vector2.Min(uv, tmin);
                    tmax = Vector2.Max(uv, tmax);
                }
                Vector2 tsize = Vector2.Max(tmax - tmin, Vector2.One);
                packBounds.Add(new PackingRectangle(
                    0, 0,
                    (uint)MathF.Ceiling(tsize.X) + 10, (uint)MathF.Ceiling(tsize.Y) + 10,
                    ~t));   // ~t is negative, never collides with positive brush IDs
            }

            // Pack
            var finalRects = packBounds.ToArray();
            RectanglePacker.Pack(finalRects, out _);

            Vector2 totalMaxV = Vector2.Zero;

            for (int b = 0; b < brushes.Length; b++)
            {
                if (brushes[b].isSkybox) continue;

                for (int f = 0; f < brushes[b].faces.Length; f++)
                {
                    var face = brushes[b].faces[f];
                    if (!face.drawn) continue;

                    Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
                    foreach (int idx in face.indices)
                    {
                        min = Vector2.Min(brushes[b].lightmapUvs[idx], min);
                        max = Vector2.Max(brushes[b].lightmapUvs[idx], max);
                    }

                    int id = Array.FindIndex(finalRects, r => r.Id == ((ushort)b << 16 | (ushort)f));
                    if (id == -1) continue;

                    var rect = finalRects[id];
                    var difMin = new Vector2(rect.X, rect.Y);

                    var offlimits = new HashSet<int>();
                    foreach (int idx in face.indices)
                    {
                        if (!offlimits.Add(idx)) continue;
                        brushes[b].lightmapUvs[idx] -= min;
                        brushes[b].lightmapUvs[idx] += difMin;
                    }

                    foreach (int idx in face.indices)
                        totalMaxV = Vector2.Max(brushes[b].lightmapUvs[idx], totalMaxV);
                }
            }

            for (int t = 0; t < terrains.Length; t++)
            {
                int id = Array.FindIndex(finalRects, r => r.Id == ~t);
                if (id == -1) continue;

                Vector2 tmin = new Vector2(float.MaxValue);
                foreach (var uv in terrains[t].lightmapUvs)
                    tmin = Vector2.Min(uv, tmin);

                var rect = finalRects[id];
                var difMin = new Vector2(rect.X, rect.Y);

                for (int v = 0; v < terrains[t].lightmapUvs.Length; v++)
                {
                    terrains[t].lightmapUvs[v] -= tmin;
                    terrains[t].lightmapUvs[v] += difMin;
                    totalMaxV = Vector2.Max(terrains[t].lightmapUvs[v], totalMaxV);
                }
            }

            totalMax = Math.Max(totalMaxV.X, totalMaxV.Y);

            for (int b = 0; b < brushes.Length; b++)
                for (int i = 0; i < brushes[b].lightmapUvs.Length; i++)
                    brushes[b].lightmapUvs[i] /= totalMax;

            for (int t = 0; t < terrains.Length; t++)
            {
                for (int v = 0; v < terrains[t].lightmapUvs.Length; v++)
                {
                    terrains[t].lightmapUvs[v] /= totalMax;
                    terrains[t].vertices[v].LightmapCoordinate = terrains[t].lightmapUvs[v];
                }
            }

            lightmapResolution = (int)Math.Ceiling(totalMax);
        }
    }
}

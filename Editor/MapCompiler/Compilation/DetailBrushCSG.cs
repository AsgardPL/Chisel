using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MapCompiler.Compilation;

public static class DetailBrushCSG
{
    class ConsoleMute : IDisposable
    {
        readonly TextWriter original;
        public ConsoleMute()
        {
            original = Console.Out;
            Console.SetOut(TextWriter.Null);
        }
        public void Dispose() => Console.SetOut(original);
    }
    public static void MergeDetailGroups(Brush[] brushes, EntityReference[] entities)
    {
        if (entities == null) return;

        foreach (var entity in entities)
        {
            if (entity?.entityName != "FuncDetail") continue;
            if (entity.brushIndices == null || entity.brushIndices.Count < 2) continue;

            var group = entity.brushIndices.Where(i => i >= 0 && i < brushes.Length).ToList();
            if (group.Count < 2) continue;

            MergeGroup(brushes, group);
        }
    }
    static void MergeGroup(Brush[] brushes, List<int> group)
    {
        var subset = group.Select(idx => brushes[idx]).ToArray();

        for (int i = 0; i < subset.Length; i++)
        {
            subset[i].isEntity = false;
            SanitizeForReconstruction(ref subset[i]);
        }

        Brush[] reconstructed;
        using (new ConsoleMute())
        {
            reconstructed = ReconstructGroupViaLocalBSP(subset);
        }

        for (int i = 0; i < group.Count; i++)
        {
            int idx = group[i];
            var original = brushes[idx];
            original.faces = reconstructed[i].faces;
            original.vertices = reconstructed[i].vertices;
            original.uvs = reconstructed[i].uvs;
            original.lightmapUvs = reconstructed[i].lightmapUvs;
            original.abnormal = reconstructed[i].abnormal;
            brushes[idx] = original;
        }
    }

    static void SanitizeForReconstruction(ref Brush b)
    {
        int vertCount = b.vertices?.Length ?? 0;

        if (b.uvs == null || b.uvs.Length != vertCount)
            b.uvs = new Vector2[vertCount];

        if (b.lightmapUvs == null || b.lightmapUvs.Length != vertCount)
            b.lightmapUvs = new Vector2[vertCount];
    }
    static Brush[] ReconstructGroupViaLocalBSP(Brush[] subset)
    {
        BSPRoot.Reset();
        Portalizer.Reset();

        var subsetBounds = new BoundingBox[subset.Length];
        Vector3 boundMin = new(float.MaxValue), boundMax = new(float.MinValue);

        for (int i = 0; i < subset.Length; i++)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var f in subset[i].faces)
                foreach (var vi in f.indices)
                {
                    var v = subset[i].vertices[vi] + subset[i].position;
                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                }
            subsetBounds[i] = new BoundingBox(min, max);
            boundMin = Vector3.Min(boundMin, min);
            boundMax = Vector3.Max(boundMax, max);
        }

        boundMin -= Vector3.One * 8f;
        boundMax += Vector3.One * 8f;
        var localBounds = new BoundingBox(boundMin, boundMax);

        var brushIDs = new ushort[subset.Length];
        var splits = new List<(int brush, int face, Plane plane)>();

        for (int i = 0; i < subset.Length; i++)
        {
            brushIDs[i] = (ushort)i;
            for (int f = 0; f < subset[i].faces.Length; f++)
            {
                var face = subset[i].faces[f];
                var plane = new Plane(subset[i].vertices[face.indices[0]] + subset[i].position,
                                     Vector3.Normalize(face.normal));
                Portalizer.FindPlane(ref plane, out _);
                splits.Add((i, f, plane));
            }
        }

        splits.Sort((a, b) =>
        {
            float sA = MapCompileOrchestrator.FaceSize(subset[a.brush].faces[a.face], subset[a.brush].vertices);
            float sB = MapCompileOrchestrator.FaceSize(subset[b.brush].faces[b.face], subset[b.brush].vertices);
            return sB.CompareTo(sA);
        });

        BSPRoot.tempNodes = new List<BSPNode> { new BSPNode() };
        BSPRoot.tempNodes[0].nodeContents = brushIDs;
        foreach (var s in splits)
            BSPRoot.Cut(s.plane, subset, subsetBounds, s.brush, s.face, 0);
        BSPRoot.DoubleCheck(subset);
        BSPRoot.nodes = BSPRoot.tempNodes.ToArray();

        Portalizer.MakeHeadnodePortals(localBounds);
        Portalizer.CutNodePortals();
        Portalizer.MergePortals();
        Portalizer.MarkBrushesOnPortals(subset, subsetBounds);

        var nodesList = BSPRoot.nodes.ToList();

        int outsideIndex = nodesList.Count;

        nodesList.Add(new BSPNode { solid = false, split = false, id = (uint)outsideIndex });

        BSPRoot.nodes = nodesList.ToArray();

        var portalsArr = Portalizer.GetPortals().ToArray();
        foreach (var portal in portalsArr)
        {
            if (portal.LeafFront == -1) portal.LeafFront = outsideIndex;
            if (portal.LeafBack == -1) portal.LeafBack = outsideIndex;
        }

        BrushCSGReconstructor.RebuildBrushesFromPortals(
            ref subset, out _, out _, out _, portalsArr);

        BSPRoot.Reset();
        Portalizer.Reset();

        return subset;
    }
}
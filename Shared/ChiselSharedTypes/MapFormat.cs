using Chisel.Collision;
using Chisel.EXScript;
using Chisel.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

#if !rockwall

namespace Chisel.Formatter
{
    // The format is a dead-simple binary container:
    //
    //   [4 bytes]  magic "CMAP"
    //   [1 byte]   version
    //   [4 bytes]  map lump offset
    //   [4 bytes]  bsp lump offset
    //   [4 bytes]  vis lump offset
    //   <map lump data>
    //   <bsp lump data>
    //   <vis lump data>

    public static class MapFormatter
    {
        const uint Magic = 0x50414D43; // "CMAP" as a little-endian uint
        
        const byte Version = 10;
        public static byte[] WriteMapData(Map map, BSPFile bspFile, VisFile visFile)
        {
            using var ms = new MemoryStream(1 << 20); // start at 1 MB, grows as needed
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(Version);

            // Reserve space for the three lump offsets, we'll seek back and fill them in
            // once we know where each lump actually landed.
            long offsetTablePos = ms.Position;
            w.Write(0); // map offset placeholder
            w.Write(0); // bsp offset placeholder
            w.Write(0); // vis offset placeholder

            int mapOffset = (int)ms.Position; WriteMap(w, map);
            int bspOffset = (int)ms.Position; WriteBSP(w, bspFile);
            int visOffset = (int)ms.Position; WriteVis(w, visFile);

            ms.Seek(offsetTablePos, SeekOrigin.Begin);
            w.Write(mapOffset);
            w.Write(bspOffset);
            w.Write(visOffset);

            return ms.ToArray();
        }

        public static (Map map, BSPFile bspFile, VisFile visFile) ReadMapData(string path)
        {
            // ReadAllBytes up front so we get a single contiguous MemoryStream
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            if (r.ReadUInt32() != Magic)
                throw new Exception("ReadMapData: not a CMAP file");

            byte version = r.ReadByte();
            if (version != Version)
                throw new Exception($"ReadMapData: unsupported version {version}, expected {Version}");

            int mapOffset = r.ReadInt32();
            int bspOffset = r.ReadInt32();
            int visOffset = r.ReadInt32();

            // Jump directly to each lump by its stored offset.
            ms.Seek(mapOffset, SeekOrigin.Begin); var map = ReadMap(r);
            ms.Seek(bspOffset, SeekOrigin.Begin); var bspFile = ReadBSP(r);
            ms.Seek(visOffset, SeekOrigin.Begin); var visFile = ReadVis(r);

            return (map, bspFile, visFile);
        }

        static void WriteMap(BinaryWriter w, Map map)
        {
            w.Write(map.hasVis);

            w.Write(map.brushBounds.Length);
            foreach (var bb in map.brushBounds) WriteBBox(w, bb);

            w.Write(map.brushes.Length);
            foreach (var b in map.brushes) WriteBrush(w, b);

            w.Write(map.terrains.Length);
            foreach (var t in map.terrains) WriteTerrain(w, t);

            w.Write(map.entities.Length);
            foreach (var e in map.entities) WriteEntityRef(w, e);

            // Root node first, then the flat allNodes list the octree traversal uses.
            w.Write(map.octreeNodes.Count);
            WriteOctree(w, map.root);
            foreach (var n in map.octreeNodes) WriteOctree(w, n);

            w.Write(map.lightNodes.Length);
            foreach (var ln in map.lightNodes) WriteLightNodeBundle(w, ln);

            w.Write(map.lightGroupKeys.Length);
            foreach (var key in map.lightGroupKeys) WriteStr(w, key);

            w.Write(map.mapModels.Length);
            foreach (var dm in map.mapModels) WriteDetailModel(w, dm);

            w.Write(map.staticGeomVertices.Length);
            foreach (var v in map.staticGeomVertices) WriteVertexLM(w, v);

            w.Write(map.leafPolygons.Length);
            foreach (var lp in map.leafPolygons) WriteLeafPolygon(w, lp);

            w.Write(map.leafPolyStart.Length);
            for (int i = 0; i < map.leafPolyStart.Length; i++)
            {
                w.Write(map.leafPolyStart[i]);
                w.Write(map.leafPolyCount[i]);
            }

            WriteNodeGraph(w, map.nodegraph);
        }

        static Map ReadMap(BinaryReader r)
        {
            bool hasVis = r.ReadBoolean();

            var brushBounds = new BoundingBox[r.ReadInt32()];
            for (int i = 0; i < brushBounds.Length; i++) brushBounds[i] = ReadBBox(r);

            var brushes = new Brush[r.ReadInt32()];
            for (int i = 0; i < brushes.Length; i++) brushes[i] = ReadBrush(r);

            var terrains = new Terrain[r.ReadInt32()];
            for (int i = 0; i < terrains.Length; i++) terrains[i] = ReadTerrain(r);

            var entities = new EntityReference[r.ReadInt32()];
            for (int i = 0; i < entities.Length; i++) entities[i] = ReadEntityRef(r);

            int octreeNodeCount = r.ReadInt32();
            OctreeRoot.allNodes.Clear();
            var root = ReadOctree(r);
            OctreeRoot.allNodes.Add(root);
            var octreeNodes = new List<Octree>(octreeNodeCount);
            for (int i = 0; i < octreeNodeCount; i++)
            {
                var n = ReadOctree(r);
                OctreeRoot.allNodes.Add(n);
                octreeNodes.Add(n);
            }

            var lightNodes = new LightNodeBundle[r.ReadInt32()];
            for (int i = 0; i < lightNodes.Length; i++) lightNodes[i] = ReadLightNodeBundle(r);

            var lightGroupKeys = new string[r.ReadInt32()];
            for (int i = 0; i < lightGroupKeys.Length; i++) lightGroupKeys[i] = ReadStr(r);

            var models = new MapPropModel[r.ReadInt32()];
            for (int i = 0; i < models.Length; i++) models[i] = ReadDetailModel(r);

            var staticGeomVertices = new VertexLightmapped[r.ReadInt32()];
            for (int i = 0; i < staticGeomVertices.Length; i++) staticGeomVertices[i] = ReadVertexLM(r);

            var leafPolygons = new LeafPolygon[r.ReadInt32()];
            for (int i = 0; i < leafPolygons.Length; i++) leafPolygons[i] = ReadLeafPolygon(r);

            int leafCount = r.ReadInt32();
            var leafPolyStart = new int[leafCount];
            var leafPolyCount = new int[leafCount];
            for (int i = 0; i < leafCount; i++)
            {
                leafPolyStart[i] = r.ReadInt32();
                leafPolyCount[i] = r.ReadInt32();
            }

            var nodegraph = ReadNodeGraph(r);

            return new Map
            {
                hasVis = hasVis,
                brushBounds = brushBounds,
                brushes = brushes,
                terrains = terrains,
                entities = entities,
                mapModels = models,
                staticGeomVertices = staticGeomVertices,
                leafPolygons = leafPolygons,
                leafPolyStart = leafPolyStart,
                leafPolyCount = leafPolyCount,
                root = root,
                octreeNodes = octreeNodes,
                lightNodes = lightNodes,
                lightGroupKeys = lightGroupKeys,
                nodegraph = nodegraph,
            };
        }

        static void WriteDetailModel(BinaryWriter w, MapPropModel mdl)
        {
            w.Write(mdl.VisLeaf);
            w.Write(mdl.Material);

            w.Write(mdl.Vertices.Length);
            foreach(var vert in mdl.Vertices)
            {
                WritePropVertex(w,vert);
            }
            w.Write(mdl.Indices.Length);
            foreach (var indice in mdl.Indices)
            {
                w.Write(indice);
            }
        }
        static void WriteLeafPolygon(BinaryWriter w, LeafPolygon lp)
        {
            w.Write(lp.VertexStart);
            w.Write(lp.VertexCount);
            WriteStr(w, lp.MaterialName);
            WriteVec3(w, lp.Normal);
            WriteVec3(w, lp.Tangent);
            WriteVec3(w, lp.Binormal);
            WriteVec3(w, lp.B1);
            WriteVec3(w, lp.B2);
            WriteVec3(w, lp.B3);
        }

        static LeafPolygon ReadLeafPolygon(BinaryReader r) => new()
        {
            VertexStart = r.ReadInt32(),
            VertexCount = r.ReadInt32(),
            MaterialName = ReadStr(r),
            Normal = ReadVec3(r),
            Tangent = ReadVec3(r),
            Binormal = ReadVec3(r),
            B1 = ReadVec3(r),
            B2 = ReadVec3(r),
            B3 = ReadVec3(r),
        };
        static MapPropModel ReadDetailModel(BinaryReader r)
        {
            MapPropModel model = default;

            model.VisLeaf = r.ReadUInt32();
            model.Material = r.ReadString();
            model.Vertices = new MapPropModelVertex[r.ReadInt32()];

            for(int i = 0; i < model.Vertices.Length; i++)
            {
                model.Vertices[i] = ReadPropVertex(r);
            }

            model.Indices = new int[r.ReadInt32()];
            for (int i = 0; i < model.Indices.Length; i++)
            {
                model.Indices[i] = r.ReadInt32();
            }

            return model;
        }

        static void WriteBSP(BinaryWriter w, BSPFile file)
        {
            w.Write(file.nodes.Length);
            foreach (var n in file.nodes)
            {
                w.Write(n.nodeFlag);
                w.Write(n.spx); w.Write(n.spy); w.Write(n.spz); w.Write(n.d);
                w.Write(n.front); w.Write(n.back); w.Write(n.parent); w.Write(n.id);
                w.Write(n.solid); w.Write(n.split);
                w.Write(n.brush); w.Write(n.face);
                w.Write(n.bnx); w.Write(n.bny); w.Write(n.bnz);
            }
        }

        static BSPFile ReadBSP(BinaryReader r)
        {
            var nodes = new BSPNode[r.ReadInt32()];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = new BSPNode();
                n.nodeFlag = r.ReadByte();
                n.spx = r.ReadSingle(); n.spy = r.ReadSingle(); n.spz = r.ReadSingle(); n.d = r.ReadSingle();
                n.front = r.ReadUInt32(); n.back = r.ReadUInt32(); n.parent = r.ReadUInt32(); n.id = r.ReadUInt32();
                n.solid = r.ReadBoolean(); n.split = r.ReadBoolean();
                n.brush = r.ReadUInt16(); n.face = r.ReadByte();
                n.bnx = r.ReadSingle(); n.bny = r.ReadSingle(); n.bnz = r.ReadSingle();
                nodes[i] = n;
            }
            return new BSPFile { nodes = nodes };
        }

        static void WriteVis(BinaryWriter w, VisFile file)
        {
            w.Write(file.leaves.Length);
            foreach (var leaf in file.leaves)
            {
                w.Write(leaf.IsEmpty); w.Write(leaf.HasSkybox); w.Write(leaf.bspLeafID);
                w.Write(leaf.portals.Length);
                foreach (var p in leaf.portals) w.Write(p);
                w.Write(leaf.pvs.Length);
                foreach (var p in leaf.pvs) w.Write(p);
                w.Write(leaf.brushes.Length);
                foreach (var b in leaf.brushes) w.Write(b);
            }

            w.Write(file.portals.Length);
            foreach (var portal in file.portals)
            {
                w.Write(portal.LeafFront); w.Write(portal.LeafBack);
                WriteVec3(w, portal.Plane.Normal); w.Write(portal.Plane.D);
                w.Write(portal.Vertices.Length);
                foreach (var v in portal.Vertices) WriteVec3(w, v);
                w.Write(portal.Brushes.Length);
                foreach (var b in portal.Brushes) w.Write(b);
            }
        }

        static VisFile ReadVis(BinaryReader r)
        {
            var leaves = new VisLeaf[r.ReadInt32()];
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = new VisLeaf();
                leaf.IsEmpty = r.ReadBoolean(); leaf.HasSkybox = r.ReadBoolean(); leaf.bspLeafID = r.ReadInt32();
                leaf.portals = new int[r.ReadInt32()];
                for (int j = 0; j < leaf.portals.Length; j++) leaf.portals[j] = r.ReadInt32();
                leaf.pvs = new uint[r.ReadInt32()];
                for (int j = 0; j < leaf.pvs.Length; j++) leaf.pvs[j] = r.ReadUInt32();
                leaf.brushes = new ushort[r.ReadInt32()];
                for (int j = 0; j < leaf.brushes.Length; j++) leaf.brushes[j] = r.ReadUInt16();
                leaves[i] = leaf;
            }

            var portals = new Portal[r.ReadInt32()];
            for (int i = 0; i < portals.Length; i++)
            {
                var portal = new Portal();
                portal.LeafFront = r.ReadInt32(); portal.LeafBack = r.ReadInt32();
                portal.Plane = new Plane(ReadVec3(r), r.ReadSingle());
                portal.Vertices = new Vector3[r.ReadInt32()];
                for (int j = 0; j < portal.Vertices.Length; j++) portal.Vertices[j] = ReadVec3(r);
                portal.Brushes = new ushort[r.ReadInt32()];
                for (int j = 0; j < portal.Brushes.Length; j++) portal.Brushes[j] = r.ReadUInt16();
                portals[i] = portal;
            }

            return new VisFile { leaves = leaves, portals = portals };
        }

        static void WriteBrush(BinaryWriter w, Brush b)
        {
            WriteVec3(w, b.position);

            // Pack the seven bool flags into one byte rather than seven.
            byte flags = 0;
            if (b.abnormal) flags |= 1 << 0;
            if (b.isDetail) flags |= 1 << 1;
            if (b.isClip) flags |= 1 << 2;
            if (b.isTrigger) flags |= 1 << 3;
            if (b.isSkybox) flags |= 1 << 4;
            if (b.isLightNodeVolume) flags |= 1 << 5;
            if (b.isEntity) flags |= 1 << 6;
            w.Write(flags);

            w.Write(b.vertices.Length);
            foreach (var v in b.vertices) WriteVec3(w, v);

            w.Write(b.uvs.Length);
            foreach (var uv in b.uvs) WriteVec2(w, uv);

            w.Write(b.lightmapUvs.Length);
            foreach (var uv in b.lightmapUvs) WriteVec2(w, uv);

            w.Write(b.faces.Length);
            foreach (var f in b.faces) WriteFace(w, f);
        }

        static Brush ReadBrush(BinaryReader r)
        {
            var b = new Brush();
            b.position = ReadVec3(r);

            byte flags = r.ReadByte();
            b.abnormal = (flags & (1 << 0)) != 0;
            b.isDetail = (flags & (1 << 1)) != 0;
            b.isClip = (flags & (1 << 2)) != 0;
            b.isTrigger = (flags & (1 << 3)) != 0;
            b.isSkybox = (flags & (1 << 4)) != 0;
            b.isLightNodeVolume = (flags & (1 << 5)) != 0;
            b.isEntity = (flags & (1 << 6)) != 0;

            b.vertices = new Vector3[r.ReadInt32()];
            for (int i = 0; i < b.vertices.Length; i++) b.vertices[i] = ReadVec3(r);

            b.uvs = new Vector2[r.ReadInt32()];
            for (int i = 0; i < b.uvs.Length; i++) b.uvs[i] = ReadVec2(r);

            b.lightmapUvs = new Vector2[r.ReadInt32()];
            for (int i = 0; i < b.lightmapUvs.Length; i++) b.lightmapUvs[i] = ReadVec2(r);

            b.faces = new Face[r.ReadInt32()];
            for (int i = 0; i < b.faces.Length; i++) b.faces[i] = ReadFace(r);

            return b;
        }

        static void WriteFace(BinaryWriter w, Face f)
        {
            WriteVec3(w, f.normal); WriteVec3(w, f.tangent); WriteVec3(w, f.binormal);
            WriteVec3(w, f.basis1); WriteVec3(w, f.basis2); WriteVec3(w, f.basis3);
            w.Write(f.drawn); w.Write(f.surface);
            w.Write(f.tOffX); w.Write(f.tOffY); w.Write(f.tScaleX); w.Write(f.tScaleY);
            w.Write(f.luxelScale);
            WriteStr(w, f.materialName);

            w.Write(f.indices.Length);
            foreach (var idx in f.indices) w.Write(idx);

            int decalCount = f.decals?.Length ?? 0;
            w.Write(decalCount);
            for (int i = 0; i < decalCount; i++)
            {
                w.Write(f.decals[i].surface);
                w.Write(f.decals[i].vertices.Length);
                foreach (var v in f.decals[i].vertices) WriteVertexLM(w, v);
            }
        }

        static Face ReadFace(BinaryReader r)
        {
            var f = new Face();
            f.normal = ReadVec3(r); f.tangent = ReadVec3(r); f.binormal = ReadVec3(r);
            f.basis1 = ReadVec3(r); f.basis2 = ReadVec3(r); f.basis3 = ReadVec3(r);
            f.drawn = r.ReadBoolean(); f.surface = r.ReadInt32();
            f.tOffX = r.ReadSingle(); f.tOffY = r.ReadSingle();
            f.tScaleX = r.ReadSingle(); f.tScaleY = r.ReadSingle();
            f.luxelScale = r.ReadSingle();
            f.materialName = ReadStr(r);

            f.indices = new int[r.ReadInt32()];
            for (int i = 0; i < f.indices.Length; i++) f.indices[i] = r.ReadInt32();

            f.decals = new EnvironmentalDecal[r.ReadInt32()];
            for (int i = 0; i < f.decals.Length; i++)
            {
                f.decals[i].surface = r.ReadInt32();
                f.decals[i].vertices = new VertexLightmapped[r.ReadInt32()];
                for (int j = 0; j < f.decals[i].vertices.Length; j++) f.decals[i].vertices[j] = ReadVertexLM(r);
            }

            return f;
        }

        static void WriteTerrain(BinaryWriter w, Terrain t)
        {
            w.Write(t.surface); w.Write(t.blendedSurface);
            w.Write(t.surfaceName); w.Write(t.blendedSurfaceName);
            w.Write(t.brushSource); w.Write(t.faceSource);
            WriteBBox(w, t.bounds);
            w.Write(t.vertices.Length);
            foreach (var v in t.vertices) WriteTerrainVert(w, v);
            w.Write(t.triangles.Length);
            foreach (var tri in t.triangles) w.Write(tri);
        }

        static Terrain ReadTerrain(BinaryReader r)
        {
            var t = new Terrain();
            t.surface = r.ReadInt32(); t.blendedSurface = r.ReadInt32();
            t.surfaceName = r.ReadString(); t.blendedSurfaceName = r.ReadString();
            t.brushSource = r.ReadInt32(); t.faceSource = r.ReadInt32();
            t.bounds = ReadBBox(r);
            t.vertices = new TerrainVertex[r.ReadInt32()];
            for (int i = 0; i < t.vertices.Length; i++) t.vertices[i] = ReadTerrainVert(r);
            t.triangles = new short[r.ReadInt32()];
            for (int i = 0; i < t.triangles.Length; i++) t.triangles[i] = r.ReadInt16();
            return t;
        }

        static void WriteEntityRef(BinaryWriter w, EntityReference e)
        {
            WriteVec3(w, e.position); WriteVec3(w, e.spawnRotation); WriteVec3(w, e.scale);
            WriteStr(w, e.entityName ?? "");
            WriteStr(w, e.name ?? "");
            WriteStr(w, e.entityMoveParentName ?? "");

            int propCount = e.properties?.Length ?? 0;
            w.Write(propCount);
            for (int i = 0; i < propCount; i++)
            {
                WriteStr(w, e.properties[i].Name);
                WriteStr(w, e.properties[i].Value);
            }

            int outCount = e.entityOutputs?.Count ?? 0;
            w.Write(outCount);
            for (int i = 0; i < outCount; i++)
            {
                var (evtName, output) = e.entityOutputs[i];
                WriteStr(w, evtName);
                w.Write(output.delay); w.Write(output.refire);
                WriteStr(w, output.entityTarget);
                WriteStr(w, output.entityInputTarget);
                WriteStr(w, output.inputParameters);
                WriteStr(w, output.scriptSource ?? "NULLSCRIPT");
            }

            int brushCount = e.brushIndices?.Count ?? 0;
            w.Write(brushCount);
            for (int i = 0; i < brushCount; i++)
                w.Write(e.brushIndices[i]);
        }

        static EntityReference ReadEntityRef(BinaryReader r)
        {
            var e = new EntityReference();
            e.position = ReadVec3(r); e.spawnRotation = ReadVec3(r); e.scale = ReadVec3(r);
            e.entityName = ReadStr(r);
            e.name = ReadStr(r);
            e.entityMoveParentName = ReadStr(r);

            int propCount = r.ReadInt32();
            if (propCount > 0)
            {
                e.properties = new EntityProperty[propCount];
                for (int i = 0; i < propCount; i++)
                {
                    var p = new EntityProperty();
                    p.Name = ReadStr(r); p.Value = ReadStr(r);
                    e.properties[i] = p;
                }
            }

            int outCount = r.ReadInt32();
            if (outCount > 0)
            {
                e.entityOutputs = new List<(string, EntityOutput)>(outCount);
                for (int i = 0; i < outCount; i++)
                {
                    string evtName = ReadStr(r);
                    var output = new EntityOutput();
                    output.delay = r.ReadSingle(); output.refire = r.ReadInt32();
                    output.entityTarget = ReadStr(r);
                    output.entityInputTarget = ReadStr(r);
                    output.inputParameters = ReadStr(r);
                    var script = ReadStr(r);
                    output.scriptSource = script == "NULLSCRIPT" ? null : script;
                    e.entityOutputs.Add((evtName, output));
                }
            }

            int brushCount = r.ReadInt32();
            if (brushCount > 0)
            {
                e.brushIndices = new List<int>(brushCount);
                for (int i = 0; i < brushCount; i++)
                    e.brushIndices.Add(r.ReadInt32());
            }

            return e;
        }

        static void WriteOctree(BinaryWriter w, Octree oct)
        {
            WriteBBox(w, oct.box);
            w.Write(oct.isEnd);
            // children is always exactly 8 elements so no length prefix needed
            foreach (var c in oct.children) w.Write(c);
            w.Write(oct.corners.Length);
            foreach (var c in oct.corners) WriteBBox(w, c);
            w.Write(oct.contents.Count);
            foreach (var c in oct.contents) w.Write(c);
        }

        static Octree ReadOctree(BinaryReader r)
        {
            var oct = new Octree();
            oct.box = ReadBBox(r);
            oct.isEnd = r.ReadBoolean();
            oct.children = new int[8];
            for (int i = 0; i < 8; i++) oct.children[i] = r.ReadInt32();
            oct.corners = new BoundingBox[r.ReadInt32()];
            for (int i = 0; i < oct.corners.Length; i++) oct.corners[i] = ReadBBox(r);
            oct.contents = new List<int>();
            var contentLength = r.ReadInt32();
            for (int i = 0; i < contentLength; i++) oct.contents.Add(r.ReadInt32());
            return oct;
        }

        static void WriteLightNodeBundle(BinaryWriter w, LightNodeBundle bundle)
        {
            WriteBBox(w, bundle.box);
            w.Write(bundle.children.Length);
            foreach (var child in bundle.children)
            {
                WriteVec3(w, child.pos);
                w.Write(child.data.Length);
                foreach (var d in child.data) { w.Write(d.lightBlocked); w.Write(d.lightNum); }
                w.Write(child.indirectCoefficients.Length);
                foreach (var c in child.indirectCoefficients) WriteVec3(w, c);

                w.Write(child.groupIndirectCoefficients.Length);
                foreach (var groupCoeffs in child.groupIndirectCoefficients)
                {
                    w.Write(groupCoeffs.Length);
                    foreach (var c in groupCoeffs) WriteVec3(w, c);
                }
            }
        }

        static LightNodeBundle ReadLightNodeBundle(BinaryReader r)
        {
            var bundle = new LightNodeBundle(ReadBBox(r));
            bundle.children = new LightNodeBundle.LightNode[r.ReadInt32()];
            for (int i = 0; i < bundle.children.Length; i++)
            {
                var child = new LightNodeBundle.LightNode();
                child.pos = ReadVec3(r);
                child.data = new LightNodeBundle.LightData[r.ReadInt32()];
                for (int j = 0; j < child.data.Length; j++)
                    child.data[j] = new LightNodeBundle.LightData { lightBlocked = r.ReadBoolean(), lightNum = r.ReadInt32() };
                child.indirectCoefficients = new Vector3[r.ReadInt32()];
                for (int j = 0; j < child.indirectCoefficients.Length; j++)
                    child.indirectCoefficients[j] = ReadVec3(r);

                child.groupIndirectCoefficients = new Vector3[r.ReadInt32()][];
                for (int g = 0; g < child.groupIndirectCoefficients.Length; g++)
                {
                    var coeffs = new Vector3[r.ReadInt32()];
                    for (int j = 0; j < coeffs.Length; j++) coeffs[j] = ReadVec3(r);
                    child.groupIndirectCoefficients[g] = coeffs;
                }

                bundle.children[i] = child;
            }
            return bundle;
        }

        static void WriteNodeGraph(BinaryWriter w, NodeGraph graph)
        {
            w.Write(graph.nodes.Length);
            foreach (var n in graph.nodes)
            {
                WriteVec3(w, n.position);
                int connCount = n.connections?.Length ?? 0;
                w.Write(connCount);
                for (int i = 0; i < connCount; i++) w.Write(n.connections[i]);
            }
        }

        static NodeGraph ReadNodeGraph(BinaryReader r)
        {
            var nodes = new AINode[r.ReadInt32()];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].position = ReadVec3(r);
                nodes[i].connections = new int[r.ReadInt32()];
                for (int j = 0; j < nodes[i].connections.Length; j++)
                    nodes[i].connections[j] = r.ReadInt32();
            }
            return new NodeGraph { nodes = nodes };
        }

        static void WriteVec3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        static void WriteVec2(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
        static void WriteBBox(BinaryWriter w, BoundingBox b) { WriteVec3(w, b.Min); WriteVec3(w, b.Max); }
        static void WriteShort4(BinaryWriter w, NormalizedShort4 v) { w.Write(v.PackedValue); }

        static Vector3 ReadVec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Vector2 ReadVec2(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());
        static BoundingBox ReadBBox(BinaryReader r) => new(ReadVec3(r), ReadVec3(r));
        static NormalizedShort4 ReadShort4(BinaryReader r) => new() { PackedValue = r.ReadUInt64()};

        static void WriteStr(BinaryWriter w, string s)
        {
            if (string.IsNullOrEmpty(s)) { w.Write(0); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
        static string ReadStr(BinaryReader r)
        {
            int len = r.ReadInt32();
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(r.ReadBytes(len));
        }
        static void WriteVertexLM(BinaryWriter w, VertexLightmapped v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec3(w, v.Tangent); WriteVec3(w, v.Binormal);
            WriteVec2(w, v.TextureCoordinate); WriteVec2(w, v.LightmapCoordinate);
        }
        static VertexLightmapped ReadVertexLM(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            Tangent = ReadVec3(r),
            Binormal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            LightmapCoordinate = ReadVec2(r),
        };

        static void WriteVertexCTN(BinaryWriter w, VertexPositionColorNormalTexture v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec2(w, v.TextureCoordinate); WriteVec3(w, v.Color.ToVector3());
        }
        static VertexPositionColorNormalTexture ReadVertexCTN(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            Color = new Color(ReadVec3(r)),
        };

        static void WritePropVertex(BinaryWriter w, MapPropModelVertex v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec2(w, v.TextureCoordinate); WriteVec3(w, v.Color);
        }
        static MapPropModelVertex ReadPropVertex(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            Color = ReadVec3(r),
        };

        static void WriteTerrainVert(BinaryWriter w, TerrainVertex v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal); WriteVec3(w, v.TextureCoordinate); WriteVec2(w, v.LightmapCoordinate); WriteShort4(w, v.Tangent);
        }
        static TerrainVertex ReadTerrainVert(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec3(r),
            LightmapCoordinate = ReadVec2(r),
            Tangent = ReadShort4(r)
        };
    }
}
#endif
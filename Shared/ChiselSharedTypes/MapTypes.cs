using Chisel.Collision;
using Chisel.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections;
using System.Collections.Generic;
#if !rockwall
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
#endif
#if !compiler && !rockwall
using Engine;
#endif
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Chisel.EXScript;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;

namespace Rockwall
{
    public struct Light
    {
        public enum LightType
        {
            Directional,
            Point,
            SpotLight
        }
        public Color color;
        public float intensity, range, angle, innerAngle;
        public Vector3 position, rotation;
        public LightType type;
        public int id;

        public string targetname;

#if compiler
        [JsonIgnore] public Tuple<int, int>[] affectedBrushes;
#endif
    }
    public struct StyleKeyframe
    {
        public float time;
        public float intensity;
        public Color tint;
        public bool blend;

        public StyleKeyframe(float time, float intensity, bool blend = true)
        {
            this.time = time;
            this.intensity = intensity;
            this.tint = Color.White;
            this.blend = blend;
        }
        public StyleKeyframe(float time, float intensity, Color tint, bool blend = true)
        {
            this.time = time;
            this.intensity = intensity;
            this.tint = tint;
            this.blend = blend;
        }
    }
    public struct LightStyle
    {
        public string name;
        public StyleKeyframe[] keyframes;  // sorted ascending by time
        public bool loop;
    }
    public struct StyleSample
    {
        public float intensity;
        public Color tint;
    }
    public struct LightGroupBounds
    {
        public string name;
        public Vector2 uvMin, uvMax;
    }
    public static class LightPageResolver
    {
        /// <summary>
        /// Evaluates a LightStyle at time t. Non-looping styles hold flat at the
        /// first/last keyframe's value past either end instead of extrapolating.
        /// </summary>
        public static StyleSample Evaluate(LightStyle style, float t)
        {
            var kf = style.keyframes;
            if (kf == null || kf.Length == 0) return new StyleSample { intensity = 1f, tint = Color.White };
            if (kf.Length == 1) return new StyleSample { intensity = kf[0].intensity, tint = kf[0].tint };

            float duration = kf[kf.Length - 1].time;
            float lt = t;

            if (style.loop && duration > 0f)
            {
                lt %= duration;
                if (lt < 0f) lt += duration;
            }
            else
            {
                if (lt <= kf[0].time) return new StyleSample { intensity = kf[0].intensity, tint = kf[0].tint };
                if (lt >= duration) return new StyleSample { intensity = kf[kf.Length - 1].intensity, tint = kf[kf.Length - 1].tint };
            }

            int i = FindSegment(kf, lt);
            var a = kf[i];
            var b = kf[i + 1];

            if (!a.blend)
                return new StyleSample { intensity = a.intensity, tint = a.tint };

            float span = b.time - a.time;
            float frac = span > 0f ? Math.Clamp((lt - a.time) / span, 0f, 1f) : 0f;

            return new StyleSample
            {
                intensity = a.intensity + (b.intensity - a.intensity) * frac,
                tint = Color.Lerp(a.tint, b.tint, frac)
            };
        }

        /// <summary>
        /// The keyframe index t currently falls on, with no interpolation.
        /// </summary>
        public static int GetKeyframeIndex(LightStyle style, float t)
        {
            var kf = style.keyframes;
            if (kf == null || kf.Length == 0) return -1;
            if (kf.Length == 1) return 0;

            float duration = kf[kf.Length - 1].time;
            float lt = t;

            if (style.loop && duration > 0f)
            {
                lt %= duration;
                if (lt < 0f) lt += duration;
            }
            else
            {
                if (lt <= kf[0].time) return 0;
                if (lt >= duration) return kf.Length - 1;
            }

            return FindSegment(kf, lt);
        }

        // Keyframes are sorted ascending by time
        private static int FindSegment(StyleKeyframe[] kf, float lt)
        {
            int i = 0;
            while (i < kf.Length - 2 && lt >= kf[i + 1].time) i++;
            return i;
        }
    }

    [System.Serializable]
    public class EditorGroup
    {
        //public string GroupName;
        public Guid[] GroupMembers;
    }
    public struct RawMap
    {
        public Brush[] brushes;
        public Hint[] hints;
        public EntityReference[] entityReferences;
        public Terrain[] terrains;
        public EditorGroup[] groups;
        /// <summary>
        /// Map-format version. 0 (the default for any JSON saved before this field existed) means
        /// "may still have brush-owned-entity data embedded per-brush".
        /// </summary>
        public int formatVersion;
        public static RawMap CompileRawMap(Hint[] hints, Brush[] brushes, EntityReference[] entityReferences, Terrain[] terrains)
        {
            return new RawMap() { hints = hints, brushes = brushes, entityReferences = entityReferences, terrains = terrains, formatVersion = 1 };
        }
    }
    public struct MatGroup
    {
        public int MaterialID;
        public IndexBuffer IndexBuffer;
    }
    public struct Brush
    {
        public Face[] faces;
        public Vector3[] vertices;
        public Vector2[] uvs, lightmapUvs;
        public Vector3 position;
        public float width, height, length;
        public bool abnormal, isDetail, isClip, isTrigger, isSkybox, isLightNodeVolume, isEntity;
#if !rockwall && !compiler
        [JsonIgnore] public BrushEntity entity;
        [JsonIgnore] public VertexBuffer brushVertexBuffer;
        [JsonIgnore] public MatGroup[] matGroups;
        [JsonIgnore] public bool renderPiecewise;
#endif
#if rockwall || compiler
        public bool isUsedForTerrain;
        public Brush Clone()
        {
            var b = new Brush
            {
                position = this.position,
                width = this.width,
                height = this.height,
                length = this.length,
                abnormal = this.abnormal,
                isUsedForTerrain = this.isUsedForTerrain
            };
            b.faces = new Face[this.faces.Length];
            b.vertices = new Vector3[this.vertices.Length];
            b.uvs = new Vector2[this.uvs.Length];

            Array.Copy(faces, b.faces, faces.Length);
            Array.Copy(vertices, b.vertices, vertices.Length);
            Array.Copy(uvs, b.uvs, uvs.Length);

            // Note: entity ownership is intentionally NOT copied here.

            return b;
        }
#endif
#if rockwall
        public Guid? GroupingID;
#endif
    }
    public struct Material
    {
        public string name;
        public string surfaceType;
        public string shaderName;
        public float reflectivity;
        public bool transparent;
        public bool alphaClip;
        public bool noCull;
        public int texelsPerUnit;
        public Dictionary<string, bool> shaderFlags;

        // Catches every JSON key that doesn't match a declared field above.
        [JsonExtensionData] private IDictionary<string, JToken> extensionData;

        [JsonIgnore] public Dictionary<string, string> texturePaths;
        [JsonIgnore] public Dictionary<string, Texture2D> textures;

        // Legacy
        [JsonIgnore] public string texture => GetPath("texture");
        [JsonIgnore] public string specular => GetPath("specular");
        [JsonIgnore] public string normal => GetPath("normal");

        // Legacy
        [JsonIgnore] public Texture2D Texture { get => GetTexture("texture"); set => SetTexture("texture", value); }
        [JsonIgnore] public Texture2D Specular { get => GetTexture("specular"); set => SetTexture("specular", value); }
        [JsonIgnore] public Texture2D Normal { get => GetTexture("normal"); set => SetTexture("normal", value); }

        [JsonIgnore] public object Shader;
        [JsonIgnore] public float TexelsPerUnit => texelsPerUnit > 0 ? texelsPerUnit : 512f;

        private static readonly HashSet<string> LegacyMapNames = new() { "texture", "specular", "normal" };

        public IEnumerable<KeyValuePair<string, string>> GetExtraTexturePaths() =>
            texturePaths?.Where(kv => !LegacyMapNames.Contains(kv.Key))
                ?? Enumerable.Empty<KeyValuePair<string, string>>();

        public IEnumerable<KeyValuePair<string, Texture2D>> GetExtraTextures() =>
            textures?.Where(kv => !LegacyMapNames.Contains(kv.Key))
                ?? Enumerable.Empty<KeyValuePair<string, Texture2D>>();

        public Texture2D GetExtraTexture(string key) =>
            !LegacyMapNames.Contains(key) ? GetTexture(key) : null;

        public string GetPath(string key) =>
            texturePaths != null && texturePaths.TryGetValue(key, out var v) ? v : null;

        public Texture2D GetTexture(string key) =>
            textures != null && textures.TryGetValue(key, out var t) ? t : null;

        public void SetTexture(string key, Texture2D value)
        {
            textures ??= new();
            textures[key] = value;
        }

        [OnDeserialized]
        void OnDeserialized(StreamingContext ctx)
        {
            if (extensionData == null) return;
            texturePaths ??= new();
            foreach (var kv in extensionData)
            {
                if (kv.Value.Type == JTokenType.String)
                    texturePaths[kv.Key] = kv.Value.Value<string>();
            }
            extensionData = null;
        }
    }
    public enum UVProjectionMode
    {
        /// <summary>
        /// Classic axial / "standard" projection.
        /// The U and V axes are the two world axes most perpendicular to the face
        /// normal.  Fast and consistent for axis-aligned geometry; can stretch on
        /// angled faces.
        /// </summary>
        World,

        /// <summary>
        /// Planar / "face" projection (Valve 220 style).
        /// Start from the same axial axes, then project them onto the face's plane
        /// so they always lie parallel to it.  No stretching on angled faces.
        /// Rotation is applied inside the face plane.
        /// </summary>
        Face,
    }

    public struct Face
    {
        public int[] indices;
        public Vector3 normal;
        public Vector3 tangent;
        public Vector3 binormal;
        public Vector3 basis1, basis2, basis3;
        public bool drawn;
        public string materialName;
        public int surface;
        public float tOffX, tOffY, tScaleX, tScaleY, luxelScale;
        public float uvRotation;
        public UVProjectionMode uvProjectionMode;
        public EnvironmentalDecal[] decals;

        public Plane? plane;

#if rockwall || compiler
        [JsonIgnore] public List<VertexLightmapped> editorVerts;
        [JsonIgnore] public VertexBuffer vertexBuffer;
        public bool toolFace;
        public int smoothGroup;
#else
        [JsonIgnore] public IndexBuffer faceIndices;
#endif
    }
    public struct EnvironmentalDecal
    {
        public VertexLightmapped[] vertices;
        public int surface;
    }
    public struct Terrain
    {
        public TerrainVertex[] vertices;
        public string surfaceName;
        public string blendedSurfaceName;
#if rockwall
        public TerrainVertex[] editor_cheat_flipalphavert;
#endif
#if compiler
        public float cmp_avgvertdist;
        public Vector2[] lightmapUvs;
#endif
        public short[] triangles;
        public int surface;
        public int blendedSurface;
        public int brushSource;
        public int faceSource;
        public Vector3 sourceNormal;
        public BoundingBox bounds;
#if rockwall
        public Guid? GroupingID;
        public Guid? BrushOwnerGUID;
#endif
#if !rockwall && !compiler
        public ulong[] leafBits;
#endif
    }
    public class Hint
    {
        public Vector3 position;
        public string header, body;
#if rockwall
        public Guid? GroupingID;
#endif
    }

    //Compiled map datatypes...
    public struct MapPropModel
    {
        public uint VisLeaf;
        public string Material;
        public int MaterialID;
        public MapPropModelVertex[] Vertices;
        public int[] Indices;
    }
    public class EntityReference
    {
        public Vector3 position;
        public Vector3 spawnRotation;
        public Quaternion rotation;
        public Vector3 scale;
        public string name;
        public string entityName;

        public EntityProperty[] properties;
        public List<(string, EntityOutput)> entityOutputs;

        /// <summary>
        /// Indices into the map's Brush[] array that this entity owns and controls. Null or empty
        /// for point entities. When non-empty, this is a "brush entity": one entity controlling one
        /// or more brushes at once (a func_door made of two brushes is one entity owning two
        /// indices, not two separate entities).
        /// </summary>
        public List<int> brushIndices;
        public string entityMoveParentName;

        /// <summary>True if this is a brush entity (owns at least one brush).</summary>
        [JsonIgnore] public bool IsBrushEntity => brushIndices != null && brushIndices.Count > 0;
#if rockwall
        public Guid? GroupingID;
        public List<Guid> brushOwnerGUIDs;
#endif
    }
    [System.Serializable]
    public struct EntityProperty
    {
        private string name, value;
        public string Name { get { return name; } set { name = value; } }
        public string Value { get { return value; } set { this.value = value; } }
    }
    public struct EntityPropertyDescriptor
    {
        public string name;
        public string hint;
        public EntityPropertyType type;

        // Enum options, EntityTarget classname filter, inspector grouping, compiled default, and Float clamp range.
        // All optional.
        public string[] options;
        public string targetFilter;
        public string category;
        public string defaultValue;
        public float min, max;

        public override string ToString()
        {
            return name;
        }
    }
    public enum EntityPropertyType
    {
        Color,
        Position,
        Direction,
        Float,
        Texture,
        Model,
        Material,
        Sound,
        String,
        Enum,
        EntityTarget,
        Bool
    }
    // "Next"/"previous" property names used to auto-wire duplicated entities together.
    public struct EntityLinkDescriptor
    {
        public string next;
        public string previous;
    }
    // Which EntityVisualizer to draw for this class, and which entity property feeds each of its fields.
    public struct EntityVisualizerBinding
    {
        public string visualizerType;
        public Dictionary<string, string> fieldToProperty;
        public Dictionary<string, string> fieldToLiteral;
    }
    // Everything the compiler knows about one entity class.
    public class EntityClassMetadata
    {
        public List<EntityPropertyDescriptor> properties = new();
        public List<string> inputs = new();
        public List<string> outputs = new();
        public EntityLinkDescriptor? link;
        public EntityVisualizerBinding? visualizer;
        public Vector3 boundsMin, boundsMax;
        public EntityProperty[] defaultProperties;
    }
    public struct LeafInfo
    {
        public int brush, face;
        public LeafInfo(int brush, int face)
        {
            this.brush = brush;
            this.face = face;
        }
    }
    public class VisLeaf
    {
        public int[] portals;
        public bool IsEmpty;
        public int bspLeafID;
        public uint[] pvs;
        public ushort[] brushes;
        public bool HasSkybox;
    }
    public class Portal
    {
        public int LeafFront = -1;
        public int LeafBack = -1;
        public Vector3[] Vertices;
        public Plane Plane;
        public ushort[] Brushes;
#if compiler
        public ulong[] mightsee;
        public ulong[] pvs;
        public int worked;
        public bool leafOverlapped;
        public List<(ushort, ushort)> brushFaces = new List<(ushort, ushort)>();
        public int planenum;
#endif
    }

    public static class OctreeRoot
    {
        public static List<Octree> allNodes = new List<Octree>();
    }
    public class Octree
    {
        const float minSize = 32;

        public int depth, id;
        public List<int> contents = new List<int>();
        public int[] children = new int[8] {
            -1,-1,
            -1,-1,
            -1,-1,
            -1,-1,
        };
        //public int[] visible;
        //public bool[,] faceToFaceVisibility;
        public BoundingBox box;
        public BoundingBox[] corners;
        public bool isEnd = false;

        public static Vector3[] searchDirections = new Vector3[6]
        {
            Vector3.Up,
            Vector3.Right,
            Vector3.Forward,

            -Vector3.Up,
            -Vector3.Right,
            -Vector3.Forward
        };
        public Octree() { }

        public Octree(BoundingBox box, int d, int id)
        {
            this.box = box;
            this.depth = d + 1;
            this.id = id;
            var boxQuarter = new BoundingBox(Vector3.Zero, (box.Max - box.Min) * 0.5f);

            //visible = new int[6] { -1,-1,-1,-1,-1,-1 };
            corners = new BoundingBox[8];

            Vector3[] c = new Vector3[]
            {
                new Vector3(0,0,0),
                new Vector3(1,0,0),
                new Vector3(0,1,0),
                new Vector3(1,1,0),
                new Vector3(0,0,1),
                new Vector3(1,0,1),
                new Vector3(0,1,1),
                new Vector3(1,1,1),
            };

            for (int i = 0; i < 8; i++)
            {
                Vector3 placement = Replace(c[i], boxQuarter.Max);

                corners[i] = new BoundingBox(box.Min + placement, box.Min + boxQuarter.Max + placement);
            }
        }

        private Vector3 Replace(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X == 1 ? b.X : 0, a.Y == 1 ? b.Y : 0, a.Z == 1 ? b.Z : 0);
        }

        private void CreateChild(int index, BoundingBox box)
        {
            children[index] = OctreeRoot.allNodes.Count;
            OctreeRoot.allNodes.Add(new Octree(box, depth, children[index]));
        }
        public void TestAdd(BoundingBox box, int brush)
        {
            if (contents.Contains(brush) || this.box.Contains(box) == ContainmentType.Disjoint) return;

            contents.Add(brush);

            if (isEnd || Math.Abs((this.box.Max - this.box.Min).X) < minSize)
            {
                isEnd = true;
                return;
            }

            if (!isEnd)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (corners[i].Contains(box) == ContainmentType.Disjoint) continue;

                    if (children[i] == -1)
                    {
                        CreateChild(i, corners[i]);
                    }

                    OctreeRoot.allNodes[children[i]].TestAdd(box, brush);
                }
            }
        }
        public int Traverse(Vector3 position)
        {
            for (int i = 0; i < 8; i++)
            {
                if (corners[i].Contains(position) == ContainmentType.Disjoint) continue;

                if (children[i] >= 0)
                {
                    return OctreeRoot.allNodes[children[i]].Traverse(position);
                }
            }

            return id;
        }
        public int TraverseBox(BoundingBox bounds)
        {
            if (isEnd) return id;

            for (int i = 0; i < 8; i++)
            {
                if (corners[i].Contains(bounds) == ContainmentType.Disjoint) continue;

                if (children[i] >= 0)
                {
                    return OctreeRoot.allNodes[children[i]].TraverseBox(bounds);
                }
            }
            return id;
        }
        public List<int> Raycast(Ray ray)
        {
            List<int> contents = new List<int>();

            if (isEnd)
            {
                return this.contents;
            }

            for (int i = 0; i < 8; i++)
            {
                if (children[i] > 0)
                {
                    float? res = ray.Intersects(OctreeRoot.allNodes[children[i]].box);

                    if (OctreeRoot.allNodes[children[i]].box.Contains(ray.Position) == ContainmentType.Contains || res.HasValue)
                    {
                        if (OctreeRoot.allNodes[children[i]].isEnd && OctreeRoot.allNodes[children[i]].box.Contains(ray.Position) == ContainmentType.Contains)
                        {
                            contents.AddRange(OctreeRoot.allNodes[children[i]].contents);
                            continue;
                        }
                        else
                        {
                            contents.AddRange(OctreeRoot.allNodes[children[i]].Raycast(ray));
                            continue;
                        }
                    }
                }
            }
            return contents;
        }
        public void Iterate(Action<Octree> onLast)
        {
            onLast.Invoke(this);
            for (int i = 0; i < 8; i++)
            {
                if (children[i] > 0)
                    OctreeRoot.allNodes[children[i]].Iterate(onLast);
            }
        }
        //public void CalculateVis_Crude()
        //{
        //    //These are always going to be cubes, so one side length is the same as the rest.
        //    float castDistance = box.Max.X - box.Min.X;
        //    Vector3 boxCenter = (box.Min + box.Max) / 2f;
        //
        //    for (int i = 0; i < 6; i++)
        //    {
        //        visible[i] = OctreeRoot.allNodes[0].Traverse(boxCenter + castDistance * searchDirections[i]);
        //        if (visible[i] == id) visible[i] = -1;
        //    }
        //
        //    //This is the crude and inaccurate version of this function, likely to cull when it shouldnt. I'll write the proper approach with edge intersection at some point
        //    BSPHit hit;
        //    faceToFaceVisibility = new bool[6, 6];
        //    for (int i = 0; i < 6; i++)
        //    {
        //        for (int j = 0; j < 6; j++)
        //        {
        //            if (i == j) continue;
        //
        //            Vector3 targetPoint = castDistance/2f * searchDirections[i] + boxCenter;
        //            Vector3 rayStart = boxCenter - (castDistance * 0.5f) * searchDirections[i];
        //            Vector3 rayDir = targetPoint - boxCenter; rayDir.Normalize();
        //
        //            hit = BSPRoot.TraceRay(new Ray(rayStart, rayDir), Vector3.Distance(rayStart,targetPoint));
        //            faceToFaceVisibility[i, j] = !hit.hit || faceToFaceVisibility[i, j];
        //        }
        //    }
        //}
        //public bool TestVisibility(int sideFrom, int sideTo)
        //{
        //    if (sideFrom == -1 || sideTo == -1) return true;
        //    if (sideFrom == sideTo) return false;
        //
        //    return faceToFaceVisibility[sideFrom,sideTo];
        //}
    }

    public class LightNodeBundle
    {
        public LightNode[] children;
        public BoundingBox box;
        public float gridSize = 2f;
        public struct LightData
        {
            public bool lightBlocked;
            public int lightNum;
        }
        public struct LightNode
        {
            public Vector3 pos;
            public LightData[] data;
            public Vector3[] indirectCoefficients;
            public Vector3[][] groupIndirectCoefficients;
        }

        public LightNodeBundle(BoundingBox box, float gridSize = 2f, bool spreadDistribution = false, bool skipOccluded = true)
        {
            this.box = box;
            this.gridSize = gridSize;
#if compiler
            List<LightNode> nodes = new List<LightNode>();

            for (int x = 0; x <= (box.Max.X - box.Min.X) / gridSize; x++)
            {
                for (int y = 0; y <= (box.Max.Y - box.Min.Y) / gridSize; y++)
                {
                    for (int z = 0; z <= (box.Max.Z - box.Min.Z) / gridSize; z++)
                    {
                        if (spreadDistribution && ((x - (int)(box.Min.X / gridSize)) + (y - (int)(box.Min.Y / gridSize)) + (z - (int)(box.Min.Z / gridSize))) % 2 != 0) continue;

                        var pos = new Vector3(x * gridSize, y * gridSize, z * gridSize) + box.Min;

                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(0, -1, 0) * 0.025f)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(0, 1, 0) * 0.025f)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(-1, 0, 0) * 0.025f)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(1, 0, 0) * 0.025f)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(0, 0, -1) * 0.025f)].solid) continue;
                        if (skipOccluded && BSPRoot.nodes[BSPRoot.Traverse(pos + new Vector3(0, 0, 1) * 0.025f)].solid) continue;

                        nodes.Add(new LightNode { pos = CMath.ClampToBoundingBox(pos, box) });
                    }
                }
            }

            children = nodes.ToArray();
#endif
        }

        private Dictionary<long, List<int>> spatialGrid;
        private readonly object gridBuildLock = new object();
        private void EnsureGridBuilt()
        {
            if (spatialGrid != null) return;
            lock (gridBuildLock)
            {
                if (spatialGrid != null) return;

                float cellSize = gridSize > 0f ? gridSize : EstimateCellSize();
                var grid = new Dictionary<long, List<int>>(children.Length);

                for (int i = 0; i < children.Length; i++)
                {
                    long key = CellKey(children[i].pos, cellSize);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new List<int>(4);
                    list.Add(i);
                }

                this.cellSizeResolved = cellSize;
                spatialGrid = grid; // publish last, after fully built
            }
        }
        private float cellSizeResolved;
        private float EstimateCellSize()
        {
            if (children.Length < 2) return 2f;
            var size = box.Max - box.Min;
            float volume = MathF.Max(size.X, 0.001f) * MathF.Max(size.Y, 0.001f) * MathF.Max(size.Z, 0.001f);
            return MathF.Max(0.1f, MathF.Cbrt(volume / children.Length));
        }
        private static long CellKey(Vector3 pos, float cellSize)
        {
            const int bias = 1 << 19; // supports cell coords roughly ±500k
            int cx = (int)MathF.Floor(pos.X / cellSize) + bias;
            int cy = (int)MathF.Floor(pos.Y / cellSize) + bias;
            int cz = (int)MathF.Floor(pos.Z / cellSize) + bias;
            return ((long)cx << 42) | ((long)cy << 21) | (uint)cz;
        }
        private List<int> CollectCandidates(Vector3 position, int desiredCount)
        {
            var result = new List<int>(16);
            int cx = (int)MathF.Floor(position.X / cellSizeResolved);
            int cy = (int)MathF.Floor(position.Y / cellSizeResolved);
            int cz = (int)MathF.Floor(position.Z / cellSizeResolved);

            const int maxRadius = 4;
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                result.Clear();
                for (int x = cx - radius; x <= cx + radius; x++)
                    for (int y = cy - radius; y <= cy + radius; y++)
                        for (int z = cz - radius; z <= cz + radius; z++)
                        {
                            long key = CellKey(new Vector3(x, y, z) * cellSizeResolved, cellSizeResolved);
                            if (spatialGrid.TryGetValue(key, out var cell))
                                result.AddRange(cell);
                        }

                if (result.Count >= desiredCount) break;
            }

            if (result.Count == 0)
                for (int i = 0; i < children.Length; i++) result.Add(i); // pathological fallback

            return result;
        }

        public LightNode Traverse(Vector3 position)
        {
            if (children.Length == 0) return default;
            EnsureGridBuilt();

            var candidates = CollectCandidates(position, desiredCount: 1);
            float best = float.MaxValue;
            int bestIdx = candidates[0];
            foreach (int i in candidates)
            {
                float d = Vector3.DistanceSquared(children[i].pos, position);
                if (d < best) { best = d; bestIdx = i; }
            }
            return children[bestIdx];
        }
        public LightNode[] GetClosest(Vector3 position)
        {
            if (children.Length == 0) return Array.Empty<LightNode>();
            EnsureGridBuilt();

            var candidates = CollectCandidates(position, desiredCount: 10);

            Span<float> bestDistSq = stackalloc float[5] { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
            Span<int> bestIndex = stackalloc int[5] { -1, -1, -1, -1, -1 };

            foreach (int i in candidates)
            {
                var toNode = children[i].pos - position;
                float distSq = toNode.LengthSquared();
                if (distSq >= bestDistSq[4]) continue;

                float dist = MathF.Sqrt(distSq);
                if (dist > 0.001f && BSPRoot.TraceRay(new Ray(position, toNode / dist), dist).hit)
                    continue; // occluded

                int insertAt = 4;
                while (insertAt > 0 && distSq < bestDistSq[insertAt - 1])
                {
                    bestDistSq[insertAt] = bestDistSq[insertAt - 1];
                    bestIndex[insertAt] = bestIndex[insertAt - 1];
                    insertAt--;
                }
                bestDistSq[insertAt] = distSq;
                bestIndex[insertAt] = i;
            }

            int count = 0;
            while (count < 5 && bestIndex[count] != -1) count++;

            var result = new LightNode[count];
            for (int i = 0; i < count; i++)
                result[i] = children[bestIndex[i]];

            return result;
        }
        public void Iterate(Action<int> onLast)
        {
            for (int i = 0; i < children.Length; i++)
            {
                onLast.Invoke(i);
            }
        }
    }

    public struct BSPFile
    {
        public BSPNode[] nodes;
    }
    public struct VisFile
    {
        public VisLeaf[] leaves;
        public Portal[] portals;
    }

    public static class VisRoot
    {
        public static VisLeaf[] visLeaves;
        public static Portal[] visPortals;
    }
    public static class BSPRoot
    {
        public static BSPNode[] nodes;
#if compiler || rockwall
        public static List<BSPNode> tempNodes = new List<BSPNode>();
        public const float EPS = 0.001f;
        static int SamePlane(Plane a, Plane b, float normalEps = EPS, float dEps = EPS)
        {
            var na = a.Normal;
            var nb = b.Normal;

            float la = na.Length();
            float lb = nb.Length();
            if (la < 1e-6f || lb < 1e-6f) return 0;

            na /= la; nb /= lb;
            float da = a.D / la;
            float db = b.D / lb;

            if (Math.Abs(Vector3.Dot(na, nb) - 1f) <= normalEps)
                return Math.Abs(da - db) <= dEps ? 1 : 0;

            if (Math.Abs(Vector3.Dot(na, nb) + 1f) <= normalEps)
            {
                db = -db;
                return Math.Abs(da - db) <= dEps ? -1 : 0;
            }

            return 0;
        }
        public static void Reset()
        {
            nodes = null;
            tempNodes = new List<BSPNode>();
        }
        public static void Cut(Plane splittingPlane, Brush[] brushes, BoundingBox[] brushBounds, int brushFrom, int face, int pside, bool allowDuplicates = false)
        {
            if (brushes[brushFrom].isEntity) return;

            Stack<BSPNode> stack = new Stack<BSPNode>();
            stack.Push(tempNodes[0]);

            void AddBrushToNodes(BSPNode node, int brush, bool flipped)
            {
                bool backside = false, frontside = false;

                for (int f = 0; f < brushes[brush].faces.Length; f++)
                {
                    for (int v = 0; v < brushes[brush].faces[f].indices.Length; v++)
                    {
                        Vector3 vertex = brushes[brush].vertices[brushes[brush].faces[f].indices[v]];

                        float dotCoord = splittingPlane.DotCoordinate(vertex + brushes[brush].position);
                        if (dotCoord < -EPS)
                            backside = true;
                        else if (dotCoord > EPS)
                            frontside = true;

                        if (backside && frontside) break;
                    }
                    if (backside && frontside) break;
                }

                // If the existing split node's plane is flipped relative to ours,
                // what we think is "back" is actually stored as "front" in that node and vice versa
                uint targetBack = flipped ? node.front : node.back;
                uint targetFront = flipped ? node.back : node.front;

                if (backside)
                {
                    Array.Resize(ref tempNodes[(int)targetBack].nodeContents, tempNodes[(int)targetBack].nodeContents.Length + 1);
                    tempNodes[(int)targetBack].nodeContents[tempNodes[(int)targetBack].nodeContents.Length - 1] = (ushort)brush;
                }
                if (frontside)
                {
                    Array.Resize(ref tempNodes[(int)targetFront].nodeContents, tempNodes[(int)targetFront].nodeContents.Length + 1);
                    tempNodes[(int)targetFront].nodeContents[tempNodes[(int)targetFront].nodeContents.Length - 1] = (ushort)brush;
                }
            }

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                if (node.split)
                {
                    // If this plane already exists, and we dont want to create a degenerate node, so we'll check to see if we need to worry about this plane
                    // Then this node will just straddle two solid leaves, or add the brush to the correct side.
                    if (!allowDuplicates && (tempNodes[(int)node.front].nodeContents.Contains((ushort)brushFrom) || tempNodes[(int)node.back].nodeContents.Contains((ushort)brushFrom)))
                    {
                        int planeMatch = SamePlane(node.splittingPlane, splittingPlane);
                        if (planeMatch != 0)
                        {
                            AddBrushToNodes(node, brushFrom, flipped: planeMatch == -1);
                            continue;
                        }
                    }

                    stack.Push(tempNodes[(int)node.front]);
                    stack.Push(tempNodes[(int)node.back]);
                }
                else
                {
                    node.Cut(splittingPlane, brushes, brushBounds, brushFrom, face, pside);
                }
            }
        }
        public static void DiagnoseUnexpectedSolids(BSPNode[] nodes, Brush[] brushes)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (n == null) continue;
                int count = n.nodeContents?.Length ?? 0;
                if (n.solid && count == 0)
                {
                    Console.WriteLine($"[BUG] Node {i} marked solid but has 0 contents. parent={n.parent} split={n.split}");
                }
                if (n.solid && count == 1)
                {
                    ushort b = n.nodeContents[0];
                    if (b >= brushes.Length)
                    {
                        Console.WriteLine($"[BUG] Node {i} has invalid brush index {b}. parent={n.parent}");
                    }
                }
            }
        }

        public static void DoubleCheck(Brush[] brushes)
        {
            Stack<BSPNode> stack = new Stack<BSPNode>();
            stack.Push(tempNodes[0]);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                if (node.split)
                {
                    stack.Push(tempNodes[(int)node.front]);
                    stack.Push(tempNodes[(int)node.back]);
                    node.solid = false;
                }
                else
                {
                    node.solid = node.nodeContents != null && node.nodeContents.Length >= 1;

                    if (node.solid) node.nodeFlag = (byte)(brushes[node.nodeContents[0]].isSkybox ? BSPNode.SkyboxNode : brushes[node.nodeContents[0]].isClip ? BSPNode.ClipNode : 0);
                }
            }

            DiagnoseUnexpectedSolids(tempNodes.ToArray(), brushes);
        }
#endif
        public static uint Traverse(Vector3 point)
        {
            uint node = 0;
            while (nodes[node].split)
                node = nodes[node].splittingPlane.DotCoordinate(point) >= 0
                    ? nodes[node].front : nodes[node].back;
            return nodes[node].id;
        }

        private struct TraceFrame
        {
            public uint farNode;
            public Vector3 mid;
            public Vector3 p2;
            public Vector3 normal;
            public Vector3 binormal;
            public int surf;
        }


        public static BSPHit TraceRay(Ray ray, float distance, bool ignoreClip = true, int ignoreBrush = -1, int ignoreFace = -1)
        {
            BSPHit hit = new BSPHit { point = ray.Position + ray.Direction * distance, normal = ray.Direction };

            Vector3 p1 = ray.Position;
            Vector3 p2 = ray.Position + ray.Direction * distance;
            uint nodeNum = 0;
            Vector3 normal = Vector3.One;
            Vector3 binormal = Vector3.One;
            int surf = 0;

            Span<TraceFrame> stack = stackalloc TraceFrame[64];
            int stackTop = 0;
            bool found = false;
            Vector3 intersection = p1;

            while (true)
            {
                BSPNode node = nodes[nodeNum];

                if (node.solid)
                {
                    intersection = p1;
                    bool rejected = false;
                    if (ignoreBrush >= 0 && ignoreFace == -1 && node.brush == ignoreBrush) rejected = true;
                    if (ignoreBrush >= 0 && ignoreFace >= 0 && node.brush == ignoreBrush && node.face == ignoreFace) rejected = true;
                    if (!rejected && node.nodeFlag == BSPNode.ClipNode && ignoreClip) rejected = true;

                    if (!rejected)
                    {
                        found = true;
                        hit.node = nodeNum;
                        break;
                    }

                    if (stackTop == 0) break;
                    stackTop--;
                    TraceFrame frame = stack[stackTop];
                    p1 = frame.mid;
                    p2 = frame.p2;
                    nodeNum = frame.farNode;
                    normal = frame.normal;
                    binormal = frame.binormal;
                    surf = frame.surf;
                    continue;
                }

                if (!node.split)
                {
                    intersection = p2;
                    surf = 0;

                    if (stackTop == 0) break;
                    stackTop--;
                    TraceFrame frame = stack[stackTop];
                    p1 = frame.mid;
                    p2 = frame.p2;
                    nodeNum = frame.farNode;
                    normal = frame.normal;
                    binormal = frame.binormal;
                    surf = frame.surf;
                    continue;
                }

                Plane plane = node.splittingPlane;
                float t1 = plane.DotCoordinate(p1);
                float t2 = plane.DotCoordinate(p2);

                if (t1 >= 0f && t2 >= 0f)
                {
                    nodeNum = node.front;
                    continue;
                }
                if (t1 < 0f && t2 < 0f)
                {
                    nodeNum = node.back;
                    continue;
                }

                float frac = Math.Clamp(t1 / (t1 - t2), 0f, 1f);
                Vector3 mid = p1 + frac * (p2 - p1);

                int nodeSurf = 0;
#if !rockwall && !compiler
                nodeSurf = string.IsNullOrEmpty(node.surface) ? 0 :
                    GlobalMapData.materialNameToIndex.TryGetValue(node.surface, out var val) ? val : 0;
#endif

                stack[stackTop] = new TraceFrame
                {
                    farNode = t1 >= 0f ? node.back : node.front,
                    mid = mid,
                    p2 = p2,
                    normal = plane.Normal,
                    binormal = node.binormal,
                    surf = nodeSurf
                };
                stackTop++;

                nodeNum = t1 >= 0f ? node.front : node.back;
                p2 = mid;
            }

            if (found)
            {
                hit = new BSPHit { point = intersection, hit = true, node = nodeNum, normal = normal, surf = surf, binormal = binormal };
            }

            if (Vector3.Dot(hit.normal, ray.Direction) > 0) hit.normal *= -1;
            return hit;
        }
    }
    [System.Serializable]
    public class BSPNode
    {
        public const byte ClipNode = 2;
        public const byte SkyboxNode = 4;

        //public Vector3 splitPos, splitNormal; // Plane that splits the room
        public Plane splittingPlane;
        public Vector3 binormal;

        public float spx
        {
            get
            {
                return splittingPlane.Normal.X;
            }
            set
            {
                splittingPlane.Normal.X = value;
            }
        }
        public float spy
        {
            get
            {
                return splittingPlane.Normal.Y;
            }
            set
            {
                splittingPlane.Normal.Y = value;
            }
        }
        public float spz
        {
            get
            {
                return splittingPlane.Normal.Z;
            }
            set
            {
                splittingPlane.Normal.Z = value;
            }
        }
        public float d
        {
            get
            {
                return splittingPlane.D;
            }
            set
            {
                splittingPlane.D = value;
            }
        }

        public uint front;
        public uint back;
        public uint parent;
        public uint id = 0;
        public byte nodeFlag;
        public ushort[] nodeContents;
        public bool solid;
        public bool split;
        public ushort brush;
        public byte face;
        public string surface;
        public float bnx
        {
            get
            {
                return binormal.X;
            }
            set
            {
                binormal.X = value;
            }
        }
        public float bny
        {
            get
            {
                return binormal.Y;
            }
            set
            {
                binormal.Y = value;
            }
        }
        public float bnz
        {
            get
            {
                return binormal.Z;
            }
            set
            {
                binormal.Z = value;
            }
        }

#if rockwall || compiler

        public bool dirty;
        List<BSPNode> Nodes => BSPRoot.tempNodes;
        public void Cut(Plane splittingPlane, Brush[] brushes, BoundingBox[] brushBounds, int brushFrom, int face, int pside)
        {
            if (!nodeContents.Contains((ushort)brushFrom)) return;
            split = true;

            this.brush = (ushort)brushFrom;
            this.face = (byte)face;

            this.splittingPlane = splittingPlane;

            var backContents = new List<ushort>() { (ushort)brushFrom };
            var frontContents = new List<ushort>();

            foreach (int content in nodeContents)
            {
                if (content == brushFrom) continue;
                if (brushes[content].isEntity) continue;
                if (brushes[content].isLightNodeVolume) continue;
                if (brushes[content].isTrigger) continue;
                if (brushes[content].isClip) continue;

                bool backside = false, frontside = false;

                for (int f = 0; f < brushes[content].faces.Length; f++)
                {
                    for (int v = 0; v < brushes[content].faces[f].indices.Length; v++)
                    {
                        Vector3 vertex = brushes[content].vertices[brushes[content].faces[f].indices[v]];

                        float dotCoord = splittingPlane.DotCoordinate(vertex + brushes[content].position);
                        if (dotCoord < -BSPRoot.EPS)
                            backside = true;
                        else if (dotCoord > BSPRoot.EPS)
                            frontside = true;

                        //No need to keep checking.
                        if (backside && frontside) break;
                    }
                    if (backside && frontside) break;
                }

                if (backside) backContents.Add((ushort)content);
                if (frontside) frontContents.Add((ushort)content);
            }

            bool skybox = brushes[brushFrom].isSkybox;
            bool clip = brushes[brushFrom].isClip;

            var bface = brushes[brushFrom].faces[face];

            front = (uint)Nodes.Count;
            back = (uint)Nodes.Count + 1;
            Nodes.Add(new BSPNode { nodeContents = frontContents.ToArray(), parent = id, id = front, nodeFlag = (byte)(skybox ? SkyboxNode : clip ? ClipNode : 0), brush = (ushort)brushFrom, face = (byte)face, binormal = bface.binormal, surface = bface.materialName });
            Nodes.Add(new BSPNode { nodeContents = backContents.ToArray(), solid = true, parent = id, id = back, nodeFlag = (byte)(skybox ? SkyboxNode : clip ? ClipNode : 0), brush = (ushort)brushFrom, face = (byte)face, binormal = bface.binormal, surface = bface.materialName });
        }
#endif
    }

    public struct BSPHit
    {
        public Vector3 point, normal, binormal;
        public bool hit;
        public uint node;
        public int surf;
    }

    public struct Navmesh
    {
        public Navpatch[] patches;
    }
    public struct Navpatch
    {
        public Vector3[] points;
    }

    public struct AINode
    {
        public enum NodeType
        {
            Ground,
            Air,
        }
        public NodeType type;
        public int[] connections;
        public int zone;

        public Vector3 position;
    }

    public struct NodeGraph
    {
        public AINode[] nodes;
    }

    public struct Map
    {
        public Brush[] brushes;
        public Terrain[] terrains;
        public BoundingBox[] brushBounds;
        public MapPropModel[] mapModels;

        public LeafPolygon[] leafPolygons;
        public int[] leafPolyStart, leafPolyCount;
        public VertexLightmapped[] staticGeomVertices;

        public bool hasVis;
        public Octree root;
        public List<Octree> octreeNodes;
        public EntityReference[] entities;
        public LightNodeBundle[] lightNodes;
        public string[] lightGroupKeys;
        public NodeGraph nodegraph;
    }

    public static class GlobalMapData
    {
        public static Map activeMap;
        public static Material[] loadedMaterials;
#if !rockwall
        public static ImmutableDictionary<string, int> materialNameToIndex;
#else
        public static Dictionary<string, int> materialNameToIndex;
#endif
    }
}
[System.Serializable]
public struct EntityOutput
{
    public string entityTarget;
    public string entityInputTarget;
    public string inputParameters;

    // scripting rocks and is awesome and epic
    public string scriptSource;
    [JsonIgnore()]
    public EXScript script;

    public float delay;
    public int refire;
}
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexLightmapped : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Binormal;
    public Vector2 TextureCoordinate;
    public Vector2 LightmapCoordinate;

    public static readonly VertexDeclaration VertexDeclaration;
    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public VertexLightmapped(Vector3 position, Vector3 normal, Vector2 textureCoordinate, Vector2 lightmapCoordinate)
    {
        Position = position;
        Normal = normal;
        Tangent = Vector3.Zero;
        Binormal = Vector3.Zero;
        TextureCoordinate = textureCoordinate;
        LightmapCoordinate = lightmapCoordinate;
    }

    public VertexLightmapped(Vector3 position, Vector3 normal, Vector3 tangent, Vector3 binormal, Vector2 textureCoordinate, Vector2 lightmapCoordinate)
    {
        Position = position;
        Normal = normal;
        Tangent = tangent;
        Binormal = binormal;
        TextureCoordinate = textureCoordinate;
        LightmapCoordinate = lightmapCoordinate;
    }

    public override int GetHashCode()
    {
        return (((Position.GetHashCode() * 397) ^ Normal.GetHashCode()) * (397 ^ LightmapCoordinate.GetHashCode())) ^ TextureCoordinate.GetHashCode();
    }

    public override string ToString()
    {
        string[] obj = new string[7] { "{{Position:", null, null, null, null, null, null };
        Vector3 position = Position;
        obj[1] = position.ToString();
        obj[2] = " Normal:";
        position = Normal;
        obj[3] = position.ToString();
        obj[4] = " TextureCoordinate:";
        Vector2 textureCoordinate = TextureCoordinate;
        obj[5] = textureCoordinate.ToString();
        obj[6] = "}}";
        return string.Concat(obj);
    }

    public static bool operator ==(VertexLightmapped left, VertexLightmapped right)
    {
        if (left.Position == right.Position && left.Normal == right.Normal &&
            left.Tangent == right.Tangent && left.Binormal == right.Binormal)
        {
            return left.TextureCoordinate == right.TextureCoordinate && left.LightmapCoordinate == right.LightmapCoordinate;
        }
        return false;
    }
    public static bool operator !=(VertexLightmapped left, VertexLightmapped right) => !(left == right);

    public override bool Equals(object obj)
    {
        if (obj == null || obj.GetType() != GetType()) return false;
        return this == (VertexLightmapped)obj;
    }

    static VertexLightmapped()
    {
        VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
            new VertexElement(36, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0),
            new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(56, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));
    }
}
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexModelLightmapped : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Binormal;
    public Vector2 TextureCoordinate;
    public Vector2 LightmapCoordinate;

    public static readonly VertexDeclaration VertexDeclaration;
    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public VertexModelLightmapped(
        Vector3 position,
        Vector3 normal,
        Vector3 tangent,
        Vector3 binormal,
        Vector2 textureCoordinate,
        Vector2 lightmapCoordinate)
    {
        Position = position;
        Normal = normal;
        Tangent = tangent;
        Binormal = binormal;
        TextureCoordinate = textureCoordinate;
        LightmapCoordinate = lightmapCoordinate;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Position.GetHashCode();
            hash = (hash * 397) ^ Normal.GetHashCode();
            hash = (hash * 397) ^ Tangent.GetHashCode();
            hash = (hash * 397) ^ Binormal.GetHashCode();
            hash = (hash * 397) ^ TextureCoordinate.GetHashCode();
            hash = (hash * 397) ^ LightmapCoordinate.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{{Position:{Position} Normal:{Normal} Tangent:{Tangent} Binormal:{Binormal} TextureCoordinate:{TextureCoordinate}}}";
    }

    public static bool operator ==(VertexModelLightmapped left, VertexModelLightmapped right)
    {
        return left.Position == right.Position
            && left.Normal == right.Normal
            && left.Tangent == right.Tangent
            && left.Binormal == right.Binormal
            && left.TextureCoordinate == right.TextureCoordinate
            && left.LightmapCoordinate == right.LightmapCoordinate;
    }

    public static bool operator !=(VertexModelLightmapped left, VertexModelLightmapped right)
    {
        return !(left == right);
    }

    public override bool Equals(object obj)
    {
        if (obj == null || obj.GetType() != GetType())
            return false;
        return this == (VertexModelLightmapped)obj;
    }

    static VertexModelLightmapped()
    {
        VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
            new VertexElement(36, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0),
            new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(56, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1)
        );
    }
}
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainVertex : IVertexType
{
    public Vector3 Position;

    public Vector2 LightmapCoordinate;

    public Vector3 Normal;

    public Vector3 TextureCoordinate;

    public NormalizedShort4 Tangent;

    public static readonly VertexDeclaration VertexDeclaration;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public TerrainVertex(Vector3 position, Vector2 lightmapCoordinate,
                         Vector3 normal, Vector3 textureCoordinate,
                         Vector3 tangent, float handedness)
    {
        Position = position;
        LightmapCoordinate = lightmapCoordinate;
        Normal = normal;
        TextureCoordinate = textureCoordinate;
        Tangent = new NormalizedShort4(tangent.X, tangent.Y, tangent.Z, handedness);
    }

    public override int GetHashCode()
    {
        return (((((Position.GetHashCode() * 397) ^ LightmapCoordinate.GetHashCode()) * 397) ^ Normal.GetHashCode()) * 397) ^ TextureCoordinate.GetHashCode();
    }

    //public override string ToString()
    //{
    //    string[] obj = new string[9] { "{{Position:", null, null, null, null, null, null, null, null };
    //    Vector3 position = Position;
    //    obj[1] = position.ToString();
    //    obj[2] = " Color:";
    //    Color color = LightmapCoordinate;
    //    obj[3] = color.ToString();
    //    obj[4] = " Normal:";
    //    position = Normal;
    //    obj[5] = position.ToString();
    //    obj[6] = " TextureCoordinate:";
    //    Vector2 textureCoordinate = TextureCoordinate;
    //    obj[7] = textureCoordinate.ToString();
    //    obj[8] = "}}";
    //    return string.Concat(obj);
    //}

    public static bool operator ==(TerrainVertex left, TerrainVertex right)
    {
        if (left.Position == right.Position && left.LightmapCoordinate == right.LightmapCoordinate && left.Normal == right.Normal)
        {
            return left.TextureCoordinate == right.TextureCoordinate;
        }

        return false;
    }

    public static bool operator !=(TerrainVertex left, TerrainVertex right)
    {
        return !(left == right);
    }

    public override bool Equals(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return this == (TerrainVertex)obj;
    }

    static TerrainVertex()
    {
        VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
            new VertexElement(20, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(32, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(44, VertexElementFormat.NormalizedShort4, VertexElementUsage.Tangent, 0));
    }
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapPropModelVertex : IVertexType
{
    public Vector3 Position;

    public Vector3 Color;

    public Vector3 Normal;

    public Vector2 TextureCoordinate;

    public static readonly VertexDeclaration VertexDeclaration;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public MapPropModelVertex(Vector3 position, Vector3 color, Vector3 normal, Vector2 textureCoordinate)
    {
        Position = position;
        Color = color;
        Normal = normal;
        TextureCoordinate = textureCoordinate;
    }

    public override int GetHashCode()
    {
        return (((((Position.GetHashCode() * 397) ^ Color.GetHashCode()) * 397) ^ Normal.GetHashCode()) * 397) ^ TextureCoordinate.GetHashCode();
    }

    public override string ToString()
    {
        string[] obj = new string[9] { "{{Position:", null, null, null, null, null, null, null, null };
        Vector3 position = Position;
        obj[1] = position.ToString();
        obj[2] = " Color:";
        Vector3 color = Color;
        obj[3] = color.ToString();
        obj[4] = " Normal:";
        position = Normal;
        obj[5] = position.ToString();
        obj[6] = " TextureCoordinate:";
        Vector2 textureCoordinate = TextureCoordinate;
        obj[7] = textureCoordinate.ToString();
        obj[8] = "}}";
        return string.Concat(obj);
    }

    public static bool operator ==(MapPropModelVertex left, MapPropModelVertex right)
    {
        if (left.Position == right.Position && left.Color == right.Color && left.Normal == right.Normal)
        {
            return left.TextureCoordinate == right.TextureCoordinate;
        }

        return false;
    }

    public static bool operator !=(MapPropModelVertex left, MapPropModelVertex right)
    {
        return !(left == right);
    }

    public override bool Equals(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return this == (MapPropModelVertex)obj;
    }

    static MapPropModelVertex()
    {
        VertexDeclaration = new VertexDeclaration(new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                                                  new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Color, 0),
                                                  new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                                                  new VertexElement(36, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
    }
}
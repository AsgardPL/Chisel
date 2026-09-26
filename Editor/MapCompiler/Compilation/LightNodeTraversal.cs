using Chisel.Utils;
using Microsoft.Xna.Framework;
using Rockwall;

namespace MapCompiler.Compilation;

public static class LightNodeTraversal
{
    public static LightNodeBundle GetClosestNodeBundle(Vector3 position)
    {
        var nodes = GlobalMapData.activeMap.lightNodes;
        if (nodes == null || nodes.Length == 0) return null;

        LightNodeBundle result = nodes[0];
        float closest = float.MaxValue;

        foreach (var node in nodes)
        {
            // Containment wins immediately
            if (node.box.Contains(position) == Microsoft.Xna.Framework.ContainmentType.Contains)
                return node;

            Vector3 checkPoint = CMath.ClampToBoundingBox(position, node.box);
            float distance = Vector3.Distance(checkPoint, position);

            if (distance >= closest) continue;

            // Reject bundles with solid geometry in between
            var hit = BSPRoot.TraceRay(
                new Ray(position, Vector3.Normalize(checkPoint - position)), distance);
            if (hit.hit) continue;

            result = node;
            closest = distance;
        }

        return result;
    }
}
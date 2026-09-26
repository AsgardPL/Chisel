using Chisel.Utils;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Threading.Tasks;

namespace Engine.Utils
{
    public static class LightNodeTraversal
    {
        public static LightNodeBundle GetClosestNodeBundle(Vector3 position)
        {
            var nodes = GlobalMapData.activeMap.lightNodes;
            if (nodes == null || nodes.Length == 0) return null;

            foreach (var node in nodes)
                if (node.box.Contains(position) == ContainmentType.Contains)
                    return node;

            float closestRawSq = float.MaxValue;
            foreach (var node in nodes)
            {
                float distSq = Vector3.DistanceSquared(CMath.ClampToBoundingBox(position, node.box), position);
                if (distSq < closestRawSq) closestRawSq = distSq;
            }

            float threshold = closestRawSq * 4f;
            LightNodeBundle result = null;
            float closestSq = float.MaxValue;

            foreach (var node in nodes)
            {
                Vector3 checkPoint = CMath.ClampToBoundingBox(position, node.box);
                float distSq = Vector3.DistanceSquared(checkPoint, position);
                if (distSq > threshold) continue;

                var hit = BSPRoot.TraceRay(new Ray(position, Vector3.Normalize(checkPoint - position)), MathF.Sqrt(distSq));
                if (distSq < closestSq && !hit.hit)
                {
                    result = node;
                    closestSq = distSq;
                }
            }

            if (result == null)
            {
                closestSq = float.MaxValue;
                foreach (var node in nodes)
                {
                    float distSq = Vector3.DistanceSquared(CMath.ClampToBoundingBox(position, node.box), position);
                    if (distSq < closestSq) { result = node; closestSq = distSq; }
                }
            }

            return result;
        }
    }
}

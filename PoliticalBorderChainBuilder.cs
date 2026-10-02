using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Converts the protected renderer's unordered frontier segments into stable,
    /// directed chains. Chains stop at topology junctions and when coastal status
    /// changes so every tessellated strip has one continuous width contract.
    /// </summary>
    internal static class PoliticalBorderChainBuilder
    {
        internal static List<Chain> Build(
            IList<PoliticalBorderGeometryCache.RawSegment> segments)
        {
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes =
                BuildNodes(segments);
            bool[] visited = new bool[segments.Count];
            List<Chain> result = new List<Chain>();

            foreach (Node node in nodes.Values)
            {
                if (node.Incidents.Count == 2) continue;
                foreach (int segmentIndex in node.Incidents)
                {
                    if (!visited[segmentIndex])
                        result.Add(Walk(node, segmentIndex, nodes, segments, visited));
                }
            }

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                if (visited[segmentIndex]) continue;
                PoliticalBorderGeometryCache.RawSegment segment = segments[segmentIndex];
                Node start = nodes[new PoliticalBorderGeometryCache.NodeKey(segment.First)];
                result.Add(Walk(start, segmentIndex, nodes, segments, visited));
            }
            return result;
        }

        private static Chain Walk(
            Node start,
            int firstSegment,
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            IList<PoliticalBorderGeometryCache.RawSegment> segments,
            bool[] visited)
        {
            Chain chain = new Chain();
            Node current = start;
            int segmentIndex = firstSegment;
            bool coastal = IsCoastal(segments[firstSegment]);
            PoliticalBorderGeometryCache.BorderLineStyle style =
                segments[firstSegment].Style;
            while (!visited[segmentIndex])
            {
                PoliticalBorderGeometryCache.RawSegment segment = segments[segmentIndex];
                bool forward = new PoliticalBorderGeometryCache.NodeKey(segment.First)
                    .Equals(current.Key);
                chain.Segments.Add(new DirectedSegment(segmentIndex, forward));
                visited[segmentIndex] = true;
                Vec2 nextPoint = forward ? segment.Second : segment.First;
                Node next = nodes[new PoliticalBorderGeometryCache.NodeKey(nextPoint)];
                if (next == start)
                {
                    chain.Closed = true;
                    break;
                }
                if (next.Incidents.Count != 2) break;

                int candidate = next.Incidents[0] == segmentIndex
                    ? next.Incidents[1] : next.Incidents[0];
                if (visited[candidate] || IsCoastal(segments[candidate]) != coastal
                    || segments[candidate].Style != style) break;
                current = next;
                segmentIndex = candidate;
            }
            return chain;
        }

        private static Dictionary<PoliticalBorderGeometryCache.NodeKey, Node> BuildNodes(
            IList<PoliticalBorderGeometryCache.RawSegment> segments)
        {
            Dictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes =
                new Dictionary<PoliticalBorderGeometryCache.NodeKey, Node>();
            for (int index = 0; index < segments.Count; index++)
            {
                AddNode(nodes, segments[index].First, index);
                AddNode(nodes, segments[index].Second, index);
            }
            return nodes;
        }

        private static void AddNode(
            IDictionary<PoliticalBorderGeometryCache.NodeKey, Node> nodes,
            Vec2 point,
            int segmentIndex)
        {
            PoliticalBorderGeometryCache.NodeKey key =
                new PoliticalBorderGeometryCache.NodeKey(point);
            Node node;
            if (!nodes.TryGetValue(key, out node))
            {
                node = new Node(key);
                nodes.Add(key, node);
            }
            node.Incidents.Add(segmentIndex);
        }

        internal static bool IsCoastal(PoliticalBorderGeometryCache.RawSegment segment)
        { return segment.LeftColor == segment.RightColor; }

        internal sealed class Chain
        {
            internal readonly List<DirectedSegment> Segments = new List<DirectedSegment>();
            internal bool Closed;
        }

        internal struct DirectedSegment
        {
            internal DirectedSegment(int segmentIndex, bool forward)
            { SegmentIndex = segmentIndex; Forward = forward; }
            internal int SegmentIndex;
            internal bool Forward;
        }

        private sealed class Node
        {
            internal Node(PoliticalBorderGeometryCache.NodeKey key) { Key = key; }
            internal readonly PoliticalBorderGeometryCache.NodeKey Key;
            internal readonly List<int> Incidents = new List<int>();
        }
    }
}

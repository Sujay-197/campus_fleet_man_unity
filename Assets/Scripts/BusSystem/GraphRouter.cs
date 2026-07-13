using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Dijkstra shortest path over a RoadGraph's weighted edges.</summary>
    public static class GraphRouter
    {
        public class RouteResult
        {
            public List<int> Nodes;
            public float Cost;
            public List<Vector3> Waypoints;
        }

        public static RouteResult FindPath(RoadGraph graph, int startNode, int endNode)
        {
            if (startNode == endNode)
                return new RouteResult
                {
                    Nodes = new List<int> { startNode },
                    Cost = 0f,
                    Waypoints = new List<Vector3> { graph.Nodes[startNode].Position }
                };

            var dist = new Dictionary<int, float>();
            var prevNode = new Dictionary<int, int>();
            var prevEdge = new Dictionary<int, RoadEdge>();
            var visited = new HashSet<int>();
            var frontier = new List<int>();

            foreach (var n in graph.Nodes) dist[n.Id] = float.MaxValue;
            dist[startNode] = 0f;
            frontier.Add(startNode);

            while (frontier.Count > 0)
            {
                frontier.Sort((a, b) => dist[a].CompareTo(dist[b]));
                int u = frontier[0];
                frontier.RemoveAt(0);
                if (visited.Contains(u)) continue;
                visited.Add(u);
                if (u == endNode) break;

                foreach (var e in graph.GetNeighborEdges(u))
                {
                    int v = e.NodeA == u ? e.NodeB : e.NodeA;
                    if (visited.Contains(v)) continue;
                    float alt = dist[u] + e.Length;
                    if (alt < dist[v])
                    {
                        dist[v] = alt;
                        prevNode[v] = u;
                        prevEdge[v] = e;
                        frontier.Add(v);
                    }
                }
            }

            if (!dist.ContainsKey(endNode) || dist[endNode] >= float.MaxValue) return null;

            var nodePath = new List<int>();
            var edgePath = new List<RoadEdge>();
            int cur = endNode;
            nodePath.Add(cur);
            while (cur != startNode)
            {
                edgePath.Add(prevEdge[cur]);
                cur = prevNode[cur];
                nodePath.Add(cur);
            }
            nodePath.Reverse();
            edgePath.Reverse();

            var waypoints = new List<Vector3> { graph.Nodes[startNode].Position };
            int fromNode = startNode;
            foreach (var e in edgePath)
            {
                var poly = new List<Vector3>(e.Polyline);
                Vector3 from = graph.Nodes[fromNode].Position;
                if ((poly[0] - from).sqrMagnitude > (poly[poly.Count - 1] - from).sqrMagnitude)
                    poly.Reverse();
                for (int k = 1; k < poly.Count; k++) waypoints.Add(poly[k]);
                fromNode = e.NodeA == fromNode ? e.NodeB : e.NodeA;
            }

            return new RouteResult { Nodes = nodePath, Cost = dist[endNode], Waypoints = waypoints };
        }

        // Cost is called heavily by InsertionPlanner (O(plan^2) trial insertions each doing
        // O(plan) cost lookups). The graph is static within a simulation run, so memoize the
        // shortest-path cost per ordered node pair. The cache auto-resets when a different
        // graph instance is queried, keeping unit tests (which use throwaway graphs) correct.
        static RoadGraph _costCacheGraph;
        static Dictionary<long, float> _costCache;

        public static float Cost(RoadGraph graph, int startNode, int endNode)
        {
            if (!ReferenceEquals(graph, _costCacheGraph))
            {
                _costCacheGraph = graph;
                _costCache = new Dictionary<long, float>();
            }

            long key = ((long)startNode << 32) | (uint)endNode;
            if (_costCache.TryGetValue(key, out float cached)) return cached;

            var r = FindPath(graph, startNode, endNode);
            float cost = r?.Cost ?? float.MaxValue;
            _costCache[key] = cost;
            return cost;
        }
    }
}

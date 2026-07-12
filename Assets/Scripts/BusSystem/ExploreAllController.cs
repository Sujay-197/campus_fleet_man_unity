using System.Collections.Generic;
using System.Linq;

namespace BusSystem
{
    /// <summary>
    /// Greedy walk that prefers unvisited edges, so the bus eventually drives every
    /// road. When all edges have been visited the flags reset and it keeps looping.
    /// </summary>
    public class ExploreAllController : IBusController
    {
        const int MaxStepsPerRoute = 48;

        public List<int> NextRoute(RoadGraph graph, int currentNode)
        {
            if (graph.Edges.Count > 0 && graph.Edges.All(e => e.Visited))
                foreach (var e in graph.Edges) e.Visited = false;

            var route = new List<int> { currentNode };
            int node = currentNode;
            int lastEdge = -1;

            for (int i = 0; i < MaxStepsPerRoute; i++)
            {
                var edges = graph.GetNeighborEdges(node).ToList();
                if (edges.Count == 0) break;

                // Prefer an unvisited edge; avoid immediately backtracking when possible.
                RoadEdge pick = edges.FirstOrDefault(e => !e.Visited && e.Id != lastEdge)
                                ?? edges.FirstOrDefault(e => !e.Visited)
                                ?? edges.FirstOrDefault(e => e.Id != lastEdge)
                                ?? edges[0];

                pick.Visited = true;
                lastEdge = pick.Id;
                node = pick.NodeA == node ? pick.NodeB : pick.NodeA;
                route.Add(node);

                if (graph.Edges.All(e => e.Visited)) break;
            }

            return route;
        }
    }
}

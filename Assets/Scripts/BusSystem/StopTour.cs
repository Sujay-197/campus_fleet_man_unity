using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Builds the fixed-route baseline's loop order. A nearest-neighbor tour keeps the baseline
    /// *competent*: the previous arbitrary StopId order could be a pathologically long zig-zag,
    /// which would let Dynamic win for the wrong reason (see ADR 0002).
    /// </summary>
    public static class StopTour
    {
        /// <summary>Greedy nearest-neighbor tour over the stop nodes, starting at the first one.</summary>
        public static List<int> NearestNeighbor(RoadGraph graph, List<int> stopNodes)
        {
            var tour = new List<int>();
            if (stopNodes == null || stopNodes.Count == 0) return tour;

            var remaining = new List<int>(stopNodes);
            int current = remaining[0];
            tour.Add(current);
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                int bestIdx = 0;
                float bestCost = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float c = GraphRouter.Cost(graph, current, remaining[i]);
                    if (c < bestCost) { bestCost = c; bestIdx = i; }
                }
                current = remaining[bestIdx];
                tour.Add(current);
                remaining.RemoveAt(bestIdx);
            }

            return tour;
        }
    }
}

using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Marks a building (or a standalone marker under a "Stops" root) as a bus stop, bound to
    /// its nearest road-graph node. Populated by RoadGraphBuilder.
    /// </summary>
    public class BusStop : MonoBehaviour
    {
        public int StopId = -1;
        public int NearestNodeIndex = -1;
        [Tooltip("Demand weighting: >1 makes this stop a hub (more origins AND more destinations).")]
        public float Weight = 1f;
    }
}

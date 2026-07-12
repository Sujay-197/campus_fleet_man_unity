using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Marks a building as a bus stop, bound to its nearest road-graph node.
    /// Populated by RoadGraphBuilder. The future fleet agent routes to these.
    /// </summary>
    public class BusStop : MonoBehaviour
    {
        public int StopId = -1;
        public int NearestNodeIndex = -1;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>A junction/endpoint in the road network.</summary>
    [Serializable]
    public class RoadNode
    {
        public int Id;
        public Vector3 Position;
    }

    /// <summary>A drivable segment between two nodes, carrying its center-line polyline.</summary>
    [Serializable]
    public class RoadEdge
    {
        public int Id;
        public int NodeA;
        public int NodeB;
        public List<Vector3> Polyline = new List<Vector3>();

        // Runtime-only exploration flag; not serialized.
        [NonSerialized] public bool Visited;
    }
}

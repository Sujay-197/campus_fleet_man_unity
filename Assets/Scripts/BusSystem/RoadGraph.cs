using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Serialized road network: nodes (junctions/endpoints) and edges (drivable
    /// segments). Built by the editor tool RoadGraphBuilder and consumed at runtime
    /// by BusAgent / IBusController.
    /// </summary>
    public class RoadGraph : MonoBehaviour
    {
        public List<RoadNode> Nodes = new List<RoadNode>();
        public List<RoadEdge> Edges = new List<RoadEdge>();

        /// <summary>Returns the id of an existing node within MergeTolerance of pos, or adds a new one.</summary>
        public int AddOrMergeNode(Vector3 pos)
        {
            float tol = RoadGraphConfig.MergeTolerance;
            float tolSq = tol * tol;
            for (int i = 0; i < Nodes.Count; i++)
                if ((Nodes[i].Position - pos).sqrMagnitude <= tolSq)
                    return Nodes[i].Id;

            var node = new RoadNode { Id = Nodes.Count, Position = pos };
            Nodes.Add(node);
            return node.Id;
        }

        /// <summary>Id of the node closest to worldPos, or -1 if the graph is empty.</summary>
        public int NearestNode(Vector3 worldPos)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Nodes.Count; i++)
            {
                float d = (Nodes[i].Position - worldPos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = Nodes[i].Id; }
            }
            return best;
        }

        /// <summary>All edges incident to the given node.</summary>
        public IEnumerable<RoadEdge> GetNeighborEdges(int nodeId)
        {
            foreach (var e in Edges)
                if (e.NodeA == nodeId || e.NodeB == nodeId)
                    yield return e;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            foreach (var n in Nodes)
                Gizmos.DrawSphere(n.Position, 1.5f);

            Gizmos.color = Color.yellow;
            foreach (var e in Edges)
                for (int i = 0; i + 1 < e.Polyline.Count; i++)
                    Gizmos.DrawLine(e.Polyline[i], e.Polyline[i + 1]);
        }
    }
}

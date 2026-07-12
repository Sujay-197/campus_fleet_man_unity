using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Drives the bus: on start it snaps to the nearest graph node, asks the
    /// controller for a route, expands it to waypoints and feeds the follower;
    /// when a route finishes it requests the next one (endless exploration).
    /// Swap the controller for the fleet agent later without touching this class.
    /// </summary>
    [RequireComponent(typeof(BusPathFollower))]
    public class BusAgent : MonoBehaviour
    {
        public RoadGraph Graph;

        BusPathFollower _follower;
        IBusController _controller;
        int _currentNode;

        void Awake()
        {
            _follower = GetComponent<BusPathFollower>();
            _controller = new ExploreAllController();   // future: swap for agentic brain
        }

        void Start()
        {
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            if (Graph == null || Graph.Nodes.Count == 0)
            {
                Debug.LogError("[BusAgent] No RoadGraph found or graph empty. Build it via 'Bus System/Build Road Graph'.");
                enabled = false;
                return;
            }
            _currentNode = Graph.NearestNode(transform.position);
            _follower.ReachedEndOfRoute += QueueNextRoute;
            QueueNextRoute();
        }

        void OnDestroy()
        {
            if (_follower != null) _follower.ReachedEndOfRoute -= QueueNextRoute;
        }

        void QueueNextRoute()
        {
            var nodeRoute = _controller.NextRoute(Graph, _currentNode);
            if (nodeRoute == null || nodeRoute.Count < 2) return;
            _follower.SetRoute(ExpandToWaypoints(nodeRoute));
            _currentNode = nodeRoute[nodeRoute.Count - 1];
        }

        List<Vector3> ExpandToWaypoints(List<int> nodeRoute)
        {
            var pts = new List<Vector3> { Graph.Nodes[nodeRoute[0]].Position };
            for (int i = 0; i + 1 < nodeRoute.Count; i++)
            {
                var edge = FindEdge(nodeRoute[i], nodeRoute[i + 1]);
                if (edge == null || edge.Polyline == null || edge.Polyline.Count < 2)
                {
                    pts.Add(Graph.Nodes[nodeRoute[i + 1]].Position);
                    continue;
                }
                var poly = new List<Vector3>(edge.Polyline);
                // Orient the polyline so it starts at the current node.
                Vector3 from = Graph.Nodes[nodeRoute[i]].Position;
                if ((poly[0] - from).sqrMagnitude > (poly[poly.Count - 1] - from).sqrMagnitude)
                    poly.Reverse();
                for (int k = 1; k < poly.Count; k++) pts.Add(poly[k]);
            }
            return pts;
        }

        RoadEdge FindEdge(int a, int b)
        {
            foreach (var e in Graph.Edges)
                if ((e.NodeA == a && e.NodeB == b) || (e.NodeA == b && e.NodeB == a)) return e;
            return null;
        }
    }
}

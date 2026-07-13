using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Executes bb.Bus.Plan against the navigator, independent of which agent (dynamic or
    /// fixed-route) populated it. At every arrival it generically alights anyone whose
    /// destination matches the current node and boards anyone waiting there, up to capacity —
    /// so incidental extra passengers can still be served even if a task was planned around
    /// a specific request.
    /// </summary>
    public class Dispatch : IAgent
    {
        readonly IVehicleNavigator _navigator;
        List<int> _currentLegNodes;
        Blackboard _bb;

        public Dispatch(IVehicleNavigator navigator)
        {
            _navigator = navigator;
            _navigator.ReachedGoal += OnReachedGoal;
        }

        public void Tick(Blackboard bb, float dt)
        {
            _bb = bb;

            ProcessArrivalIfAny(bb);

            if (_currentLegNodes == null && bb.Bus.Plan.Count > 0)
                BeginLegTo(bb, bb.Bus.Plan[0].StopNode);

            bb.Metrics.SampleOccupancy(bb.Bus.OnboardRequestIds.Count);
        }

        void ProcessArrivalIfAny(Blackboard bb)
        {
            while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == bb.Bus.CurrentNode
                   && (_currentLegNodes == null || _navigator.Arrived))
            {
                int node = bb.Bus.CurrentNode;

                foreach (var reqId in bb.Bus.OnboardRequestIds.ToList())
                {
                    var r = bb.Requests.First(x => x.Id == reqId);
                    if (r.DestNode != node) continue;
                    r.State = RequestState.Delivered;
                    r.AlightTime = bb.SimTime;
                    bb.Bus.OnboardRequestIds.Remove(reqId);
                    bb.Metrics.RecordDelivery(r);
                }

                foreach (var r in bb.WaitingAt(node).OrderBy(x => x.SpawnTime).ToList())
                {
                    if (bb.Bus.OnboardRequestIds.Count >= bb.Bus.Capacity) break;
                    r.State = RequestState.OnBoard;
                    r.BoardTime = bb.SimTime;
                    bb.Bus.OnboardRequestIds.Add(r.Id);
                }

                while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == node)
                    bb.Bus.Plan.RemoveAt(0);

                _currentLegNodes = null;
            }
        }

        void BeginLegTo(Blackboard bb, int targetNode)
        {
            var route = GraphRouter.FindPath(bb.Graph, bb.Bus.CurrentNode, targetNode);
            if (route == null)
            {
                Debug.LogWarning("[Dispatch] no path from " + bb.Bus.CurrentNode + " to " + targetNode + "; skipping task.");
                bb.Bus.Plan.RemoveAt(0);
                return;
            }
            if (bb.Bus.OnboardRequestIds.Count == 0) bb.Metrics.EmptyTravelDistance += route.Cost;
            _currentLegNodes = route.Nodes;
            _navigator.SetGoalPath(route.Waypoints);
        }

        void OnReachedGoal()
        {
            if (_bb == null || _currentLegNodes == null) return;
            _bb.Bus.CurrentNode = _currentLegNodes[_currentLegNodes.Count - 1];
            _currentLegNodes = null;
        }
    }
}

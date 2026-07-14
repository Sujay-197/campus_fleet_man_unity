using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Executes bb.Bus.Plan, independent of which agent (dynamic or fixed-route) populated it.
    ///
    /// Leg travel is driven by the simulation clock, not render frames: a leg to a target
    /// node completes once SimTime has advanced by (route length / cruise speed). This keeps
    /// the whole simulation deterministic and independent of framerate — the same seed and
    /// parameters always yield identical metrics, and the run can execute headlessly. The
    /// IVehicleNavigator is still handed each leg's waypoints so the physical bus animates
    /// (see KinematicNavigator), but the bus's *logical* position (CurrentNode) advances on
    /// the sim clock rather than waiting for a physical arrival event.
    ///
    /// At every arrival it generically alights anyone whose destination matches the current
    /// node and boards anyone waiting there, up to capacity — so incidental extra passengers
    /// can still be served even if a task was planned around a specific request.
    /// </summary>
    public class Dispatch : IAgent
    {
        readonly IVehicleNavigator _navigator;
        readonly float _cruiseUnitsPerSimSecond;

        bool _traveling;
        int _legEndNode;
        float _legStartTime;
        float _legArriveTime;

        public Dispatch(IVehicleNavigator navigator, float cruiseUnitsPerSimSecond)
        {
            _navigator = navigator;
            _cruiseUnitsPerSimSecond = cruiseUnitsPerSimSecond;
        }

        public void Tick(Blackboard bb, float dt)
        {
            // Complete an in-progress leg once enough sim-time has elapsed.
            if (_traveling && bb.SimTime >= _legArriveTime)
            {
                bb.Bus.CurrentNode = _legEndNode;
                _traveling = false;
            }

            if (!_traveling)
            {
                ProcessArrival(bb);
                if (bb.Bus.Plan.Count > 0)
                    BeginLegTo(bb, bb.Bus.Plan[0].StopNode);
            }

            // Drive the visual bus to exactly match this leg's sim-time progress, so it stays
            // on the road line and never teleports (position is a pure function of sim state).
            float frac = 1f;
            if (_traveling && _legArriveTime > _legStartTime)
                frac = Mathf.Clamp01((bb.SimTime - _legStartTime) / (_legArriveTime - _legStartTime));
            _navigator.UpdateTravel(frac);

            bb.Metrics.SampleOccupancy(bb.Bus.OnboardRequestIds.Count);
        }

        void ProcessArrival(Blackboard bb)
        {
            // Serve every task whose stop coincides with the current node.
            while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == bb.Bus.CurrentNode)
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
                    bb.Activity.Add(ActivityFeed.Kind.Dropped, r.DestStop, -1, bb.SimTime);
                }

                foreach (var r in bb.WaitingAt(node).OrderBy(x => x.SpawnTime).ToList())
                {
                    if (bb.Bus.OnboardRequestIds.Count >= bb.Bus.Capacity) break;
                    r.State = RequestState.OnBoard;
                    r.BoardTime = bb.SimTime;
                    bb.Bus.OnboardRequestIds.Add(r.Id);
                    bb.Activity.Add(ActivityFeed.Kind.PickedUp, r.OriginStop, -1, bb.SimTime);
                }

                while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == node)
                    bb.Bus.Plan.RemoveAt(0);
            }
        }

        void BeginLegTo(Blackboard bb, int targetNode)
        {
            if (targetNode == bb.Bus.CurrentNode) { ProcessArrival(bb); return; }

            var route = GraphRouter.FindPath(bb.Graph, bb.Bus.CurrentNode, targetNode);
            if (route == null)
            {
                Debug.LogWarning("[Dispatch] no path from " + bb.Bus.CurrentNode + " to " + targetNode + "; skipping task.");
                bb.Bus.Plan.RemoveAt(0);
                return;
            }

            if (bb.Bus.OnboardRequestIds.Count == 0) bb.Metrics.EmptyTravelDistance += route.Cost;

            _legEndNode = targetNode;
            _legStartTime = bb.SimTime;
            _legArriveTime = bb.SimTime + (_cruiseUnitsPerSimSecond > 0f ? route.Cost / _cruiseUnitsPerSimSecond : 0f);
            _traveling = true;
            _navigator.SetGoalPath(route.Waypoints); // visual: positioned each tick by UpdateTravel
        }
    }
}

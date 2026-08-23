using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Executes one bus's plan, independent of which agent (fleet optimizer or fixed-route)
    /// populated it. One Dispatch instance is created per bus; all of its leg state is
    /// per-instance, so the buses run independently.
    ///
    /// Leg travel is driven by the simulation clock, not render frames: a leg to a target
    /// node completes once SimTime has advanced by (route length / cruise speed). This keeps
    /// the whole simulation deterministic and independent of framerate — the same seed and
    /// parameters always yield identical metrics, and the run can execute headlessly. The
    /// IVehicleNavigator is positioned each tick at the leg's sim-time fraction so the
    /// physical bus animates along the road line without ever teleporting.
    ///
    /// At every arrival it generically alights anyone whose destination matches the current
    /// node and boards anyone waiting there, up to capacity — so incidental extra passengers
    /// can still be served even if a task was planned around a specific request.
    /// </summary>
    public class Dispatch : IAgent
    {
        readonly IVehicleNavigator _navigator;
        readonly float _cruiseUnitsPerSimSecond;
        readonly int _busId;

        bool _traveling;
        int _legEndNode;
        float _legStartTime;
        float _legArriveTime;

        public Dispatch(IVehicleNavigator navigator, float cruiseUnitsPerSimSecond, int busId)
        {
            _navigator = navigator;
            _cruiseUnitsPerSimSecond = cruiseUnitsPerSimSecond;
            _busId = busId;
        }

        public void Tick(Blackboard bb, float dt)
        {
            var bus = bb.Buses[_busId];

            // Complete an in-progress leg once enough sim-time has elapsed.
            if (_traveling && bb.SimTime >= _legArriveTime)
            {
                bus.CurrentNode = _legEndNode;
                _traveling = false;
            }

            if (!_traveling)
            {
                ProcessArrival(bb, bus);
                if (bus.Plan.Count > 0)
                    BeginLegTo(bb, bus, bus.Plan[0].StopNode);
            }

            // Drive the visual bus to exactly match this leg's sim-time progress, so it stays
            // on the road line and never teleports (position is a pure function of sim state).
            float frac = 1f;
            if (_traveling && _legArriveTime > _legStartTime)
                frac = Mathf.Clamp01((bb.SimTime - _legStartTime) / (_legArriveTime - _legStartTime));
            _navigator.UpdateTravel(frac);

            bb.Metrics.SampleOccupancy(bus.Id, bus.OnboardRequestIds.Count);
        }

        void ProcessArrival(Blackboard bb, BusState bus)
        {
            // Serve every task whose stop coincides with the current node.
            while (bus.Plan.Count > 0 && bus.Plan[0].StopNode == bus.CurrentNode)
            {
                int node = bus.CurrentNode;

                foreach (var reqId in bus.OnboardRequestIds.ToList())
                {
                    var r = bb.Requests.First(x => x.Id == reqId);
                    if (r.DestNode != node) continue;
                    r.State = RequestState.Delivered;
                    r.AlightTime = bb.SimTime;
                    bus.OnboardRequestIds.Remove(reqId);
                    bb.Metrics.RecordDelivery(r, bus.Id);
                    bb.Activity.Add(ActivityFeed.Kind.Dropped, r.DestStop, -1, bb.SimTime);
                }

                foreach (var r in bb.WaitingAt(node).OrderBy(x => x.SpawnTime).ToList())
                {
                    if (bus.OnboardRequestIds.Count >= bus.Capacity) break;
                    r.State = RequestState.OnBoard;
                    r.BoardTime = bb.SimTime;
                    r.AssignedBusId = bus.Id;   // incidental boarders get credited too
                    bus.OnboardRequestIds.Add(r.Id);
                    bb.Activity.Add(ActivityFeed.Kind.PickedUp, r.OriginStop, -1, bb.SimTime);
                }

                while (bus.Plan.Count > 0 && bus.Plan[0].StopNode == node)
                    bus.Plan.RemoveAt(0);
            }
        }

        void BeginLegTo(Blackboard bb, BusState bus, int targetNode)
        {
            if (targetNode == bus.CurrentNode) { ProcessArrival(bb, bus); return; }

            var route = GraphRouter.FindPath(bb.Graph, bus.CurrentNode, targetNode);
            if (route == null)
            {
                Debug.LogWarning("[Dispatch] no path from " + bus.CurrentNode + " to " + targetNode + "; skipping task.");
                bus.Plan.RemoveAt(0);
                return;
            }

            if (bus.OnboardRequestIds.Count == 0) bb.Metrics.AddEmptyTravel(bus.Id, route.Cost);

            _legEndNode = targetNode;
            _legStartTime = bb.SimTime;
            _legArriveTime = bb.SimTime + (_cruiseUnitsPerSimSecond > 0f ? route.Cost / _cruiseUnitsPerSimSecond : 0f);
            _traveling = true;
            _navigator.SetGoalPath(route.Waypoints); // visual: positioned each tick by UpdateTravel
        }
    }
}

using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Status-quo baseline: drives a fixed cyclic loop through all stops regardless of
    /// demand, refilling one lap whenever the plan drains. Dispatch's generic board/alight
    /// still serves whoever matches at each stop as the bus passes.
    /// </summary>
    public class FixedRouteAgent : IAgent
    {
        readonly List<int> _stopNodesInOrder;

        public FixedRouteAgent(List<int> stopNodesInOrder)
        {
            _stopNodesInOrder = stopNodesInOrder;
        }

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Mode != RunMode.FixedRoute) return;
            if (bb.Buses[0].Plan.Count > 0) return;

            foreach (var node in _stopNodesInOrder)
                bb.Buses[0].Plan.Add(new PlanTask { Kind = PlanTaskKind.Visit, RequestId = -1, StopNode = node });
        }
    }
}

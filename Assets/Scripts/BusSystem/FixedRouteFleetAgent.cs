using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Status-quo baseline for a fleet (see ADR 0002): every bus drives the *same* full loop of all
    /// stops regardless of demand, refilling one lap whenever its plan drains. Each lap begins at
    /// the tour position nearest that bus, so buses distributed at startup stay phase-offset around
    /// the loop — the headway model of several shuttles sharing one line.
    /// </summary>
    public class FixedRouteFleetAgent : IAgent
    {
        readonly List<int> _tour;

        public FixedRouteFleetAgent(List<int> tour)
        {
            _tour = tour;
        }

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Mode != RunMode.FixedRoute) return;
            if (_tour == null || _tour.Count == 0) return;

            foreach (var bus in bb.Buses)
            {
                if (bus.Plan.Count > 0) continue;

                int start = NearestTourIndex(bb, bus);
                for (int k = 0; k < _tour.Count; k++)
                {
                    int node = _tour[(start + k) % _tour.Count];
                    bus.Plan.Add(new PlanTask { Kind = PlanTaskKind.Visit, RequestId = -1, StopNode = node });
                }
            }
        }

        int NearestTourIndex(Blackboard bb, BusState bus)
        {
            int best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < _tour.Count; i++)
            {
                float c = GraphRouter.Cost(bb.Graph, bus.CurrentNode, _tour[i]);
                if (c < bestCost) { bestCost = c; best = i; }
            }
            return best;
        }
    }
}

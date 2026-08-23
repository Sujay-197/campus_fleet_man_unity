using System.Linq;

namespace BusSystem
{
    /// <summary>
    /// Centralized fleet assignment (see Docs/adr/0001-centralized-fleet-assignment.md).
    /// Each planning pass, every waiting *unassigned* request is offered to the whole fleet and
    /// committed to the bus whose plan grows least — a greedy, one-shot assignment that is never
    /// revisited.
    ///
    /// Buses are compared on the *marginal* score (score after − score before), not the absolute
    /// plan score: a bus that already has a long plan would otherwise look expensive regardless of
    /// how cheaply it could serve this particular request.
    ///
    /// Determinism: requests are processed in ascending Id and ties break to the lowest bus Id.
    /// Planning is throttled to a cadence (a minute of sim-time is negligible next to leg travel)
    /// and saturated buses are skipped, which keeps a full simulated day cheap to compute.
    /// </summary>
    public class FleetOptimizerAgent : IAgent
    {
        const float ReplanInterval = 60f; // sim-seconds between planning passes
        float _nextReplanTime;

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Mode != RunMode.Dynamic) return;
            if (bb.SimTime < _nextReplanTime) return;
            _nextReplanTime = bb.SimTime + ReplanInterval;

            foreach (var req in bb.Waiting.Where(r => r.AssignedBusId < 0).OrderBy(r => r.Id).ToList())
            {
                int bestBus = -1;
                float bestMarginal = float.MaxValue;
                int bestI = -1, bestJ = -1;

                foreach (var bus in bb.Buses)
                {
                    // Skip a bus already holding a full load's worth of tasks; further
                    // insertions would only be rejected on capacity anyway.
                    if (bus.Plan.Count >= 2 * bus.Capacity) continue;

                    float after; int i, j;
                    if (!InsertionPlanner.Probe(bb.Graph, bus, req, out after, out i, out j)) continue;

                    float before = InsertionPlanner.PlanScore(bb.Graph, bus.CurrentNode, bus.Plan);
                    float marginal = after - before;

                    if (marginal < bestMarginal)
                    {
                        bestMarginal = marginal;
                        bestBus = bus.Id;
                        bestI = i;
                        bestJ = j;
                    }
                }

                if (bestBus < 0) continue; // no feasible bus this pass; retried next pass

                InsertionPlanner.Commit(bb.Buses[bestBus], req, bestI, bestJ);
                req.AssignedBusId = bestBus;
            }
        }
    }
}

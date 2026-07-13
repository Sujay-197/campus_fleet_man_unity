using System.Linq;

namespace BusSystem
{
    /// <summary>
    /// Dynamic-mode routing brain: inserts unplanned waiting requests into the bus plan via
    /// the insertion heuristic (see InsertionPlanner). To keep a full simulated day cheap to
    /// compute, it plans on a throttled cadence rather than every tick, and skips work once
    /// the plan is already saturated with a full load's worth of pickup/dropoff tasks
    /// (further insertions would only be rejected on capacity). Throttling by a minute of
    /// sim-time is negligible next to multi-minute leg travel, so it does not materially
    /// change the metrics.
    /// </summary>
    public class RouteOptimizerAgent : IAgent
    {
        const float ReplanInterval = 60f; // sim-seconds between planning passes
        float _nextReplanTime;

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Mode != RunMode.Dynamic) return;
            if (bb.SimTime < _nextReplanTime) return;
            _nextReplanTime = bb.SimTime + ReplanInterval;

            int planCap = 2 * bb.Bus.Capacity;
            if (bb.Bus.Plan.Count >= planCap) return;

            foreach (var req in bb.Waiting.ToList())
            {
                if (bb.Bus.Plan.Count >= planCap) break;
                bool alreadyPlanned = bb.Bus.Plan.Any(t => t.RequestId == req.Id);
                if (alreadyPlanned) continue;
                InsertionPlanner.TryInsert(bb.Graph, bb.Bus, req);
            }
        }
    }
}

using System.Linq;

namespace BusSystem
{
    /// <summary>Dynamic-mode routing brain: inserts every unplanned waiting request each tick.</summary>
    public class RouteOptimizerAgent : IAgent
    {
        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Mode != RunMode.Dynamic) return;

            foreach (var req in bb.Waiting.ToList())
            {
                bool alreadyPlanned = bb.Bus.Plan.Any(t => t.RequestId == req.Id);
                if (alreadyPlanned) continue;
                InsertionPlanner.TryInsert(bb.Graph, bb.Bus, req);
            }
        }
    }
}

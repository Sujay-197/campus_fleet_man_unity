using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Dial-A-Ride insertion heuristic: for a new request, try inserting its pickup+dropoff
    /// at every valid position pair in a bus's current plan and pick the cheapest one that
    /// respects capacity and pickup-before-dropoff.
    ///
    /// Split into a non-committing <see cref="Probe"/> and a <see cref="Commit"/> so the fleet
    /// optimizer can cost *every* bus before mutating any single one (see FleetOptimizerAgent).
    ///
    /// The selection objective is <see cref="PlanScore"/> — the sum of each task's cumulative
    /// arrival cost — which is passenger-centric: it rewards serving people sooner and heavily
    /// penalises detours that delay everyone downstream. (Minimising raw total distance instead
    /// would encourage long shared detours that inflate individual wait/ride times.)
    /// </summary>
    public static class InsertionPlanner
    {
        /// <summary>
        /// Finds the cheapest feasible insertion without touching the bus. On success, `score` is
        /// the <see cref="PlanScore"/> of the plan that would result, and `atI`/`atJ` are the
        /// positions to hand to <see cref="Commit"/>.
        /// </summary>
        public static bool Probe(RoadGraph graph, BusState bus, PassengerRequest req,
                                 out float score, out int atI, out int atJ)
        {
            var pickup = new PlanTask { Kind = PlanTaskKind.Pickup, RequestId = req.Id, StopNode = req.OriginNode };
            var dropoff = new PlanTask { Kind = PlanTaskKind.Dropoff, RequestId = req.Id, StopNode = req.DestNode };

            float bestScore = float.MaxValue;
            int bestI = -1, bestJ = -1;
            int n = bus.Plan.Count;

            for (int i = 0; i <= n; i++)
            {
                for (int j = i; j <= n; j++)
                {
                    var trial = new List<PlanTask>(bus.Plan);
                    trial.Insert(i, pickup);
                    // dropoff at j+1: since pickup already occupies index i <= j, this always
                    // lands strictly after pickup in the trial list, guaranteeing precedence.
                    trial.Insert(j + 1, dropoff);

                    if (!IsFeasible(bus, trial)) continue;

                    float s = PlanScore(graph, bus.CurrentNode, trial);
                    if (s < bestScore) { bestScore = s; bestI = i; bestJ = j; }
                }
            }

            score = bestScore;
            atI = bestI;
            atJ = bestJ;
            return bestI >= 0;
        }

        /// <summary>Applies an insertion previously found by <see cref="Probe"/>.</summary>
        public static void Commit(BusState bus, PassengerRequest req, int atI, int atJ)
        {
            var pickup = new PlanTask { Kind = PlanTaskKind.Pickup, RequestId = req.Id, StopNode = req.OriginNode };
            var dropoff = new PlanTask { Kind = PlanTaskKind.Dropoff, RequestId = req.Id, StopNode = req.DestNode };
            bus.Plan.Insert(atI, pickup);
            bus.Plan.Insert(atJ + 1, dropoff);
        }

        /// <summary>Probe + Commit against a single bus (the iteration-1 single-bus entry point).</summary>
        public static bool TryInsert(RoadGraph graph, BusState bus, PassengerRequest req)
        {
            float score; int i, j;
            if (!Probe(graph, bus, req, out score, out i, out j)) return false;
            Commit(bus, req, i, j);
            return true;
        }

        static bool IsFeasible(BusState bus, List<PlanTask> trial)
        {
            int occ = bus.OnboardRequestIds.Count;
            foreach (var t in trial)
            {
                occ += t.Kind == PlanTaskKind.Pickup ? 1 : -1;
                if (occ > bus.Capacity || occ < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Passenger-centric objective: the sum over tasks of the cumulative travel cost to
        /// reach each one. Because a delay early in the plan is charged against every later
        /// task, this favours plans that serve passengers promptly over ones that merely
        /// minimise total travelled distance.
        /// </summary>
        public static float PlanScore(RoadGraph graph, int startNode, List<PlanTask> plan)
        {
            float cumulative = 0f, total = 0f;
            int cur = startNode;
            foreach (var t in plan)
            {
                cumulative += GraphRouter.Cost(graph, cur, t.StopNode);
                cur = t.StopNode;
                total += cumulative;
            }
            return total;
        }

        /// <summary>Total travel distance to complete the whole plan in order (operator cost).</summary>
        public static float PlanCost(RoadGraph graph, int startNode, List<PlanTask> plan)
        {
            float total = 0f;
            int cur = startNode;
            foreach (var t in plan)
            {
                total += GraphRouter.Cost(graph, cur, t.StopNode);
                cur = t.StopNode;
            }
            return total;
        }
    }
}

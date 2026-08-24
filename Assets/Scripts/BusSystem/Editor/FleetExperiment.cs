using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusSystem.EditorTools
{
    /// <summary>
    /// Headless A/B + sweep harness. The simulation is plain C# and deterministic, so a full
    /// simulated day runs in well under a second without entering play mode — which is why the
    /// whole sweep is a menu item rather than a play-mode session.
    ///
    /// Two sweeps are run:
    ///   * fleet size (1..MaxBuses) at the default demand rate — the scaling curve, and
    ///   * demand rate at a fixed fleet — a robustness check that Dynamic's advantage is not an
    ///     artifact of one saturated operating point.
    /// </summary>
    public static class FleetExperiment
    {
        const int Seed = 12345;
        const float DurationHours = 16f;
        const float DefaultBaseRate = 6f;
        const int Capacity = 20;
        const float Cruise = 0.25f;
        const int MaxBuses = 4;
        const int RateSweepBuses = 3;

        [MenuItem("Bus System/Run Fleet Sweep")]
        public static void RunSweep()
        {
            var graph = Object.FindFirstObjectByType<RoadGraph>();
            if (graph == null) { Debug.LogError("[FleetSweep] No RoadGraph in the scene."); return; }

            var stops = Object.FindObjectsByType<BusStop>(FindObjectsSortMode.None)
                .OrderBy(s => s.StopId).ToList();
            if (stops.Count < 2) { Debug.LogError("[FleetSweep] Need at least 2 stops."); return; }

            var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();
            var weights = stops.Select(s => s.Weight).ToList();
            var tour = StopTour.NearestNeighbor(graph, stopNodes);

            string dir = Path.Combine(Application.dataPath, "..", "Results");
            Directory.CreateDirectory(dir);

            const string header =
                "Sweep,Mode,BusCount,BaseRate,Requests,Delivered,Undelivered,ServedPct," +
                "AvgWait,P90Wait,AvgRide,AvgTotal,MeanOccupancy,EmptyTravelDistance,DeliveredCoV";
            var lines = new List<string> { header };

            Debug.Log("[FleetSweep] stops=" + stops.Count + " hubs=" +
                      string.Join(",", stops.Where(s => s.Weight > 1f).Select(s => s.name + ":" + s.Weight).ToArray()));

            // --- Sweep 1: fleet size ---
            foreach (RunMode mode in new[] { RunMode.Dynamic, RunMode.FixedRoute })
                for (int n = 1; n <= MaxBuses; n++)
                    lines.Add(Row("FleetSize", mode, n, DefaultBaseRate,
                        RunOne(graph, stopNodes, weights, tour, mode, n, DefaultBaseRate)));

            // --- Sweep 2: demand rate (robustness) ---
            foreach (float rate in new[] { 1f, 2f, 3f, 4.5f, 6f })
                foreach (RunMode mode in new[] { RunMode.Dynamic, RunMode.FixedRoute })
                    lines.Add(Row("DemandRate", mode, RateSweepBuses, rate,
                        RunOne(graph, stopNodes, weights, tour, mode, RateSweepBuses, rate)));

            File.WriteAllLines(Path.Combine(dir, "sweep.csv"), lines);
            Debug.Log("[FleetSweep] Wrote Results/sweep.csv (" + (lines.Count - 1) + " configurations).");
        }

        static string Row(string sweep, RunMode mode, int buses, float rate, RunResult r)
        {
            var s = r.Summary;
            float servedPct = r.Requests == 0 ? 0f : 100f * s.Delivered / r.Requests;
            Debug.Log("[FleetSweep] " + sweep + " " + mode + " x" + buses + " rate=" + rate.ToString("F1") +
                      " delivered=" + s.Delivered + "/" + r.Requests + " (" + servedPct.ToString("F1") + "%)" +
                      " p90=" + s.P90Wait.ToString("F0") + " empty=" + s.EmptyTravelDistance.ToString("F0") +
                      " cov=" + s.DeliveredCoV.ToString("F3"));

            return sweep + "," + mode + "," + buses + "," + rate.ToString("F1") + "," +
                   r.Requests + "," + s.Delivered + "," + s.Undelivered + "," + servedPct.ToString("F1") + "," +
                   s.AvgWait.ToString("F2") + "," + s.P90Wait.ToString("F2") + "," +
                   s.AvgRide.ToString("F2") + "," + s.AvgTotal.ToString("F2") + "," +
                   s.MeanOccupancy.ToString("F2") + "," + s.EmptyTravelDistance.ToString("F2") + "," +
                   s.DeliveredCoV.ToString("F4");
        }

        class RunResult
        {
            public MetricsSummary Summary;
            public int Requests;
        }

        static RunResult RunOne(RoadGraph graph, List<int> stopNodes, List<float> weights,
                                List<int> tour, RunMode mode, int busCount, float baseRate)
        {
            var bb = new Blackboard { Graph = graph, Rng = new System.Random(Seed), Mode = mode };
            bb.Metrics.EnsureBuses(busCount);

            var agents = new List<IAgent>
            {
                new SimClockAgent(DurationHours),
                new DemandAgent(stopNodes, weights, baseRate),
                mode == RunMode.Dynamic ? (IAgent)new FleetOptimizerAgent() : new FixedRouteFleetAgent(tour)
            };

            for (int i = 0; i < busCount; i++)
            {
                int startNode = stopNodes[(i * stopNodes.Count) / busCount];
                bb.Buses.Add(new BusState { Id = i, Capacity = Capacity, CurrentNode = startNode });
                agents.Add(new Dispatch(new HeadlessNavigator(), Cruise, i));
            }

            const float dt = 5f;
            for (int k = 0; k < 500000 && !bb.Finished; k++)
                foreach (var a in agents) a.Tick(bb, dt);

            int undelivered = bb.Requests.Count(r => r.State != RequestState.Delivered);
            return new RunResult { Summary = bb.Metrics.Summarize(undelivered), Requests = bb.Requests.Count };
        }

        /// <summary>No-op actuator: the sweep has no visual bus to drive.</summary>
        class HeadlessNavigator : IVehicleNavigator
        {
            public bool Arrived { get; private set; } = true;
            public event System.Action ReachedGoal;
            public void SetGoalPath(IReadOnlyList<Vector3> waypoints) { Arrived = false; }
            public void UpdateTravel(float f)
            {
                if (f >= 1f && !Arrived) { Arrived = true; if (ReachedGoal != null) ReachedGoal(); }
            }
        }
    }
}

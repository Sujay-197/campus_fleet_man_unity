using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Orchestrates one simulation run: builds the Blackboard, ticks agents in a fixed
    /// order on a fixed sim-timestep (scaled by SimSecondsPerRealSecond), and shows a
    /// live HUD. Mode selects Dynamic (RouteOptimizerAgent) or FixedRoute (FixedRouteAgent)
    /// as the routing brain; Dispatch and everything else is shared between both.
    /// </summary>
    public class Simulation : MonoBehaviour
    {
        public RoadGraph Graph;
        public BusPathFollower Follower;
        public RunMode Mode = RunMode.Dynamic;
        public float SimSecondsPerRealSecond = 600f;
        public float SimDurationHours = 16f;
        public int BusCapacity = 20;
        public float BaseRatePerStopPerHour = 6f;
        // Bus cruise speed in world-units per simulated second. Drives leg travel time
        // (route length / this) deterministically, independent of render framerate.
        public float BusCruiseUnitsPerSimSecond = 0.25f;
        public int RandomSeed = 12345;

        const float FixedStep = 5f; // sim-seconds per logic tick

        Blackboard _bb;
        List<IAgent> _agents;
        float _accumulator;
        string _hudText = "";

        void Start()
        {
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            if (Follower == null) Follower = FindObjectOfType<BusPathFollower>();
            var stops = FindObjectsByType<BusStop>(FindObjectsSortMode.None)
                .OrderBy(s => s.StopId).ToList();
            var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();
            var stopNames = stops.Select(s => s.name).ToList(); // building names (AB1..AB4)

            _bb = new Blackboard
            {
                Graph = Graph,
                Rng = new System.Random(RandomSeed),
                Mode = Mode,
                StopNames = stopNames,
                Bus = new BusState { Capacity = BusCapacity, CurrentNode = Graph.NearestNode(Follower.transform.position) }
            };

            var navigator = new KinematicNavigator(Follower);
            string resultsDir = System.IO.Path.Combine(Application.dataPath, "..", "Results");

            _agents = new List<IAgent>
            {
                new SimClockAgent(SimDurationHours),
                new DemandAgent(stopNodes, BaseRatePerStopPerHour),
                Mode == RunMode.Dynamic
                    ? (IAgent)new RouteOptimizerAgent()
                    : new FixedRouteAgent(stopNodes),
                new Dispatch(navigator, BusCruiseUnitsPerSimSecond),
                new MonitorAgent(resultsDir)
            };
        }

        void Update()
        {
            if (_bb == null || _bb.Finished) return;

            _accumulator += Time.deltaTime * SimSecondsPerRealSecond;
            while (_accumulator >= FixedStep)
            {
                foreach (var agent in _agents) agent.Tick(_bb, FixedStep);
                _accumulator -= FixedStep;
                if (_bb.Finished) break;
            }

            _hudText = MonitorAgent.FormatHud(_bb);
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(_hudText)) return;
            GUI.Box(new Rect(10, 10, 220, 110), "");
            GUI.Label(new Rect(20, 15, 210, 100), _hudText);

            if (_bb == null) return;
            var feed = _bb.Activity.Current(_bb.SimTime);
            if (feed.Count == 0) return;

            const float lineH = 18f;
            float h = 22f + feed.Count * lineH;
            GUI.Box(new Rect(10, 130, 260, h), "");
            GUI.Label(new Rect(20, 133, 245, lineH), "Activity");
            // Newest first.
            for (int i = 0; i < feed.Count; i++)
            {
                var e = feed[feed.Count - 1 - i];
                GUI.Label(new Rect(20, 133 + (i + 1) * lineH, 245, lineH),
                    ActivityFeed.Format(e, _bb.StopName));
            }
        }
    }
}

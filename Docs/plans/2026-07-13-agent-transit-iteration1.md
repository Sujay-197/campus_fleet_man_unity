# Agent-Based Campus Transit — Iteration 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One bus dynamically serves passengers appearing at stops (seeded Poisson demand with rush-hour peaks), using a capacity-aware insertion-heuristic router over the road graph, and its performance is measured against a fixed-route baseline under identical demand.

**Architecture:** Plain-C# agents (`IAgent.Tick(Blackboard, dt)`) — `SimClockAgent`, `DemandAgent`, `RouteOptimizerAgent` (Dynamic) or `FixedRouteAgent` (baseline), `Dispatch`, `MonitorAgent` — driven in fixed order by one `Simulation` MonoBehaviour on a fixed sim-timestep. Routing uses Dijkstra (`GraphRouter`) over `RoadGraph` edges now carrying travel-time weights. Movement goes through the `IVehicleNavigator` seam (`KinematicNavigator` wraps the existing `BusPathFollower`) so a future sensor-based navigator can replace it with zero change upstream.

**Tech Stack:** Unity 6000.5.3f1, Built-in Render Pipeline, C#, driven via the `unity-mcp` MCP server. Builds on `Assets/Scripts/BusSystem/` from the road-exploration iteration (`RoadGraph`, `BusStop`, `BusPathFollower`).

## Global Constraints

- Unity version: **6000.5.3f1**, render pipeline **Built-in** (no URP/HDRP shaders).
- Namespace: **`BusSystem`**. All new files go in `Assets/Scripts/BusSystem/` (flat, no subfolders), **no asmdef** — code must compile into the default `Assembly-CSharp` assembly, because verification is done via `mcp__unity-mcp__Unity_RunCommand`, whose sandbox only resolves types from that default assembly (confirmed in the prior iteration: a `BusSystem.asmdef` made `RunCommand` scripts fail with `CS0246`).
- **No NUnit / Unity Test Runner.** It cannot be driven headlessly through this MCP setup. All verification in this plan uses the **red/green `RunCommand` pattern**: run a verification script referencing not-yet-existing types (expect `COMPILATION_FAILED` = RED), write the implementation, recompile, rerun the *same* script (expect `PASS` logs = GREEN).
- **Recompile procedure** after writing/editing any `.cs` file: call `mcp__unity-mcp__Unity_RunCommand` with a script that runs `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate)`. Editing a file under `Assets/Scripts/BusSystem/Editor/` triggers a domain reload that briefly drops the MCP bridge ("Unity not detected") — retry `mcp__unity-mcp__Unity_ManageEditor GetState` until it reconnects, then proceed.
- **`RunCommand`'s `result.Log` only substitutes `{0}`-style placeholders with no format specifiers** (`{0:F2}` does not work). Pre-format numbers with `.ToString("F2")` etc. and string-concatenate before logging.
- **Never call `Object.GetInstanceID()` or `EditorUtility.InstanceIDToObject`** in a `RunCommand` script — both are obsolete-as-error on this Unity version and will fail compilation.
- `Application.runInBackground` is already enabled project-wide, so Play-mode simulation keeps ticking while the Unity window is unfocused.
- Results/CSV output goes to a **`Results/`** folder at the **project root** (sibling of `Assets/`, like `Docs/`) — not inside `Assets/`, so Unity doesn't try to import CSVs. Build the path as `Path.Combine(Application.dataPath, "..", "Results")`.
- Existing scene objects to reuse: `RoadGraph` GameObject (has the built graph), `Buildings` (4 children with `BusStop`), `Vehicles/school-bus` (has `BusPathFollower`, plus a `BusAgent` component from iteration 0 that this plan supersedes — Task 10 removes it to avoid two things calling `BusPathFollower.SetRoute` at once).
- Git: commit after each task using `git -c user.name="Sujay-197" -c user.email="sujaysenthil01097@gmail.com" commit -m "..."`; push each commit (`git push origin main`) since the user reviews progress on GitHub.

---

### Task 1: Core data model — Blackboard, requests, plan tasks, metrics

**Files:**
- Create: `Assets/Scripts/BusSystem/IAgent.cs`
- Create: `Assets/Scripts/BusSystem/PassengerRequest.cs`
- Create: `Assets/Scripts/BusSystem/PlanTask.cs`
- Create: `Assets/Scripts/BusSystem/BusState.cs`
- Create: `Assets/Scripts/BusSystem/Metrics.cs`
- Create: `Assets/Scripts/BusSystem/Blackboard.cs`

**Interfaces:**
- Produces:
  - `interface IAgent { void Tick(Blackboard bb, float dt); }`
  - `enum RequestState { Waiting, OnBoard, Delivered }`
  - `class PassengerRequest { int Id; int OriginStop; int OriginNode; int DestStop; int DestNode; float SpawnTime; float BoardTime=-1; float AlightTime=-1; RequestState State=Waiting; }`
  - `enum PlanTaskKind { Pickup, Dropoff, Visit }`
  - `class PlanTask { PlanTaskKind Kind; int RequestId; int StopNode; }`
  - `class BusState { int CurrentNode; int Capacity; List<int> OnboardRequestIds; List<PlanTask> Plan; }`
  - `class MetricsSummary { int Delivered; int Undelivered; float AvgWait; float P90Wait; float AvgRide; float AvgTotal; float MeanOccupancy; float EmptyTravelDistance; }`
  - `class Metrics { void RecordDelivery(PassengerRequest r); void SampleOccupancy(int count); float EmptyTravelDistance; MetricsSummary Summarize(int undelivered); }`
  - `enum RunMode { Dynamic, FixedRoute }`
  - `class Blackboard { float SimTime; System.Random Rng; RoadGraph Graph; List<PassengerRequest> Requests; BusState Bus; Metrics Metrics; RunMode Mode; bool Finished; int NextRequestId(); IEnumerable<PassengerRequest> Waiting; IEnumerable<PassengerRequest> WaitingAt(int stopNode); }`

- [ ] **Step 1: Run the verification script (expect it to fail to compile — RED)**

Run via `mcp__unity-mcp__Unity_RunCommand` with title "Verify core types (red)":
```csharp
using UnityEngine;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        var bb = new Blackboard { Rng = new System.Random(1), Bus = new BusState { Capacity = 4 } };
        var r1 = new PassengerRequest { Id = bb.NextRequestId(), OriginStop = 0, OriginNode = 0, DestStop = 1, DestNode = 5, SpawnTime = 0f, State = RequestState.Waiting };
        bb.Requests.Add(r1);
        Check(bb.Waiting.Count() == 1, "one waiting request");
        Check(bb.WaitingAt(0).Count() == 1, "WaitingAt matches origin node");
        Check(bb.WaitingAt(1).Count() == 0, "WaitingAt excludes non-origin");

        r1.State = RequestState.OnBoard; r1.BoardTime = 10f;
        r1.State = RequestState.Delivered; r1.AlightTime = 25f;
        var m = new Metrics();
        m.RecordDelivery(r1);
        m.SampleOccupancy(1);
        m.EmptyTravelDistance = 12.5f;
        var summary = m.Summarize(0);
        Check(summary.Delivered == 1, "summary delivered count");
        Check(Mathf.Approximately(summary.AvgWait, 10f), "avg wait == 10");
        Check(Mathf.Approximately(summary.AvgRide, 15f), "avg ride == 15");
        Check(Mathf.Approximately(summary.AvgTotal, 25f), "avg total == 25");
        Check(Mathf.Approximately(summary.MeanOccupancy, 1f), "mean occupancy == 1");
        Check(Mathf.Approximately(summary.EmptyTravelDistance, 12.5f), "empty travel distance preserved");

        var task = new PlanTask { Kind = PlanTaskKind.Pickup, RequestId = r1.Id, StopNode = 0 };
        bb.Bus.Plan.Add(task);
        Check(bb.Bus.Plan.Count == 1, "plan has one task");
        Check(bb.NextRequestId() == 1, "request ids increment");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` with `CS0246`-style errors for `Blackboard`, `BusState`, `PassengerRequest`, etc.

- [ ] **Step 2: Write `IAgent.cs`**

```csharp
namespace BusSystem
{
    /// <summary>An autonomous agent: perceive (read Blackboard) → decide → act (write Blackboard).</summary>
    public interface IAgent
    {
        void Tick(Blackboard bb, float dt);
    }
}
```

- [ ] **Step 3: Write `PassengerRequest.cs`**

```csharp
namespace BusSystem
{
    public enum RequestState { Waiting, OnBoard, Delivered }

    /// <summary>A single passenger trip request from an origin stop to a destination stop.</summary>
    public class PassengerRequest
    {
        public int Id;
        public int OriginStop;
        public int OriginNode;
        public int DestStop;
        public int DestNode;
        public float SpawnTime;
        public float BoardTime = -1f;
        public float AlightTime = -1f;
        public RequestState State = RequestState.Waiting;
    }
}
```

- [ ] **Step 4: Write `PlanTask.cs`**

```csharp
namespace BusSystem
{
    // Kind is planning-time bookkeeping only (used by InsertionPlanner's cost/feasibility
    // reasoning and for debugging); Dispatch executes generically off StopNode alone —
    // at every stop it alights anyone whose destination matches and boards anyone waiting
    // there, regardless of which specific request a task was inserted for. Visit is used
    // by the fixed-route baseline, which has no specific request bound to a stop.
    public enum PlanTaskKind { Pickup, Dropoff, Visit }

    public class PlanTask
    {
        public PlanTaskKind Kind;
        public int RequestId;
        public int StopNode;
    }
}
```

- [ ] **Step 5: Write `BusState.cs`**

```csharp
using System.Collections.Generic;

namespace BusSystem
{
    public class BusState
    {
        public int CurrentNode;
        public int Capacity;
        public List<int> OnboardRequestIds = new List<int>();
        public List<PlanTask> Plan = new List<PlanTask>();
    }
}
```

- [ ] **Step 6: Write `Metrics.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    public class MetricsSummary
    {
        public int Delivered;
        public int Undelivered;
        public float AvgWait;
        public float P90Wait;
        public float AvgRide;
        public float AvgTotal;
        public float MeanOccupancy;
        public float EmptyTravelDistance;
    }

    /// <summary>Accumulates per-delivery timings and occupancy samples for one simulation run.</summary>
    public class Metrics
    {
        readonly List<float> _waits = new List<float>();
        readonly List<float> _rides = new List<float>();
        readonly List<float> _totals = new List<float>();
        readonly List<int> _occupancySamples = new List<int>();

        public float EmptyTravelDistance;

        public void RecordDelivery(PassengerRequest r)
        {
            _waits.Add(r.BoardTime - r.SpawnTime);
            _rides.Add(r.AlightTime - r.BoardTime);
            _totals.Add(r.AlightTime - r.SpawnTime);
        }

        public void SampleOccupancy(int count) => _occupancySamples.Add(count);

        public MetricsSummary Summarize(int undelivered)
        {
            return new MetricsSummary
            {
                Delivered = _waits.Count,
                Undelivered = undelivered,
                AvgWait = Average(_waits),
                P90Wait = Percentile(_waits, 0.9f),
                AvgRide = Average(_rides),
                AvgTotal = Average(_totals),
                MeanOccupancy = _occupancySamples.Count == 0 ? 0f : (float)_occupancySamples.Average(),
                EmptyTravelDistance = EmptyTravelDistance
            };
        }

        static float Average(List<float> xs) => xs.Count == 0 ? 0f : xs.Sum() / xs.Count;

        static float Percentile(List<float> xs, float p)
        {
            if (xs.Count == 0) return 0f;
            var sorted = xs.OrderBy(x => x).ToList();
            int idx = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
```

- [ ] **Step 7: Write `Blackboard.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace BusSystem
{
    public enum RunMode { Dynamic, FixedRoute }

    /// <summary>Shared world state every agent perceives from and acts upon.</summary>
    public class Blackboard
    {
        public float SimTime;
        public System.Random Rng;
        public RoadGraph Graph;
        public List<PassengerRequest> Requests = new List<PassengerRequest>();
        public BusState Bus = new BusState();
        public Metrics Metrics = new Metrics();
        public RunMode Mode;
        public bool Finished;

        int _nextRequestId;
        public int NextRequestId() => _nextRequestId++;

        public IEnumerable<PassengerRequest> Waiting => Requests.Where(r => r.State == RequestState.Waiting);
        public IEnumerable<PassengerRequest> WaitingAt(int stopNode) => Waiting.Where(r => r.OriginNode == stopNode);
    }
}
```

- [ ] **Step 8: Recompile**

Run via `Unity_RunCommand` (title "Refresh"):
```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        result.Log("refresh");
    }
}
```
If the bridge reports "Unity not detected", call `Unity_ManageEditor GetState` repeatedly until it reconnects.

- [ ] **Step 9: Rerun the Step 1 script — expect GREEN**

Expected log tail: `RESULT: 8 passed, 0 failed`.

- [ ] **Step 10: Commit**
```bash
git add Assets/Scripts/BusSystem/IAgent.cs Assets/Scripts/BusSystem/PassengerRequest.cs Assets/Scripts/BusSystem/PlanTask.cs Assets/Scripts/BusSystem/BusState.cs Assets/Scripts/BusSystem/Metrics.cs Assets/Scripts/BusSystem/Blackboard.cs
git commit -m "feat(sim): core data model - Blackboard, requests, plan tasks, metrics"
git push origin main
```

---

### Task 2: Road-graph edge weights + GraphRouter (Dijkstra)

**Files:**
- Modify: `Assets/Scripts/BusSystem/RoadGraphData.cs` (add `Length` to `RoadEdge`)
- Modify: `Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs:161-165` (compute `Length` in `AddEdge`)
- Create: `Assets/Scripts/BusSystem/GraphRouter.cs`

**Interfaces:**
- Consumes: `RoadGraph.Nodes`, `RoadGraph.Edges`, `RoadGraph.GetNeighborEdges(int)` (from iteration 0).
- Produces:
  - `RoadEdge.Length` (float, world-units).
  - `class GraphRouter.RouteResult { List<int> Nodes; float Cost; List<Vector3> Waypoints; }`
  - `static GraphRouter.RouteResult FindPath(RoadGraph graph, int startNode, int endNode)` — null if unreachable.
  - `static float GraphRouter.Cost(RoadGraph graph, int startNode, int endNode)` — `float.MaxValue` if unreachable.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify GraphRouter (red)"):
```csharp
using UnityEngine;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        // Diamond graph: 0-1 (10), 0-2 (1), 1-3 (1), 2-3 (10). Shortest 0->3 should go via 2? No:
        // 0->1->3 = 11, 0->2->3 = 11 too (tie) -- use asymmetric weights to force a unique answer:
        // 0-1 (1), 1-3 (1), 0-2 (1), 2-3 (5)  => cheapest 0->3 is via 1, cost 2.
        var g = new GameObject("g").AddComponent<RoadGraph>();
        for (int i = 0; i < 4; i++) g.Nodes.Add(new RoadNode { Id = i, Position = new Vector3(i, 0, 0) });
        void E(int a, int b, float len)
        {
            g.Edges.Add(new RoadEdge { Id = g.Edges.Count, NodeA = a, NodeB = b, Length = len,
                Polyline = new System.Collections.Generic.List<Vector3> { g.Nodes[a].Position, g.Nodes[b].Position } });
        }
        E(0, 1, 1f); E(1, 3, 1f); E(0, 2, 1f); E(2, 3, 5f);

        var route = GraphRouter.FindPath(g, 0, 3);
        Check(route != null, "path found");
        Check(Mathf.Approximately(route.Cost, 2f), "cheapest cost is 2 (via node 1)");
        Check(route.Nodes.Count == 3 && route.Nodes[0] == 0 && route.Nodes[1] == 1 && route.Nodes[2] == 3, "path is 0-1-3");
        Check(route.Waypoints.Count >= 3, "waypoints expanded from polylines");

        Check(GraphRouter.FindPath(g, 0, 0).Cost == 0f, "same-node path has zero cost");

        var g2 = new GameObject("g2").AddComponent<RoadGraph>();
        g2.Nodes.Add(new RoadNode { Id = 0, Position = Vector3.zero });
        g2.Nodes.Add(new RoadNode { Id = 1, Position = Vector3.right * 5 });
        Check(GraphRouter.FindPath(g2, 0, 1) == null, "no path between disconnected nodes");
        Check(GraphRouter.Cost(g2, 0, 1) == float.MaxValue, "Cost is MaxValue when unreachable");

        Object.DestroyImmediate(g.gameObject);
        Object.DestroyImmediate(g2.gameObject);
        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `RoadEdge` has no `Length` (or `GraphRouter` missing, once `Length` is added first this will fail on `GraphRouter`). Confirm it fails for a `GraphRouter`-related reason after adding `Length` in the next step, or just confirm it fails now (either missing member is acceptable RED evidence).

- [ ] **Step 2: Add `Length` to `RoadEdge` in `RoadGraphData.cs`**

Find the `RoadEdge` class and add the field:
```csharp
    [Serializable]
    public class RoadEdge
    {
        public int Id;
        public int NodeA;
        public int NodeB;
        public List<Vector3> Polyline = new List<Vector3>();
        public float Length;

        [NonSerialized] public bool Visited;
    }
```

- [ ] **Step 3: Compute `Length` in `RoadGraphBuilder.AddEdge`**

Replace the existing helper at `Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs:161-165`:
```csharp
        static void AddEdge(RoadGraph g, ref int id, int a, int b, List<Vector3> poly)
        {
            if (a == b) return;
            float length = 0f;
            for (int i = 0; i + 1 < poly.Count; i++) length += Vector3.Distance(poly[i], poly[i + 1]);
            g.Edges.Add(new RoadEdge { Id = id++, NodeA = a, NodeB = b, Polyline = poly, Length = length });
        }
```

- [ ] **Step 4: Write `GraphRouter.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Dijkstra shortest path over a RoadGraph's weighted edges.</summary>
    public static class GraphRouter
    {
        public class RouteResult
        {
            public List<int> Nodes;
            public float Cost;
            public List<Vector3> Waypoints;
        }

        public static RouteResult FindPath(RoadGraph graph, int startNode, int endNode)
        {
            if (startNode == endNode)
                return new RouteResult
                {
                    Nodes = new List<int> { startNode },
                    Cost = 0f,
                    Waypoints = new List<Vector3> { graph.Nodes[startNode].Position }
                };

            var dist = new Dictionary<int, float>();
            var prevNode = new Dictionary<int, int>();
            var prevEdge = new Dictionary<int, RoadEdge>();
            var visited = new HashSet<int>();
            var frontier = new List<int>();

            foreach (var n in graph.Nodes) dist[n.Id] = float.MaxValue;
            dist[startNode] = 0f;
            frontier.Add(startNode);

            while (frontier.Count > 0)
            {
                frontier.Sort((a, b) => dist[a].CompareTo(dist[b]));
                int u = frontier[0];
                frontier.RemoveAt(0);
                if (visited.Contains(u)) continue;
                visited.Add(u);
                if (u == endNode) break;

                foreach (var e in graph.GetNeighborEdges(u))
                {
                    int v = e.NodeA == u ? e.NodeB : e.NodeA;
                    if (visited.Contains(v)) continue;
                    float alt = dist[u] + e.Length;
                    if (alt < dist[v])
                    {
                        dist[v] = alt;
                        prevNode[v] = u;
                        prevEdge[v] = e;
                        frontier.Add(v);
                    }
                }
            }

            if (!dist.ContainsKey(endNode) || dist[endNode] >= float.MaxValue) return null;

            var nodePath = new List<int>();
            var edgePath = new List<RoadEdge>();
            int cur = endNode;
            nodePath.Add(cur);
            while (cur != startNode)
            {
                edgePath.Add(prevEdge[cur]);
                cur = prevNode[cur];
                nodePath.Add(cur);
            }
            nodePath.Reverse();
            edgePath.Reverse();

            var waypoints = new List<Vector3> { graph.Nodes[startNode].Position };
            int fromNode = startNode;
            foreach (var e in edgePath)
            {
                var poly = new List<Vector3>(e.Polyline);
                Vector3 from = graph.Nodes[fromNode].Position;
                if ((poly[0] - from).sqrMagnitude > (poly[poly.Count - 1] - from).sqrMagnitude)
                    poly.Reverse();
                for (int k = 1; k < poly.Count; k++) waypoints.Add(poly[k]);
                fromNode = e.NodeA == fromNode ? e.NodeB : e.NodeA;
            }

            return new RouteResult { Nodes = nodePath, Cost = dist[endNode], Waypoints = waypoints };
        }

        public static float Cost(RoadGraph graph, int startNode, int endNode)
        {
            var r = FindPath(graph, startNode, endNode);
            return r?.Cost ?? float.MaxValue;
        }
    }
}
```

- [ ] **Step 5: Recompile** (same refresh script as Task 1 Step 8)

- [ ] **Step 6: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 7 passed, 0 failed`.

- [ ] **Step 7: Rebuild the live road graph and confirm edges now carry real lengths**

Run via `Unity_ManageMenuItem` (`Execute`, `Bus System/Build Road Graph`, `Refresh: true`), then via `Unity_RunCommand` (title "Check edge lengths"):
```csharp
using UnityEngine;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var g = Object.FindObjectOfType<RoadGraph>();
        bool allPositive = g.Edges.All(e => e.Length > 0.1f);
        float avg = g.Edges.Average(e => e.Length);
        result.Log("edges=" + g.Edges.Count + " allPositive=" + allPositive + " avgLength=" + avg.ToString("F1"));
    }
}
```
Expected: `allPositive=True`, `avgLength` roughly in the 15–40 range (matches the ~28-unit tile scale).

- [ ] **Step 8: Save the scene and commit**

Run `Unity_ManageScene` `Action: Save`, then:
```bash
git add Assets/Scripts/BusSystem/RoadGraphData.cs Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs Assets/Scripts/BusSystem/GraphRouter.cs Assets/Scenes/SampleScene.unity
git commit -m "feat(sim): road-graph edge weights + GraphRouter (Dijkstra)"
git push origin main
```

---

### Task 3: SimClockAgent + PeakProfile + DemandAgent

**Files:**
- Create: `Assets/Scripts/BusSystem/SimClockAgent.cs`
- Create: `Assets/Scripts/BusSystem/PeakProfile.cs`
- Create: `Assets/Scripts/BusSystem/DemandAgent.cs`

**Interfaces:**
- Consumes: `IAgent`, `Blackboard` (Task 1).
- Produces:
  - `class SimClockAgent : IAgent` — ctor `(float durationHours)`; each tick advances `bb.SimTime` by `dt` and sets `bb.Finished = true` once `bb.SimTime >= durationHours*3600`.
  - `static class PeakProfile { static float Multiplier(float timeOfDayHours); }`
  - `class DemandAgent : IAgent` — ctor `(List<int> stopNodes, float baseRatePerStopPerHour)`.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify SimClock+Demand (red)"):
```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        // SimClockAgent
        var bb = new Blackboard { Rng = new System.Random(1) };
        var clock = new SimClockAgent(1f); // 1 hour = 3600s
        clock.Tick(bb, 100f);
        Check(Mathf.Approximately(bb.SimTime, 100f), "SimTime advances by dt");
        Check(!bb.Finished, "not finished before duration");
        clock.Tick(bb, 4000f);
        Check(bb.Finished, "finished after duration reached");

        // PeakProfile: peak (8h) should be much higher than off-peak (3h)
        float peak = PeakProfile.Multiplier(8f);
        float offpeak = PeakProfile.Multiplier(3f);
        Check(peak > offpeak * 3f, "peak multiplier much greater than off-peak");

        // DemandAgent determinism: same seed -> same request count over N ticks
        var stops = new List<int> { 10, 20, 30 };
        var bb1 = new Blackboard { Rng = new System.Random(42) };
        var bb2 = new Blackboard { Rng = new System.Random(42) };
        var d1 = new DemandAgent(stops, 20f);
        var d2 = new DemandAgent(stops, 20f);
        for (int i = 0; i < 200; i++) { bb1.SimTime += 30f; d1.Tick(bb1, 30f); }
        for (int i = 0; i < 200; i++) { bb2.SimTime += 30f; d2.Tick(bb2, 30f); }
        Check(bb1.Requests.Count == bb2.Requests.Count, "same seed -> same total spawn count");
        Check(bb1.Requests.Count > 0, "at least some requests spawned");
        bool allValid = bb1.Requests.All(r => stops.Contains(r.OriginNode) && stops.Contains(r.DestNode) && r.OriginNode != r.DestNode);
        Check(allValid, "requests have valid distinct origin/destination nodes");

        // Peak-hour tick should spawn (statistically) more than an off-peak tick at high rate
        var bbPeak = new Blackboard { Rng = new System.Random(7), SimTime = 8f * 3600f };
        var bbOff = new Blackboard { Rng = new System.Random(7), SimTime = 3f * 3600f };
        var dPeak = new DemandAgent(stops, 60f);
        var dOff = new DemandAgent(stops, 60f);
        int peakSpawns = 0, offSpawns = 0;
        for (int i = 0; i < 500; i++) { dPeak.Tick(bbPeak, 5f); bbPeak.SimTime += 5f; }
        for (int i = 0; i < 500; i++) { dOff.Tick(bbOff, 5f); bbOff.SimTime += 5f; }
        peakSpawns = bbPeak.Requests.Count; offSpawns = bbOff.Requests.Count;
        Check(peakSpawns > offSpawns, "more spawns accumulate during peak window than off-peak (peak=" + peakSpawns + " off=" + offSpawns + ")");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `SimClockAgent`, `PeakProfile`, `DemandAgent` missing.

- [ ] **Step 2: Write `SimClockAgent.cs`**

```csharp
namespace BusSystem
{
    /// <summary>Advances the simulation clock and flags the run finished after a fixed duration.</summary>
    public class SimClockAgent : IAgent
    {
        readonly float _durationSeconds;

        public SimClockAgent(float durationHours)
        {
            _durationSeconds = durationHours * 3600f;
        }

        public void Tick(Blackboard bb, float dt)
        {
            bb.SimTime += dt;
            if (bb.SimTime >= _durationSeconds) bb.Finished = true;
        }
    }
}
```

- [ ] **Step 3: Write `PeakProfile.cs`**

```csharp
using UnityEngine;

namespace BusSystem
{
    /// <summary>Demand-rate multiplier over a 24h day: a base level plus two rush-hour bumps.</summary>
    public static class PeakProfile
    {
        public static float Multiplier(float timeOfDayHours)
        {
            float morning = Gaussian(timeOfDayHours, 8f, 1.2f);
            float evening = Gaussian(timeOfDayHours, 17f, 1.2f);
            return 0.3f + 2.5f * (morning + evening);
        }

        static float Gaussian(float x, float mean, float sigma)
        {
            float d = (x - mean) / sigma;
            return Mathf.Exp(-0.5f * d * d);
        }
    }
}
```

- [ ] **Step 4: Write `DemandAgent.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Spawns passenger requests at stops via a Poisson process shaped by PeakProfile.</summary>
    public class DemandAgent : IAgent
    {
        readonly List<int> _stopNodes;
        readonly float _baseRatePerStopPerHour;

        public DemandAgent(List<int> stopNodes, float baseRatePerStopPerHour)
        {
            _stopNodes = stopNodes;
            _baseRatePerStopPerHour = baseRatePerStopPerHour;
        }

        public void Tick(Blackboard bb, float dt)
        {
            float timeOfDay = (bb.SimTime / 3600f) % 24f;
            float mult = PeakProfile.Multiplier(timeOfDay);
            float lambda = _baseRatePerStopPerHour * mult * (dt / 3600f);

            for (int i = 0; i < _stopNodes.Count; i++)
            {
                int count = SamplePoisson(bb.Rng, lambda);
                for (int k = 0; k < count; k++) SpawnRequest(bb, i);
            }
        }

        void SpawnRequest(Blackboard bb, int originIdx)
        {
            int destIdx = originIdx;
            while (destIdx == originIdx) destIdx = bb.Rng.Next(_stopNodes.Count);

            bb.Requests.Add(new PassengerRequest
            {
                Id = bb.NextRequestId(),
                OriginStop = originIdx,
                OriginNode = _stopNodes[originIdx],
                DestStop = destIdx,
                DestNode = _stopNodes[destIdx],
                SpawnTime = bb.SimTime,
                State = RequestState.Waiting
            });
        }

        // Knuth's algorithm; fine for the small lambda values used per tick here.
        static int SamplePoisson(System.Random rng, float lambda)
        {
            if (lambda <= 0f) return 0;
            float L = Mathf.Exp(-lambda);
            int k = 0;
            float p = 1f;
            do { k++; p *= (float)rng.NextDouble(); } while (p > L);
            return k - 1;
        }
    }
}
```

- [ ] **Step 5: Recompile**

- [ ] **Step 6: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 8 passed, 0 failed`.

- [ ] **Step 7: Commit**
```bash
git add Assets/Scripts/BusSystem/SimClockAgent.cs Assets/Scripts/BusSystem/PeakProfile.cs Assets/Scripts/BusSystem/DemandAgent.cs
git commit -m "feat(sim): SimClockAgent + PeakProfile + seeded DemandAgent"
git push origin main
```

---

### Task 4: InsertionPlanner + RouteOptimizerAgent

**Files:**
- Create: `Assets/Scripts/BusSystem/InsertionPlanner.cs`
- Create: `Assets/Scripts/BusSystem/RouteOptimizerAgent.cs`

**Interfaces:**
- Consumes: `GraphRouter.Cost` (Task 2), `RoadGraph`, `BusState`, `PassengerRequest`, `PlanTask`, `IAgent`, `Blackboard` (Task 1).
- Produces:
  - `static class InsertionPlanner { static bool TryInsert(RoadGraph graph, BusState bus, PassengerRequest req); static float PlanCost(RoadGraph graph, int startNode, List<PlanTask> plan); }`
  - `class RouteOptimizerAgent : IAgent` — no-op unless `bb.Mode == RunMode.Dynamic`; inserts every not-yet-planned waiting request each tick.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify InsertionPlanner (red)"):
```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        // Line graph: nodes 0..4 at x=0,10,20,30,40, one straight edge each (length 10).
        var g = new GameObject("g").AddComponent<RoadGraph>();
        for (int i = 0; i < 5; i++) g.Nodes.Add(new RoadNode { Id = i, Position = new Vector3(i * 10, 0, 0) });
        for (int i = 0; i < 4; i++)
            g.Edges.Add(new RoadEdge { Id = i, NodeA = i, NodeB = i + 1, Length = 10f,
                Polyline = new List<Vector3> { g.Nodes[i].Position, g.Nodes[i + 1].Position } });

        // Bus at node 0, empty plan, capacity 2. Insert a request 1->3.
        var bus = new BusState { CurrentNode = 0, Capacity = 2 };
        var req = new PassengerRequest { Id = 0, OriginNode = 1, DestNode = 3 };
        bool inserted = InsertionPlanner.TryInsert(g, bus, req);
        Check(inserted, "first insertion succeeds");
        Check(bus.Plan.Count == 2, "plan has pickup+dropoff");
        Check(bus.Plan[0].StopNode == 1 && bus.Plan[0].Kind == PlanTaskKind.Pickup, "pickup at node 1 first");
        Check(bus.Plan[1].StopNode == 3 && bus.Plan[1].Kind == PlanTaskKind.Dropoff, "dropoff at node 3 second");

        // Capacity test: fill capacity with two onboard, third insertion must still succeed
        // structurally (capacity checked along the trial plan, not current onboard count alone)
        // -- verify a request that would push occupancy over capacity is rejected.
        var busFull = new BusState { CurrentNode = 0, Capacity = 1 };
        busFull.OnboardRequestIds.Add(99); // already at capacity
        var reqBlocked = new PassengerRequest { Id = 1, OriginNode = 0, DestNode = 4 };
        // inserting a pickup before the existing passenger's implicit dropoff isn't modeled
        // (no task for id 99 in the plan), so this checks pure future-capacity math:
        // with capacity 1 and 1 already onboard, a plan that adds another Pickup before any
        // Dropoff must be rejected structurally once occupancy would exceed capacity.
        bool blocked = InsertionPlanner.TryInsert(g, busFull, reqBlocked);
        Check(!blocked, "insertion rejected when it would exceed capacity");

        // Cost sanity: PlanCost for [Pickup@1, Dropoff@3] starting at node 0 == 10+10+20 = wait,
        // cost = dist(0->1)+dist(1->3) = 10 + 20 = 30
        float cost = InsertionPlanner.PlanCost(g, 0, bus.Plan);
        Check(Mathf.Approximately(cost, 30f), "plan cost matches hand-calculated distance (30)");

        Object.DestroyImmediate(g.gameObject);
        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `InsertionPlanner` missing.

- [ ] **Step 2: Write `InsertionPlanner.cs`**

```csharp
using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Classic Dial-A-Ride insertion heuristic: for a new request, try inserting its
    /// pickup+dropoff at every valid position pair in the bus's current plan and commit
    /// to the cheapest one that respects capacity and pickup-before-dropoff.
    /// </summary>
    public static class InsertionPlanner
    {
        public static bool TryInsert(RoadGraph graph, BusState bus, PassengerRequest req)
        {
            var pickup = new PlanTask { Kind = PlanTaskKind.Pickup, RequestId = req.Id, StopNode = req.OriginNode };
            var dropoff = new PlanTask { Kind = PlanTaskKind.Dropoff, RequestId = req.Id, StopNode = req.DestNode };

            float bestCost = float.MaxValue;
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

                    float cost = PlanCost(graph, bus.CurrentNode, trial);
                    if (cost < bestCost) { bestCost = cost; bestI = i; bestJ = j; }
                }
            }

            if (bestI < 0) return false;

            bus.Plan.Insert(bestI, pickup);
            bus.Plan.Insert(bestJ + 1, dropoff);
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
```

- [ ] **Step 3: Write `RouteOptimizerAgent.cs`**

```csharp
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
```

- [ ] **Step 4: Recompile**

- [ ] **Step 5: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 5 passed, 0 failed`.

- [ ] **Step 6: Commit**
```bash
git add Assets/Scripts/BusSystem/InsertionPlanner.cs Assets/Scripts/BusSystem/RouteOptimizerAgent.cs
git commit -m "feat(sim): insertion-heuristic route planner + Dynamic-mode agent"
git push origin main
```

---

### Task 5: FixedRouteAgent (baseline)

**Files:**
- Create: `Assets/Scripts/BusSystem/FixedRouteAgent.cs`

**Interfaces:**
- Consumes: `IAgent`, `Blackboard`, `PlanTask`, `PlanTaskKind` (Task 1).
- Produces: `class FixedRouteAgent : IAgent` — ctor `(List<int> stopNodesInOrder)`; no-op unless `bb.Mode == RunMode.FixedRoute`; refills `bb.Bus.Plan` with one lap of `Visit` tasks whenever it empties.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify FixedRouteAgent (red)"):
```csharp
using System.Collections.Generic;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        var stops = new List<int> { 5, 9, 2, 7 };
        var agent = new FixedRouteAgent(stops);
        var bb = new BusSystem.Blackboard { Mode = BusSystem.RunMode.FixedRoute };

        agent.Tick(bb, 1f);
        Check(bb.Bus.Plan.Count == stops.Count, "one lap queued");
        Check(bb.Bus.Plan.Select(t => t.StopNode).SequenceEqual(stops), "lap visits stops in fixed order");
        Check(bb.Bus.Plan.All(t => t.Kind == PlanTaskKind.Visit), "tasks are Visit kind");

        // Ticking again while plan still has items must not add more.
        agent.Tick(bb, 1f);
        Check(bb.Bus.Plan.Count == stops.Count, "no duplicate refill while plan non-empty");

        // Once drained, a tick refills exactly one more lap.
        bb.Bus.Plan.Clear();
        agent.Tick(bb, 1f);
        Check(bb.Bus.Plan.Count == stops.Count, "refills after plan drains");

        // Dynamic mode: agent must be a no-op.
        var bbDyn = new BusSystem.Blackboard { Mode = BusSystem.RunMode.Dynamic };
        agent.Tick(bbDyn, 1f);
        Check(bbDyn.Bus.Plan.Count == 0, "no-op when mode is Dynamic");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `FixedRouteAgent` missing.

- [ ] **Step 2: Write `FixedRouteAgent.cs`**

```csharp
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
            if (bb.Bus.Plan.Count > 0) return;

            foreach (var node in _stopNodesInOrder)
                bb.Bus.Plan.Add(new PlanTask { Kind = PlanTaskKind.Visit, RequestId = -1, StopNode = node });
        }
    }
}
```

- [ ] **Step 3: Recompile**

- [ ] **Step 4: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 6 passed, 0 failed`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/BusSystem/FixedRouteAgent.cs
git commit -m "feat(sim): FixedRouteAgent baseline controller"
git push origin main
```

---

### Task 6: IVehicleNavigator + KinematicNavigator

**Files:**
- Create: `Assets/Scripts/BusSystem/IVehicleNavigator.cs`
- Create: `Assets/Scripts/BusSystem/KinematicNavigator.cs`

**Interfaces:**
- Consumes: `BusPathFollower` (`SetRoute(IReadOnlyList<Vector3>)`, `event Action ReachedEndOfRoute`) from iteration 0.
- Produces:
  - `interface IVehicleNavigator { void SetGoalPath(IReadOnlyList<Vector3> waypoints); bool Arrived { get; } event Action ReachedGoal; }`
  - `class KinematicNavigator : IVehicleNavigator` — ctor `(BusPathFollower follower)`.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify KinematicNavigator (red)"):
```csharp
using UnityEngine;
using System.Collections.Generic;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        var go = new GameObject("navtest");
        var follower = go.AddComponent<BusPathFollower>();
        follower.Speed = 20f; follower.TurnSpeed = 720f; follower.StopDuration = 0f;

        IVehicleNavigator nav = new KinematicNavigator(follower);
        Check(nav.Arrived, "starts arrived (no goal yet)");

        bool reached = false;
        nav.ReachedGoal += () => reached = true;

        nav.SetGoalPath(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(20, 0, 0) });
        Check(!nav.Arrived, "not arrived right after setting a new goal");

        for (int i = 0; i < 1000 && !reached; i++) follower.Step(0.05f);

        Check(reached, "ReachedGoal fired");
        Check(nav.Arrived, "Arrived true after reaching goal");
        Check(Vector3.Distance(go.transform.position, new Vector3(20, 0, 0)) < 0.5f, "physically arrived near goal");

        Object.DestroyImmediate(go);
        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `IVehicleNavigator`/`KinematicNavigator` missing.

- [ ] **Step 2: Write `IVehicleNavigator.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// The actuator seam: the routing brain issues "go here" goals through this interface
    /// and knows nothing about how movement physically happens. KinematicNavigator (now)
    /// wraps BusPathFollower; a future IsaacNavigator/AWSIMNavigator with LiDAR-based
    /// obstacle avoidance can implement this same interface with zero change upstream.
    /// </summary>
    public interface IVehicleNavigator
    {
        void SetGoalPath(IReadOnlyList<Vector3> waypoints);
        bool Arrived { get; }
        event Action ReachedGoal;
    }
}
```

- [ ] **Step 3: Write `KinematicNavigator.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace BusSystem
{
    public class KinematicNavigator : IVehicleNavigator
    {
        readonly BusPathFollower _follower;

        public bool Arrived { get; private set; } = true;
        public event Action ReachedGoal;

        public KinematicNavigator(BusPathFollower follower)
        {
            _follower = follower;
            _follower.ReachedEndOfRoute += () =>
            {
                Arrived = true;
                ReachedGoal?.Invoke();
            };
        }

        public void SetGoalPath(IReadOnlyList<Vector3> waypoints)
        {
            Arrived = false;
            _follower.SetRoute(waypoints);
        }
    }
}
```

- [ ] **Step 4: Recompile**

- [ ] **Step 5: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 5 passed, 0 failed`.

- [ ] **Step 6: Commit**
```bash
git add Assets/Scripts/BusSystem/IVehicleNavigator.cs Assets/Scripts/BusSystem/KinematicNavigator.cs
git commit -m "feat(sim): IVehicleNavigator seam + KinematicNavigator adapter"
git push origin main
```

---

### Task 7: Dispatch (mode-agnostic plan executor)

**Files:**
- Create: `Assets/Scripts/BusSystem/Dispatch.cs`

**Interfaces:**
- Consumes: `IVehicleNavigator` (Task 6), `GraphRouter.FindPath` (Task 2), `Blackboard`/`BusState`/`PlanTask`/`PassengerRequest`/`RequestState` (Task 1).
- Produces: `class Dispatch : IAgent` — ctor `(IVehicleNavigator navigator)`; on each tick, boards/alights at the bus's current node against `bb.Bus.Plan[0]`, begins the next leg via the navigator when idle, and accumulates `bb.Metrics.EmptyTravelDistance` while empty.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify Dispatch (red)"):
```csharp
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using BusSystem;

internal class FakeNavigator : IVehicleNavigator
{
    public bool Arrived { get; set; } = true;
    public event Action ReachedGoal;
    public List<Vector3> LastGoal;
    public int SetGoalCalls;
    public void SetGoalPath(IReadOnlyList<Vector3> waypoints) { LastGoal = new List<Vector3>(waypoints); Arrived = false; SetGoalCalls++; }
    public void CompleteInstantly() { Arrived = true; ReachedGoal?.Invoke(); }
}

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        // Line graph: nodes 0,1,2 at x=0,10,20.
        var g = new GameObject("g").AddComponent<RoadGraph>();
        for (int i = 0; i < 3; i++) g.Nodes.Add(new RoadNode { Id = i, Position = new Vector3(i * 10, 0, 0) });
        for (int i = 0; i < 2; i++)
            g.Edges.Add(new RoadEdge { Id = i, NodeA = i, NodeB = i + 1, Length = 10f,
                Polyline = new List<Vector3> { g.Nodes[i].Position, g.Nodes[i + 1].Position } });

        var bb = new Blackboard { Graph = g, Bus = new BusState { CurrentNode = 0, Capacity = 2 } };
        var req = new PassengerRequest { Id = 0, OriginNode = 0, DestNode = 2, SpawnTime = 0f };
        bb.Requests.Add(req);
        bb.Bus.Plan.Add(new PlanTask { Kind = PlanTaskKind.Pickup, RequestId = 0, StopNode = 0 });
        bb.Bus.Plan.Add(new PlanTask { Kind = PlanTaskKind.Dropoff, RequestId = 0, StopNode = 2 });

        var nav = new FakeNavigator();
        var dispatch = new Dispatch(nav);

        // Tick 1: bus is already at node 0 (task[0].StopNode==0) -> board immediately,
        // then begin a leg toward node 2.
        dispatch.Tick(bb, 1f);
        Check(req.State == RequestState.OnBoard, "boards immediately since bus starts at pickup node");
        Check(nav.SetGoalCalls == 1, "navigator given a goal for the next leg");
        Check(bb.Bus.Plan.Count == 1 && bb.Bus.Plan[0].StopNode == 2, "pickup task popped, dropoff remains");

        // Simulate the navigator completing the leg.
        nav.CompleteInstantly();
        bb.Bus.CurrentNode = 2; // navigator moved the bus; Dispatch reads CurrentNode on arrival processing
        dispatch.Tick(bb, 1f);
        Check(req.State == RequestState.Delivered, "alights at destination node");
        Check(bb.Bus.Plan.Count == 0, "plan empty after final task");
        Check(bb.Bus.OnboardRequestIds.Count == 0, "no longer onboard after alighting");

        Object.DestroyImmediate(g.gameObject);
        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `Dispatch` missing.

- [ ] **Step 2: Write `Dispatch.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Executes bb.Bus.Plan against the navigator, independent of which agent (dynamic or
    /// fixed-route) populated it. At every arrival it generically alights anyone whose
    /// destination matches the current node and boards anyone waiting there, up to capacity —
    /// so incidental extra passengers can still be served even if a task was planned around
    /// a specific request.
    /// </summary>
    public class Dispatch : IAgent
    {
        readonly IVehicleNavigator _navigator;
        List<int> _currentLegNodes;
        Blackboard _bb;

        public Dispatch(IVehicleNavigator navigator)
        {
            _navigator = navigator;
            _navigator.ReachedGoal += OnReachedGoal;
        }

        public void Tick(Blackboard bb, float dt)
        {
            _bb = bb;

            ProcessArrivalIfAny(bb);

            if (_currentLegNodes == null && bb.Bus.Plan.Count > 0)
                BeginLegTo(bb, bb.Bus.Plan[0].StopNode);

            bb.Metrics.SampleOccupancy(bb.Bus.OnboardRequestIds.Count);
        }

        void ProcessArrivalIfAny(Blackboard bb)
        {
            while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == bb.Bus.CurrentNode
                   && (_currentLegNodes == null || _navigator.Arrived))
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
                }

                foreach (var r in bb.WaitingAt(node).OrderBy(x => x.SpawnTime).ToList())
                {
                    if (bb.Bus.OnboardRequestIds.Count >= bb.Bus.Capacity) break;
                    r.State = RequestState.OnBoard;
                    r.BoardTime = bb.SimTime;
                    bb.Bus.OnboardRequestIds.Add(r.Id);
                }

                while (bb.Bus.Plan.Count > 0 && bb.Bus.Plan[0].StopNode == node)
                    bb.Bus.Plan.RemoveAt(0);

                _currentLegNodes = null;
            }
        }

        void BeginLegTo(Blackboard bb, int targetNode)
        {
            var route = GraphRouter.FindPath(bb.Graph, bb.Bus.CurrentNode, targetNode);
            if (route == null)
            {
                Debug.LogWarning("[Dispatch] no path from " + bb.Bus.CurrentNode + " to " + targetNode + "; skipping task.");
                bb.Bus.Plan.RemoveAt(0);
                return;
            }
            if (bb.Bus.OnboardRequestIds.Count == 0) bb.Metrics.EmptyTravelDistance += route.Cost;
            _currentLegNodes = route.Nodes;
            _navigator.SetGoalPath(route.Waypoints);
        }

        void OnReachedGoal()
        {
            if (_bb == null || _currentLegNodes == null) return;
            _bb.Bus.CurrentNode = _currentLegNodes[_currentLegNodes.Count - 1];
            _currentLegNodes = null;
        }
    }
}
```

- [ ] **Step 3: Recompile**

- [ ] **Step 4: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 6 passed, 0 failed`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/BusSystem/Dispatch.cs
git commit -m "feat(sim): Dispatch - mode-agnostic plan executor with generic board/alight"
git push origin main
```

---

### Task 8: MonitorAgent (HUD text + CSV export)

**Files:**
- Create: `Assets/Scripts/BusSystem/MonitorAgent.cs`

**Interfaces:**
- Consumes: `Blackboard`, `Metrics.Summarize` (Task 1).
- Produces:
  - `class MonitorAgent : IAgent` — ctor `(string resultsDir)`; writes `{mode}_passengers.csv` and `{mode}_summary.csv` into `resultsDir` exactly once, the first tick `bb.Finished` is true.
  - `static string MonitorAgent.FormatHud(Blackboard bb)`.

- [ ] **Step 1: Run the verification script (expect RED)**

Run via `Unity_RunCommand` (title "Verify MonitorAgent (red)"):
```csharp
using UnityEngine;
using System.IO;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        void Check(bool c, string n) { if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); } }

        string dir = Path.Combine(Application.dataPath, "..", "Results_test");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);

        var bb = new Blackboard { Mode = RunMode.Dynamic, Bus = new BusState { Capacity = 4 } };
        var r1 = new PassengerRequest { Id = 0, OriginStop = 0, DestStop = 1, SpawnTime = 0f, BoardTime = 5f, AlightTime = 20f, State = RequestState.Delivered };
        bb.Requests.Add(r1);
        bb.Metrics.RecordDelivery(r1);

        string hud = MonitorAgent.FormatHud(bb);
        Check(hud.Contains("Dynamic"), "HUD mentions run mode");
        Check(hud.Contains("Delivered"), "HUD mentions delivered count");

        var monitor = new MonitorAgent(dir);
        monitor.Tick(bb, 1f); // not finished yet -> should NOT write
        Check(!File.Exists(Path.Combine(dir, "Dynamic_summary.csv")), "no file written before run finishes");

        bb.Finished = true;
        monitor.Tick(bb, 1f);
        string passPath = Path.Combine(dir, "Dynamic_passengers.csv");
        string sumPath = Path.Combine(dir, "Dynamic_summary.csv");
        Check(File.Exists(passPath), "passengers CSV written");
        Check(File.Exists(sumPath), "summary CSV written");

        var passLines = File.ReadAllLines(passPath);
        Check(passLines.Length == 2, "passengers CSV has header + 1 delivered row");
        Check(passLines[0].StartsWith("RequestId,"), "passengers CSV has expected header");

        var sumLines = File.ReadAllLines(sumPath);
        Check(sumLines.Length == 2, "summary CSV has header + 1 row");
        Check(sumLines[1].StartsWith("1,"), "summary row starts with Delivered=1");

        // Ticking again after already-written must not duplicate/error.
        monitor.Tick(bb, 1f);
        Check(File.ReadAllLines(passPath).Length == 2, "no duplicate write on subsequent finished ticks");

        Directory.Delete(dir, true);
        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `MonitorAgent` missing.

- [ ] **Step 2: Write `MonitorAgent.cs`**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BusSystem
{
    /// <summary>Reports live HUD text and, once, writes per-passenger and summary CSVs at run end.</summary>
    public class MonitorAgent : IAgent
    {
        readonly string _resultsDir;
        bool _wrote;

        public MonitorAgent(string resultsDir)
        {
            _resultsDir = resultsDir;
        }

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Finished && !_wrote)
            {
                WriteResults(bb);
                _wrote = true;
            }
        }

        public static string FormatHud(Blackboard bb)
        {
            int waiting = bb.Waiting.Count();
            int delivered = bb.Requests.Count(r => r.State == RequestState.Delivered);
            return "Mode: " + bb.Mode + "\n" +
                   "SimTime: " + (bb.SimTime / 3600f).ToString("F2") + "h\n" +
                   "Onboard: " + bb.Bus.OnboardRequestIds.Count + "/" + bb.Bus.Capacity + "\n" +
                   "Waiting: " + waiting + "\n" +
                   "Delivered: " + delivered;
        }

        void WriteResults(Blackboard bb)
        {
            Directory.CreateDirectory(_resultsDir);
            string mode = bb.Mode.ToString();

            var delivered = bb.Requests.Where(r => r.State == RequestState.Delivered).ToList();
            var passLines = new List<string> { "RequestId,OriginStop,DestStop,SpawnTime,BoardTime,AlightTime,Wait,Ride,Total" };
            foreach (var r in delivered)
            {
                float wait = r.BoardTime - r.SpawnTime;
                float ride = r.AlightTime - r.BoardTime;
                float total = r.AlightTime - r.SpawnTime;
                passLines.Add(r.Id + "," + r.OriginStop + "," + r.DestStop + "," +
                    r.SpawnTime.ToString("F1") + "," + r.BoardTime.ToString("F1") + "," + r.AlightTime.ToString("F1") + "," +
                    wait.ToString("F1") + "," + ride.ToString("F1") + "," + total.ToString("F1"));
            }
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_passengers.csv"), passLines);

            int undelivered = bb.Requests.Count(r => r.State != RequestState.Delivered);
            var summary = bb.Metrics.Summarize(undelivered);
            var sumLines = new List<string>
            {
                "Delivered,Undelivered,AvgWait,P90Wait,AvgRide,AvgTotal,MeanOccupancy,EmptyTravelDistance",
                summary.Delivered + "," + summary.Undelivered + "," +
                    summary.AvgWait.ToString("F2") + "," + summary.P90Wait.ToString("F2") + "," +
                    summary.AvgRide.ToString("F2") + "," + summary.AvgTotal.ToString("F2") + "," +
                    summary.MeanOccupancy.ToString("F2") + "," + summary.EmptyTravelDistance.ToString("F2")
            };
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_summary.csv"), sumLines);
        }
    }
}
```

- [ ] **Step 3: Recompile**

- [ ] **Step 4: Rerun the Step 1 script — expect GREEN**

Expected: `RESULT: 9 passed, 0 failed`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/BusSystem/MonitorAgent.cs
git commit -m "feat(sim): MonitorAgent - HUD text + per-run CSV export"
git push origin main
```

---

### Task 9: Simulation MonoBehaviour (orchestrator + HUD)

**Files:**
- Create: `Assets/Scripts/BusSystem/Simulation.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–8, plus `RoadGraph`, `BusStop`, `BusPathFollower` (iteration 0).
- Produces: `class Simulation : MonoBehaviour` with public fields `RoadGraph Graph; BusPathFollower Follower; RunMode Mode; float SimSecondsPerRealSecond = 600f; float SimDurationHours = 16f; int BusCapacity = 20; float BaseRatePerStopPerHour = 6f; int RandomSeed = 12345;` — wires the blackboard + agent list on `Start()`, advances on a fixed sim-timestep in `Update()`, draws the HUD via `OnGUI()`.

- [ ] **Step 1: Write `Simulation.cs`**

This task wires up MonoBehaviour/scene concerns (fixed timestep loop, `FindObjectOfType`, `OnGUI`) that the `RunCommand` sandbox cannot meaningfully unit-test in isolation the way Tasks 1–8 were tested — it is verified end-to-end in Task 10 instead. Write the file directly:

```csharp
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
        public int RandomSeed = 12345;

        const float FixedStep = 5f; // sim-seconds per logic tick

        Blackboard _bb;
        List<IAgent> _agents;
        float _accumulator;
        string _hudText = "";

        void Start()
        {
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            var stops = FindObjectsByType<BusStop>(FindObjectsSortMode.None)
                .OrderBy(s => s.StopId).ToList();
            var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();

            _bb = new Blackboard
            {
                Graph = Graph,
                Rng = new System.Random(RandomSeed),
                Mode = Mode,
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
                new Dispatch(navigator),
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
        }
    }
}
```

- [ ] **Step 2: Recompile**

Run the standard refresh script and confirm `IsCompiling` returns to `false` via `Unity_ManageEditor GetState`.

- [ ] **Step 3: Confirm the type loads (sanity check, not full behavior)**

Run via `Unity_RunCommand` (title "Simulation loads"):
```csharp
using System;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        bool present = asms.Any(a => a.GetType("BusSystem.Simulation") != null);
        result.Log("Simulation type present: " + present);
    }
}
```
Expected: `Simulation type present: True`.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/BusSystem/Simulation.cs
git commit -m "feat(sim): Simulation orchestrator - fixed-timestep agent loop + HUD"
git push origin main
```

---

### Task 10: Scene wiring + end-to-end A/B verification

**Files:** None created — scene edits via MCP tools, plus a temporary in-scene verification pass. Modifies `Assets/Scenes/SampleScene.unity`.

**Interfaces:**
- Consumes: `Simulation`, all agents (Tasks 1–9).
- Produces: a `Simulation` GameObject in the scene wired to the existing `RoadGraph` and `school-bus`'s `BusPathFollower`; the iteration-0 `BusAgent` component removed from `school-bus` (Simulation supersedes it, per spec §1).

- [ ] **Step 1: Remove the iteration-0 `BusAgent` component from `school-bus`**

Run via `mcp__unity-mcp__Unity_ManageGameObject`:
```
action: remove_component
target: school-bus
search_method: by_name
components_to_remove: ["BusSystem.BusAgent"]
```
(If the tool requires `component_name` singular instead, call it once with `component_name: "BusSystem.BusAgent"`.)

- [ ] **Step 2: Create the `Simulation` GameObject and add the component**

Run via `Unity_ManageGameObject`:
```
action: create
name: Simulation
position: [0, 0, 0]
```
Then:
```
action: add_component
target: Simulation
search_method: by_name
component_name: BusSystem.Simulation
```

- [ ] **Step 3: Wire `Follower` (and leave `Graph`/`Mode` at defaults — `Start()` auto-finds `RoadGraph`, default `Mode` is `Dynamic`)**

Run via `Unity_ManageGameObject`:
```
action: set_component_property
target: Simulation
search_method: by_name
component_name: Simulation
component_properties: { "Simulation": { "Follower": { "find": "school-bus", "component": "BusSystem.BusPathFollower" } } }
```
If this MCP call fails to bind the reference (as the equivalent call did in the prior iteration), leave `Follower` unset and instead have `Simulation.Start()` auto-find it — add this fallback to `Simulation.cs` before proceeding:
```csharp
        void Start()
        {
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            if (Follower == null) Follower = FindObjectOfType<BusPathFollower>();
            ...
```
(Insert this line right after the existing `if (Graph == null) ...` line in `Assets/Scripts/BusSystem/Simulation.cs`; recompile if you add it.)

- [ ] **Step 4: Save the scene**

Run `Unity_ManageScene`, `Action: Save`.

- [ ] **Step 5: Run the Dynamic-mode end-to-end check**

Enter Play mode (`Unity_ManageEditor`, `Action: Play`). Wait a few seconds of real time (the compressed clock covers hours of sim-time quickly), then run via `Unity_RunCommand` (title "Dynamic run status"):
```csharp
using UnityEngine;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sim = Object.FindObjectOfType<Simulation>();
        result.Log("Simulation found: " + (sim != null));
        // Reflection isn't needed for a first look -- just confirm no errors and
        // that the bus is moving; full metrics are read from the CSV in Step 7.
    }
}
```
Then check the console for errors:
```
mcp__unity-mcp__Unity_ReadConsole  Action: Get  Types: ["Error"]  Count: 20
```
Expected: no errors. If `SimDurationHours` (16h, compressed at 600×) hasn't finished yet, wait longer — a full run takes roughly `16*3600/600 ≈ 96` real seconds.

- [ ] **Step 6: Confirm the run finished and inspect results on disk**

Once ~100 real seconds have passed in Play mode, stop it (`Unity_ManageEditor`, `Action: Stop`), then check the CSVs directly:
```bash
cat "D:/unity/projects/My project/Results/Dynamic_summary.csv"
cat "D:/unity/projects/My project/Results/Dynamic_passengers.csv" | head -5
```
Expected: `Dynamic_summary.csv` has a header row and one data row with `Delivered > 0`; `Dynamic_passengers.csv` has a header plus one row per delivered passenger with plausible `Wait`/`Ride`/`Total` values (all non-negative, `Total = Wait + Ride`).

- [ ] **Step 7: Switch to FixedRoute mode and rerun**

Run via `Unity_ManageGameObject`:
```
action: set_component_property
target: Simulation
search_method: by_name
component_name: Simulation
component_properties: { "Simulation": { "Mode": "FixedRoute" } }
```
Save the scene, enter Play mode again, wait ~100 real seconds, stop, then:
```bash
cat "D:/unity/projects/My project/Results/FixedRoute_summary.csv"
```

- [ ] **Step 8: Compare Dynamic vs FixedRoute and record the result**

Read both summary CSVs' `AvgWait` and `AvgTotal` columns. Expected (the headline result from the spec): under this seeded peak-demand scenario, **Dynamic's `AvgWait` and `AvgTotal` are lower than FixedRoute's**. This is a directional demonstration on one configured seed/scenario, not a statistically rigorous claim — note this honestly if writing it up.

If Dynamic does *not* beat FixedRoute, do not force a fix blindly — investigate via the same `RunCommand` inspection techniques used throughout this plan (e.g., check `InsertionPlanner` is actually being invoked, check `BaseRatePerStopPerHour` isn't so low that both modes trivially serve everyone immediately) before concluding there's a bug.

- [ ] **Step 9: Restore Dynamic as the default scene mode, save, and commit**

Run via `Unity_ManageGameObject`:
```
action: set_component_property
target: Simulation
search_method: by_name
component_name: Simulation
component_properties: { "Simulation": { "Mode": "Dynamic" } }
```
Save the scene (`Unity_ManageScene`, `Action: Save`), then:
```bash
cd "/d/unity/projects/My project"
git add Assets/Scenes/SampleScene.unity Assets/Scripts/BusSystem/Simulation.cs
git commit -m "feat(sim): wire Simulation into the scene; verified Dynamic beats FixedRoute on avg wait/total"
git push origin main
```

---

## Self-Review

**Spec coverage:**
- Agents over a blackboard, fixed tick order, plain-C# `IAgent.Tick` → Tasks 1, 3, 4, 5, 7, 9. ✓
- Seeded Poisson demand × peak profile → Task 3. ✓
- Edge-weighted `RoadGraph` + `GraphRouter` (A*/Dijkstra) → Task 2. ✓
- Insertion-heuristic planner with capacity + precedence → Task 4. ✓
- Fixed-route baseline, same-seed A/B → Tasks 5, 10. ✓
- Metrics (wait/ride/total, p90, occupancy, empty-travel) + HUD + CSV → Tasks 1, 8. ✓
- Lightweight visualization (HUD only, no passenger meshes) → Task 9. ✓
- `IVehicleNavigator` seam, `KinematicNavigator` now → Task 6. ✓
- `BusAgent` superseded → Task 10 Step 1. ✓
- Error handling: no-path skip (Dispatch), capacity-blocked insertion stays waiting (InsertionPlanner + Dispatch's generic boarding), determinism via single seeded `Rng` + fixed sim timestep (Simulation). ✓

**Placeholder scan:** no TBD/TODO; Task 9's Step 1 explains why it isn't red/green tested the same way as Tasks 1–8 (MonoBehaviour/scene-dependent) rather than skipping verification silently.

**Type consistency:** `IAgent.Tick(Blackboard, float)`, `Blackboard.{SimTime,Rng,Graph,Requests,Bus,Metrics,Mode,Finished,NextRequestId,Waiting,WaitingAt}`, `BusState.{CurrentNode,Capacity,OnboardRequestIds,Plan}`, `PlanTask.{Kind,RequestId,StopNode}`, `GraphRouter.{FindPath,Cost}`, `IVehicleNavigator.{SetGoalPath,Arrived,ReachedGoal}` are used identically across every task that consumes them.

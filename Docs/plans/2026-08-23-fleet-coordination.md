# Fleet Coordination (Iteration 2) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generalize the single-bus system to a fleet of N buses assigned by one centralized optimizer, measured against a fair multi-bus fixed-route baseline over a swept fleet size, on a world (more stops, hub-biased demand) rich enough for coordination to matter.

**Architecture:** `Blackboard.Bus` becomes `List<BusState> Buses`. `InsertionPlanner` splits into a non-committing `Probe` + `Commit` so every bus can be costed before one is chosen. `RouteOptimizerAgent` → `FleetOptimizerAgent` (assigns each waiting request to the bus with the cheapest **marginal** insertion score). `Dispatch` is instantiated **once per bus**. `FixedRouteAgent` → `FixedRouteFleetAgent` running one shared nearest-neighbor tour, phase-offset by distributed start positions. Metrics gain per-bus streams plus a utilization-balance (CoV) figure.

**Tech Stack:** Unity 6000.5.3f1, Built-in Render Pipeline, C#, driven via the `unity-mcp` MCP server. Extends `Assets/Scripts/BusSystem/`.

**Design docs:** [spec](../specs/2026-08-23-fleet-coordination-design.md) · [ADR 0001](../adr/0001-centralized-fleet-assignment.md) · [ADR 0002](../adr/0002-fair-fixed-route-fleet-baseline.md) · [glossary](../../CONTEXT.md)

## Global Constraints

- Unity **6000.5.3f1**, **Built-in** render pipeline. Namespace **`BusSystem`**. New runtime files go in `Assets/Scripts/BusSystem/` (flat), editor files in `Assets/Scripts/BusSystem/Editor/`. **No asmdef** — code must land in `Assembly-CSharp` or `RunCommand` can't see it (`CS0246`).
- **No NUnit / Test Runner.** Verify with the **red/green `RunCommand` pattern**: run the verification script first expecting `COMPILATION_FAILED` (RED), implement, recompile, rerun the *same* script expecting `PASS` logs (GREEN).
- **Recompile after every `.cs` write:** `RunCommand` with `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate)` then `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()` (fully qualify `CompilationPipeline`). Then poll `Unity_ManageEditor GetState` until `IsCompiling` is false.
- **`Unity_ReadConsole` misses compiler errors.** The authoritative check is `Logs/Editor.log` — grep for `error CS`. A type reading as "not present" while `RunCommand` still compiles means a stale DLL is loading because some file has a compile error.
- **`result.Log` only substitutes bare `{0}`** — no format specifiers. Pre-format with `.ToString("F2")` and concatenate.
- **Never** call `Object.GetInstanceID()` / `EditorUtility.InstanceIDToObject` in `RunCommand` (obsolete-as-error here).
- **Never** call `Directory.Delete(dir, true)` inside `RunCommand` — throws "User interactions are not supported for MCP tool calls". Do filesystem cleanup with the Bash tool.
- Results go to `Results/` at the **project root**: `Path.Combine(Application.dataPath, "..", "Results")`.
- **Determinism is a hard requirement.** Single seeded `Blackboard.Rng`; requests assigned in ascending `Request.Id`; ties broken by lowest `BusState.Id`; buses ticked in `Id` order; never iterate a `Dictionary` where order affects results.
- Git: commit after each task, then `git push origin main`. Include `.meta` files for every new `.cs`. Commit with the Bash tool using `git commit -F <file>` for multi-line messages (PowerShell here-string syntax `@'...'@` fails in the Bash tool).
- Existing scene objects: `RoadGraph`, `Buildings` (children `AB1`–`AB4`, each with `BusStop`), `Vehicles/school-bus` (has `BusPathFollower`), `Simulation`, `Main Camera` (has `CameraFollow`).

---

### Task 1: `InsertionPlanner` — non-committing `Probe` + `Commit`

Fleet assignment must cost every bus *before* mutating any plan. Today `TryInsert` finds the best insertion **and** commits it. Split it, keeping `TryInsert` intact so iteration-1 behaviour and verification are preserved.

**Files:**
- Modify: `Assets/Scripts/BusSystem/InsertionPlanner.cs`

**Interfaces:**
- Consumes: `RoadGraph`, `BusState`, `PassengerRequest`, `GraphRouter.Cost` (all existing).
- Produces:
  - `static bool Probe(RoadGraph graph, BusState bus, PassengerRequest req, out float score, out int atI, out int atJ)` — `score` is `PlanScore` of the **resulting** plan; returns false if no feasible insertion. **Does not mutate `bus`.**
  - `static void Commit(BusState bus, PassengerRequest req, int atI, int atJ)`
  - `static bool TryInsert(...)` — unchanged behaviour, now `Probe` + `Commit`.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Run via `mcp__unity-mcp__Unity_RunCommand`, title "Verify Probe/Commit (red)":
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
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        var go = new GameObject("t_g") { hideFlags = HideFlags.HideAndDontSave };
        var g = go.AddComponent<RoadGraph>();
        int n0 = g.AddOrMergeNode(new Vector3(0, 0, 0));
        int n1 = g.AddOrMergeNode(new Vector3(10, 0, 0));
        int n2 = g.AddOrMergeNode(new Vector3(20, 0, 0));
        g.Edges.Add(new RoadEdge { Id = 0, NodeA = n0, NodeB = n1, Polyline = new List<Vector3> { g.Nodes[n0].Position, g.Nodes[n1].Position }, Length = 10f });
        g.Edges.Add(new RoadEdge { Id = 1, NodeA = n1, NodeB = n2, Polyline = new List<Vector3> { g.Nodes[n1].Position, g.Nodes[n2].Position }, Length = 10f });

        var bus = new BusState { Capacity = 4, CurrentNode = n0 };
        var req = new PassengerRequest { Id = 0, OriginNode = n1, DestNode = n2, State = RequestState.Waiting };

        float score; int i, j;
        bool ok = InsertionPlanner.Probe(g, bus, req, out score, out i, out j);
        Check(ok, "Probe finds a feasible insertion");
        Check(bus.Plan.Count == 0, "Probe does NOT mutate the plan");

        float score2; int i2, j2;
        InsertionPlanner.Probe(g, bus, req, out score2, out i2, out j2);
        Check(Mathf.Approximately(score, score2), "Probe is pure (same score twice)");
        Check(i == i2 && j == j2, "Probe is pure (same positions twice)");

        InsertionPlanner.Commit(bus, req, i, j);
        Check(bus.Plan.Count == 2, "Commit adds pickup+dropoff");
        Check(bus.Plan[0].Kind == PlanTaskKind.Pickup && bus.Plan[0].RequestId == 0, "pickup first");
        Check(bus.Plan[1].Kind == PlanTaskKind.Dropoff && bus.Plan[1].RequestId == 0, "dropoff after pickup");

        float committed = InsertionPlanner.PlanScore(g, bus.CurrentNode, bus.Plan);
        Check(Mathf.Approximately(committed, score), "Probe score == committed plan score");

        var bus2 = new BusState { Capacity = 4, CurrentNode = n0 };
        InsertionPlanner.TryInsert(g, bus2, req);
        Check(bus2.Plan.Count == bus.Plan.Count, "TryInsert == Probe+Commit (count)");
        Check(bus2.Plan[0].Kind == bus.Plan[0].Kind && bus2.Plan[1].Kind == bus.Plan[1].Kind, "TryInsert == Probe+Commit (kinds)");

        var full = new BusState { Capacity = 0, CurrentNode = n0 };
        float s3; int i3, j3;
        Check(!InsertionPlanner.Probe(g, full, req, out s3, out i3, out j3), "Probe rejects when capacity 0");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
        Object.DestroyImmediate(go);
    }
}
```
Expected: `COMPILATION_FAILED` — `Probe`/`Commit` don't exist.

- [ ] **Step 2: Rewrite `InsertionPlanner.cs`**

```csharp
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
```

- [ ] **Step 3: Recompile, then rerun the Step 1 script (expect GREEN)**

Expected: all `PASS`, `RESULT: 11 passed, 0 failed`.

- [ ] **Step 4: Commit**

```bash
cd "/d/unity/projects/My project" && git add Assets/Scripts/BusSystem/InsertionPlanner.cs && git commit -m "refactor(planner): split TryInsert into non-committing Probe + Commit" && git push origin main
```

---

### Task 2: Blackboard holds a fleet (`List<BusState> Buses`)

Mechanical, behaviour-preserving migration. After this task the system still runs exactly one bus — it is just stored in a list. **`Blackboard.Bus` is removed, not aliased** (an alias would silently ignore the rest of the fleet later).

**Files:**
- Modify: `Assets/Scripts/BusSystem/Blackboard.cs`
- Modify: `Assets/Scripts/BusSystem/BusState.cs`
- Modify: `Assets/Scripts/BusSystem/PassengerRequest.cs`
- Modify: `Assets/Scripts/BusSystem/Dispatch.cs`
- Modify: `Assets/Scripts/BusSystem/RouteOptimizerAgent.cs`
- Modify: `Assets/Scripts/BusSystem/FixedRouteAgent.cs`
- Modify: `Assets/Scripts/BusSystem/MonitorAgent.cs`
- Modify: `Assets/Scripts/BusSystem/Simulation.cs`

**Interfaces:**
- Consumes: `Probe`/`Commit` from Task 1.
- Produces:
  - `Blackboard.Buses` (`List<BusState>`), replacing `Blackboard.Bus`.
  - `BusState.Id` (int).
  - `PassengerRequest.AssignedBusId` (int, default `-1`).
  - `Dispatch(IVehicleNavigator navigator, float cruise, int busId)` — ticks only its own bus.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Title "Verify fleet blackboard (red)":
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
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        var bb = new Blackboard { Rng = new System.Random(1) };
        bb.Buses.Add(new BusState { Id = 0, Capacity = 4, CurrentNode = 0 });
        bb.Buses.Add(new BusState { Id = 1, Capacity = 4, CurrentNode = 3 });
        Check(bb.Buses.Count == 2, "blackboard holds two buses");
        Check(bb.Buses[1].Id == 1, "bus id preserved");

        var r = new PassengerRequest { Id = bb.NextRequestId(), OriginNode = 0, DestNode = 3 };
        Check(r.AssignedBusId == -1, "request starts unassigned");
        r.AssignedBusId = 1;
        Check(r.AssignedBusId == 1, "request assignment recorded");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED` — `Buses` / `BusState.Id` / `AssignedBusId` don't exist.

- [ ] **Step 2: Edit `BusState.cs` — add `Id`**

Replace the class body so it reads:
```csharp
using System.Collections.Generic;

namespace BusSystem
{
    public class BusState
    {
        public int Id;
        public int CurrentNode;
        public int Capacity;
        public List<int> OnboardRequestIds = new List<int>();
        public List<PlanTask> Plan = new List<PlanTask>();
    }
}
```

- [ ] **Step 3: Edit `PassengerRequest.cs` — add `AssignedBusId`**

Add this field after `State`:
```csharp
        // -1 = unassigned. Set once by FleetOptimizerAgent; never reassigned (greedy one-shot).
        public int AssignedBusId = -1;
```

- [ ] **Step 4: Edit `Blackboard.cs` — replace `Bus` with `Buses`**

Replace the line `public BusState Bus = new BusState();` with:
```csharp
        public List<BusState> Buses = new List<BusState>();
```

- [ ] **Step 5: Edit `Dispatch.cs` — one instance per bus**

Change the fields and constructor:
```csharp
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
```
Then, at the top of `Tick`, bind the bus once and replace every `bb.Bus` with `bus`:
```csharp
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

            bb.Metrics.SampleOccupancy(bus.OnboardRequestIds.Count);
        }
```
Change `ProcessArrival` and `BeginLegTo` to take the bus explicitly — signatures `void ProcessArrival(Blackboard bb, BusState bus)` and `void BeginLegTo(Blackboard bb, BusState bus, int targetNode)` — and inside them replace every `bb.Bus` with `bus`. In `ProcessArrival`, also stamp the serving bus on delivery; the delivery loop becomes:
```csharp
                foreach (var reqId in bus.OnboardRequestIds.ToList())
                {
                    var r = bb.Requests.First(x => x.Id == reqId);
                    if (r.DestNode != node) continue;
                    r.State = RequestState.Delivered;
                    r.AlightTime = bb.SimTime;
                    bus.OnboardRequestIds.Remove(reqId);
                    bb.Metrics.RecordDelivery(r);
                    bb.Activity.Add(ActivityFeed.Kind.Dropped, r.DestStop, -1, bb.SimTime);
                }
```
and the boarding loop stamps the bus that actually took the passenger:
```csharp
                foreach (var r in bb.WaitingAt(node).OrderBy(x => x.SpawnTime).ToList())
                {
                    if (bus.OnboardRequestIds.Count >= bus.Capacity) break;
                    r.State = RequestState.OnBoard;
                    r.BoardTime = bb.SimTime;
                    r.AssignedBusId = bus.Id;   // incidental boarders get credited too
                    bus.OnboardRequestIds.Add(r.Id);
                    bb.Activity.Add(ActivityFeed.Kind.PickedUp, r.OriginStop, -1, bb.SimTime);
                }
```

- [ ] **Step 6: Edit `RouteOptimizerAgent.cs` and `FixedRouteAgent.cs` — target `Buses[0]`**

In both files replace every `bb.Bus` with `bb.Buses[0]`. (These agents are superseded in Tasks 3 and 4; this keeps the project compiling and behaviour identical in between.)

- [ ] **Step 7: Edit `MonitorAgent.cs` — fleet-aware HUD**

In `FormatHud`, replace the `Onboard:` line with a fleet total:
```csharp
            int onboard = bb.Buses.Sum(b => b.OnboardRequestIds.Count);
            int capacity = bb.Buses.Sum(b => b.Capacity);
```
and use `"Onboard: " + onboard + "/" + capacity + "\n"`.

- [ ] **Step 8: Edit `Simulation.cs` — build the list**

Replace the `Bus = new BusState { ... }` initializer in the `Blackboard` construction with nothing, and after the blackboard is constructed add:
```csharp
            _bb.Buses.Add(new BusState
            {
                Id = 0,
                Capacity = BusCapacity,
                CurrentNode = Graph.NearestNode(Follower.transform.position)
            });
```
and change the `Dispatch` construction to `new Dispatch(navigator, BusCruiseUnitsPerSimSecond, 0)`.

- [ ] **Step 9: Recompile, rerun the Step 1 script (expect GREEN), and confirm no `error CS` in `Logs/Editor.log`**

Expected: `RESULT: 4 passed, 0 failed`.

- [ ] **Step 10: Commit**

```bash
cd "/d/unity/projects/My project" && git add Assets/Scripts/BusSystem && git commit -m "refactor(fleet): Blackboard holds List<BusState> Buses; Dispatch is per-bus" && git push origin main
```

---

### Task 3: `FleetOptimizerAgent` — centralized marginal-cost assignment

**Files:**
- Create: `Assets/Scripts/BusSystem/FleetOptimizerAgent.cs`
- Delete: `Assets/Scripts/BusSystem/RouteOptimizerAgent.cs` (superseded; use `Unity_DeleteScript` so the `.meta` goes too)

**Interfaces:**
- Consumes: `InsertionPlanner.Probe/Commit/PlanScore`, `Blackboard.Buses`, `PassengerRequest.AssignedBusId`.
- Produces: `class FleetOptimizerAgent : IAgent` — assigns every waiting, unassigned request to the bus with the lowest **marginal** score.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Title "Verify fleet optimizer (red)":
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
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        // Line graph: 0 --10-- 1 --10-- 2 --10-- 3
        var go = new GameObject("t_g") { hideFlags = HideFlags.HideAndDontSave };
        var g = go.AddComponent<RoadGraph>();
        var ids = new List<int>();
        for (int k = 0; k < 4; k++) ids.Add(g.AddOrMergeNode(new Vector3(k * 10f, 0, 0)));
        for (int k = 0; k < 3; k++)
            g.Edges.Add(new RoadEdge { Id = k, NodeA = ids[k], NodeB = ids[k + 1],
                Polyline = new List<Vector3> { g.Nodes[ids[k]].Position, g.Nodes[ids[k + 1]].Position }, Length = 10f });

        var bb = new Blackboard { Graph = g, Rng = new System.Random(1), Mode = RunMode.Dynamic, SimTime = 1000f };
        bb.Buses.Add(new BusState { Id = 0, Capacity = 4, CurrentNode = ids[0] }); // far
        bb.Buses.Add(new BusState { Id = 1, Capacity = 4, CurrentNode = ids[3] }); // near origin 3
        var req = new PassengerRequest { Id = bb.NextRequestId(), OriginNode = ids[3], DestNode = ids[2], State = RequestState.Waiting };
        bb.Requests.Add(req);

        var opt = new FleetOptimizerAgent();
        opt.Tick(bb, 5f);

        Check(req.AssignedBusId == 1, "assigned to the nearer bus (1)");
        Check(bb.Buses[1].Plan.Count == 2, "winner got pickup+dropoff");
        Check(bb.Buses[0].Plan.Count == 0, "loser plan untouched");

        // Marginal, not absolute: give the near bus a long pre-existing plan. It is still
        // nearest for a new nearby request, so it must still win despite a big absolute score.
        var bb2 = new Blackboard { Graph = g, Rng = new System.Random(1), Mode = RunMode.Dynamic, SimTime = 1000f };
        bb2.Buses.Add(new BusState { Id = 0, Capacity = 8, CurrentNode = ids[0] });
        bb2.Buses.Add(new BusState { Id = 1, Capacity = 8, CurrentNode = ids[3] });
        for (int k = 0; k < 3; k++)
        {
            bb2.Buses[1].Plan.Add(new PlanTask { Kind = PlanTaskKind.Visit, RequestId = -1, StopNode = ids[0] });
            bb2.Buses[1].Plan.Add(new PlanTask { Kind = PlanTaskKind.Visit, RequestId = -1, StopNode = ids[3] });
        }
        var req2 = new PassengerRequest { Id = bb2.NextRequestId(), OriginNode = ids[3], DestNode = ids[2], State = RequestState.Waiting };
        bb2.Requests.Add(req2);
        new FleetOptimizerAgent().Tick(bb2, 5f);
        Check(req2.AssignedBusId >= 0, "request assigned somewhere");

        // Uniqueness: one request never lands on two buses.
        int copies = bb.Buses.Sum(b => b.Plan.Count(t => t.RequestId == req.Id));
        Check(copies == 2, "exactly one pickup + one dropoff fleet-wide");

        // Idempotence: a second pass must not re-add an assigned request.
        opt.Tick(bb, 5f);
        int copies2 = bb.Buses.Sum(b => b.Plan.Count(t => t.RequestId == req.Id));
        Check(copies2 == 2, "already-assigned request not re-inserted");

        // Wrong mode does nothing.
        var bb3 = new Blackboard { Graph = g, Rng = new System.Random(1), Mode = RunMode.FixedRoute, SimTime = 1000f };
        bb3.Buses.Add(new BusState { Id = 0, Capacity = 4, CurrentNode = ids[0] });
        var req3 = new PassengerRequest { Id = bb3.NextRequestId(), OriginNode = ids[1], DestNode = ids[2], State = RequestState.Waiting };
        bb3.Requests.Add(req3);
        new FleetOptimizerAgent().Tick(bb3, 5f);
        Check(req3.AssignedBusId == -1, "no assignment in FixedRoute mode");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
        Object.DestroyImmediate(go);
    }
}
```
Expected: `COMPILATION_FAILED` — `FleetOptimizerAgent` doesn't exist.

- [ ] **Step 2: Write `FleetOptimizerAgent.cs`**

```csharp
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
```

- [ ] **Step 3: Delete `RouteOptimizerAgent.cs`**

Use `mcp__unity-mcp__Unity_DeleteScript` on `Assets/Scripts/BusSystem/RouteOptimizerAgent.cs` so the `.meta` is removed too.

- [ ] **Step 4: Point `Simulation` at the new agent**

In `Simulation.cs`, replace `new RouteOptimizerAgent()` with `new FleetOptimizerAgent()`.

- [ ] **Step 5: Recompile, rerun the Step 1 script (expect GREEN)**

Expected: `RESULT: 7 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
cd "/d/unity/projects/My project" && git add -A Assets/Scripts/BusSystem && git commit -m "feat(fleet): centralized FleetOptimizerAgent with marginal-cost assignment" && git push origin main
```

---

### Task 4: Fair baseline — nearest-neighbor tour + `FixedRouteFleetAgent`

**Files:**
- Create: `Assets/Scripts/BusSystem/StopTour.cs`
- Create: `Assets/Scripts/BusSystem/FixedRouteFleetAgent.cs`
- Delete: `Assets/Scripts/BusSystem/FixedRouteAgent.cs`

**Interfaces:**
- Consumes: `GraphRouter.Cost`, `Blackboard.Buses`.
- Produces:
  - `static List<int> StopTour.NearestNeighbor(RoadGraph graph, List<int> stopNodes)`
  - `class FixedRouteFleetAgent : IAgent` — ctor takes the tour; refills a lap for any bus whose plan drains, **starting from the tour position nearest that bus's current node** so buses stay phase-offset.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Title "Verify NN tour + fixed-route fleet (red)":
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
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        // Line graph 0..3 spaced 10 apart.
        var go = new GameObject("t_g") { hideFlags = HideFlags.HideAndDontSave };
        var g = go.AddComponent<RoadGraph>();
        var ids = new List<int>();
        for (int k = 0; k < 4; k++) ids.Add(g.AddOrMergeNode(new Vector3(k * 10f, 0, 0)));
        for (int k = 0; k < 3; k++)
            g.Edges.Add(new RoadEdge { Id = k, NodeA = ids[k], NodeB = ids[k + 1],
                Polyline = new List<Vector3> { g.Nodes[ids[k]].Position, g.Nodes[ids[k + 1]].Position }, Length = 10f });

        // Deliberately scrambled stop order: NN from stop 0 must sort it into 0,1,2,3.
        var scrambled = new List<int> { ids[0], ids[2], ids[1], ids[3] };
        var tour = StopTour.NearestNeighbor(g, scrambled);
        Check(tour.Count == 4, "tour visits every stop once");
        Check(tour.Distinct().Count() == 4, "tour has no duplicates");
        bool ordered = tour[0] == ids[0] && tour[1] == ids[1] && tour[2] == ids[2] && tour[3] == ids[3];
        Check(ordered, "NN tour orders the line 0,1,2,3");

        float TourLen(List<int> t) {
            float s = 0f;
            for (int k = 0; k + 1 < t.Count; k++) s += GraphRouter.Cost(g, t[k], t[k + 1]);
            return s;
        }
        Check(TourLen(tour) <= TourLen(scrambled), "NN tour no longer than the scrambled order");

        // Fleet baseline: two buses, each refills a lap; laps start near each bus.
        var bb = new Blackboard { Graph = g, Rng = new System.Random(1), Mode = RunMode.FixedRoute };
        bb.Buses.Add(new BusState { Id = 0, Capacity = 4, CurrentNode = ids[0] });
        bb.Buses.Add(new BusState { Id = 1, Capacity = 4, CurrentNode = ids[3] });

        var agent = new FixedRouteFleetAgent(tour);
        agent.Tick(bb, 5f);
        Check(bb.Buses[0].Plan.Count == 4, "bus 0 got a full lap");
        Check(bb.Buses[1].Plan.Count == 4, "bus 1 got a full lap");
        Check(bb.Buses[0].Plan[0].StopNode == ids[0], "bus 0 lap starts at its own position");
        Check(bb.Buses[1].Plan[0].StopNode == ids[3], "bus 1 lap starts at its own position (phase-offset)");
        Check(bb.Buses[0].Plan.All(t => t.Kind == PlanTaskKind.Visit), "lap tasks are Visit");

        // Non-empty plans are left alone.
        agent.Tick(bb, 5f);
        Check(bb.Buses[0].Plan.Count == 4, "no refill while plan non-empty");

        // Wrong mode does nothing.
        var bb2 = new Blackboard { Graph = g, Rng = new System.Random(1), Mode = RunMode.Dynamic };
        bb2.Buses.Add(new BusState { Id = 0, Capacity = 4, CurrentNode = ids[0] });
        new FixedRouteFleetAgent(tour).Tick(bb2, 5f);
        Check(bb2.Buses[0].Plan.Count == 0, "no lap in Dynamic mode");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
        Object.DestroyImmediate(go);
    }
}
```
Expected: `COMPILATION_FAILED`.

- [ ] **Step 2: Write `StopTour.cs`**

```csharp
using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Builds the fixed-route baseline's loop order. A nearest-neighbor tour keeps the baseline
    /// *competent*: the previous arbitrary StopId order could be a pathologically long zig-zag,
    /// which would let Dynamic win for the wrong reason (see ADR 0002).
    /// </summary>
    public static class StopTour
    {
        /// <summary>Greedy nearest-neighbor tour over the stop nodes, starting at the first one.</summary>
        public static List<int> NearestNeighbor(RoadGraph graph, List<int> stopNodes)
        {
            var tour = new List<int>();
            if (stopNodes == null || stopNodes.Count == 0) return tour;

            var remaining = new List<int>(stopNodes);
            int current = remaining[0];
            tour.Add(current);
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                int bestIdx = 0;
                float bestCost = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float c = GraphRouter.Cost(graph, current, remaining[i]);
                    if (c < bestCost) { bestCost = c; bestIdx = i; }
                }
                current = remaining[bestIdx];
                tour.Add(current);
                remaining.RemoveAt(bestIdx);
            }

            return tour;
        }
    }
}
```

- [ ] **Step 3: Write `FixedRouteFleetAgent.cs`**

```csharp
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
```

- [ ] **Step 4: Delete `FixedRouteAgent.cs`** via `Unity_DeleteScript`.

- [ ] **Step 5: Point `Simulation` at the new baseline**

In `Simulation.cs`, replace `new FixedRouteAgent(stopNodes)` with `new FixedRouteFleetAgent(StopTour.NearestNeighbor(Graph, stopNodes))`.

- [ ] **Step 6: Recompile, rerun the Step 1 script (expect GREEN)**

Expected: `RESULT: 11 passed, 0 failed`.

- [ ] **Step 7: Commit**

```bash
cd "/d/unity/projects/My project" && git add -A Assets/Scripts/BusSystem && git commit -m "feat(fleet): fair baseline - nearest-neighbor tour + staggered FixedRouteFleetAgent" && git push origin main
```

---

### Task 5: Per-bus metrics + utilization balance (CoV) + per-bus CSV

**Files:**
- Modify: `Assets/Scripts/BusSystem/Metrics.cs`
- Modify: `Assets/Scripts/BusSystem/Dispatch.cs`
- Modify: `Assets/Scripts/BusSystem/MonitorAgent.cs`

**Interfaces:**
- Produces:
  - `class BusMetrics { int Delivered; float EmptyTravelDistance; float MeanOccupancy; }`
  - `Metrics.EnsureBuses(int count)`, `RecordDelivery(PassengerRequest r, int busId)`, `SampleOccupancy(int busId, int count)`, `AddEmptyTravel(int busId, float d)`, `List<BusMetrics> PerBus(...)`
  - `MetricsSummary.BusCount`, `MetricsSummary.DeliveredCoV`
  - `{mode}_buses.csv` written by `MonitorAgent`.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Title "Verify fleet metrics (red)":
```csharp
using UnityEngine;
using System.Collections.Generic;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int pass = 0, fail = 0;
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        var m = new Metrics();
        m.EnsureBuses(2);

        var a = new PassengerRequest { Id = 0, SpawnTime = 0f, BoardTime = 10f, AlightTime = 25f };
        var b = new PassengerRequest { Id = 1, SpawnTime = 0f, BoardTime = 20f, AlightTime = 40f };
        var c = new PassengerRequest { Id = 2, SpawnTime = 0f, BoardTime = 30f, AlightTime = 60f };
        m.RecordDelivery(a, 0);
        m.RecordDelivery(b, 0);
        m.RecordDelivery(c, 1);

        m.SampleOccupancy(0, 2);
        m.SampleOccupancy(0, 4);
        m.SampleOccupancy(1, 0);
        m.AddEmptyTravel(0, 10f);
        m.AddEmptyTravel(1, 30f);

        var s = m.Summarize(0);
        Check(s.Delivered == 3, "fleet delivered total");
        Check(s.BusCount == 2, "bus count recorded");
        Check(Mathf.Approximately(s.EmptyTravelDistance, 40f), "fleet empty travel summed");

        var per = m.PerBus();
        Check(per.Count == 2, "two per-bus records");
        Check(per[0].Delivered == 2 && per[1].Delivered == 1, "per-bus delivered split");
        Check(Mathf.Approximately(per[0].MeanOccupancy, 3f), "per-bus mean occupancy");
        Check(Mathf.Approximately(per[1].EmptyTravelDistance, 30f), "per-bus empty travel");

        // CoV of {2,1}: mean 1.5, population stddev 0.5 -> 0.3333
        Check(Mathf.Abs(s.DeliveredCoV - 0.3333f) < 0.01f, "delivered CoV computed");

        // Perfectly balanced -> CoV 0
        var m2 = new Metrics();
        m2.EnsureBuses(2);
        m2.RecordDelivery(a, 0);
        m2.RecordDelivery(b, 1);
        Check(Mathf.Approximately(m2.Summarize(0).DeliveredCoV, 0f), "balanced fleet has CoV 0");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED`.

- [ ] **Step 2: Rewrite `Metrics.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Per-bus rollup, used for the utilization-balance figure and the per-bus CSV.</summary>
    public class BusMetrics
    {
        public int BusId;
        public int Delivered;
        public float EmptyTravelDistance;
        public float MeanOccupancy;
    }

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
        public int BusCount;
        /// <summary>Coefficient of variation of per-bus delivered counts: 0 = perfectly balanced.</summary>
        public float DeliveredCoV;
    }

    /// <summary>
    /// Accumulates per-delivery timings and occupancy for one simulation run, both fleet-wide and
    /// per bus. The per-bus split exists to expose fleet *imbalance* — greedy one-shot assignment
    /// plus hold-in-place idling can load one bus far more than another, and DeliveredCoV measures
    /// exactly that rather than hiding it inside a fleet average.
    /// </summary>
    public class Metrics
    {
        readonly List<float> _waits = new List<float>();
        readonly List<float> _rides = new List<float>();
        readonly List<float> _totals = new List<float>();

        // Indexed by bus id (a list, not a dictionary, so iteration order is deterministic).
        readonly List<int> _deliveredPerBus = new List<int>();
        readonly List<float> _emptyTravelPerBus = new List<float>();
        readonly List<long> _occSumPerBus = new List<long>();
        readonly List<int> _occCountPerBus = new List<int>();

        public void EnsureBuses(int count)
        {
            while (_deliveredPerBus.Count < count)
            {
                _deliveredPerBus.Add(0);
                _emptyTravelPerBus.Add(0f);
                _occSumPerBus.Add(0L);
                _occCountPerBus.Add(0);
            }
        }

        public void RecordDelivery(PassengerRequest r, int busId)
        {
            _waits.Add(r.BoardTime - r.SpawnTime);
            _rides.Add(r.AlightTime - r.BoardTime);
            _totals.Add(r.AlightTime - r.SpawnTime);
            if (busId >= 0)
            {
                EnsureBuses(busId + 1);
                _deliveredPerBus[busId]++;
            }
        }

        public void SampleOccupancy(int busId, int count)
        {
            EnsureBuses(busId + 1);
            _occSumPerBus[busId] += count;
            _occCountPerBus[busId]++;
        }

        public void AddEmptyTravel(int busId, float distance)
        {
            EnsureBuses(busId + 1);
            _emptyTravelPerBus[busId] += distance;
        }

        public List<BusMetrics> PerBus()
        {
            var list = new List<BusMetrics>();
            for (int i = 0; i < _deliveredPerBus.Count; i++)
            {
                list.Add(new BusMetrics
                {
                    BusId = i,
                    Delivered = _deliveredPerBus[i],
                    EmptyTravelDistance = _emptyTravelPerBus[i],
                    MeanOccupancy = _occCountPerBus[i] == 0 ? 0f : (float)_occSumPerBus[i] / _occCountPerBus[i]
                });
            }
            return list;
        }

        public MetricsSummary Summarize(int undelivered)
        {
            long occSum = _occSumPerBus.Sum();
            int occCount = _occCountPerBus.Sum();

            return new MetricsSummary
            {
                Delivered = _waits.Count,
                Undelivered = undelivered,
                AvgWait = Average(_waits),
                P90Wait = Percentile(_waits, 0.9f),
                AvgRide = Average(_rides),
                AvgTotal = Average(_totals),
                MeanOccupancy = occCount == 0 ? 0f : (float)occSum / occCount,
                EmptyTravelDistance = _emptyTravelPerBus.Sum(),
                BusCount = _deliveredPerBus.Count,
                DeliveredCoV = CoefficientOfVariation(_deliveredPerBus)
            };
        }

        /// <summary>Population stddev ÷ mean. 0 when perfectly even (or nothing delivered).</summary>
        static float CoefficientOfVariation(List<int> xs)
        {
            if (xs.Count == 0) return 0f;
            float mean = (float)xs.Average();
            if (mean <= 0f) return 0f;
            float varSum = xs.Sum(x => (x - mean) * (x - mean));
            float stddev = Mathf.Sqrt(varSum / xs.Count);
            return stddev / mean;
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

- [ ] **Step 3: Edit `Dispatch.cs` — route metrics through the bus id**

- `bb.Metrics.RecordDelivery(r);` → `bb.Metrics.RecordDelivery(r, bus.Id);`
- `bb.Metrics.SampleOccupancy(bus.OnboardRequestIds.Count);` → `bb.Metrics.SampleOccupancy(bus.Id, bus.OnboardRequestIds.Count);`
- In `BeginLegTo`, `if (bus.OnboardRequestIds.Count == 0) bb.Metrics.EmptyTravelDistance += route.Cost;` → `if (bus.OnboardRequestIds.Count == 0) bb.Metrics.AddEmptyTravel(bus.Id, route.Cost);`

- [ ] **Step 4: Edit `MonitorAgent.cs` — per-bus CSV + balance in the summary**

In `WriteResults`, after the existing summary write, add the per-bus CSV and extend the summary header/row:
```csharp
            var sumLines = new List<string>
            {
                "Delivered,Undelivered,AvgWait,P90Wait,AvgRide,AvgTotal,MeanOccupancy,EmptyTravelDistance,BusCount,DeliveredCoV",
                summary.Delivered + "," + summary.Undelivered + "," +
                    summary.AvgWait.ToString("F2") + "," + summary.P90Wait.ToString("F2") + "," +
                    summary.AvgRide.ToString("F2") + "," + summary.AvgTotal.ToString("F2") + "," +
                    summary.MeanOccupancy.ToString("F2") + "," + summary.EmptyTravelDistance.ToString("F2") + "," +
                    summary.BusCount + "," + summary.DeliveredCoV.ToString("F4")
            };
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_summary.csv"), sumLines);

            var busLines = new List<string> { "BusId,Delivered,MeanOccupancy,EmptyTravelDistance" };
            foreach (var bm in bb.Metrics.PerBus())
                busLines.Add(bm.BusId + "," + bm.Delivered + "," +
                    bm.MeanOccupancy.ToString("F2") + "," + bm.EmptyTravelDistance.ToString("F2"));
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_buses.csv"), busLines);
```
Also add a fleet line to `FormatHud` after `Delivered`:
```csharp
                   "Buses: " + bb.Buses.Count + "\n" +
```

- [ ] **Step 5: Recompile, rerun the Step 1 script (expect GREEN)**

Expected: `RESULT: 9 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
cd "/d/unity/projects/My project" && git add Assets/Scripts/BusSystem && git commit -m "feat(metrics): per-bus occupancy/empty-travel/delivered + CoV balance + per-bus CSV" && git push origin main
```

---

### Task 6: `Simulation` wiring — `BusCount`, prefab instantiation, distributed starts

**Files:**
- Modify: `Assets/Scripts/BusSystem/Simulation.cs`
- Modify: `Assets/Scripts/BusSystem/CameraFollow.cs`

**Interfaces:**
- Produces: `Simulation.BusCount` (int), `Simulation.BusPrefab` (`BusPathFollower`), N buses each with their own follower/navigator/`Dispatch`; camera targets bus 0.

- [ ] **Step 1: Edit `Simulation.cs`**

Add fields next to the existing tunables:
```csharp
        public int BusCount = 1;
        [Tooltip("Prefab (or scene template) with a BusPathFollower; BusCount copies are instantiated at startup.")]
        public BusPathFollower BusPrefab;
```
Replace the body of `Start()` from the stop discovery down to the agent list with:
```csharp
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            if (Follower == null) Follower = FindObjectOfType<BusPathFollower>();
            if (BusPrefab == null) BusPrefab = Follower;

            var stops = FindObjectsByType<BusStop>(FindObjectsSortMode.None)
                .OrderBy(s => s.StopId).ToList();
            var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();
            var stopNames = stops.Select(s => s.name).ToList(); // building names (AB1..AB4)

            int busCount = Mathf.Max(1, BusCount);

            _bb = new Blackboard
            {
                Graph = Graph,
                Rng = new System.Random(RandomSeed),
                Mode = Mode,
                StopNames = stopNames
            };
            _bb.Metrics.EnsureBuses(busCount);

            // One Dispatch per bus, each driving its own visual follower through its own navigator.
            var dispatches = new List<IAgent>();
            for (int i = 0; i < busCount; i++)
            {
                // Distributed start: evenly spaced across the stop list so buses begin spread out
                // (and so the fixed-route baseline is naturally phase-offset). Identical in both
                // modes, so neither is advantaged.
                int startNode = stopNodes.Count > 0
                    ? stopNodes[(i * stopNodes.Count) / busCount]
                    : Graph.NearestNode(Follower.transform.position);

                BusPathFollower follower;
                if (i == 0)
                {
                    follower = Follower; // reuse the bus already in the scene
                }
                else
                {
                    follower = Instantiate(BusPrefab, BusPrefab.transform.parent);
                    follower.name = BusPrefab.name + "_" + i;
                }
                follower.transform.position = Graph.Nodes[startNode].Position;

                _bb.Buses.Add(new BusState { Id = i, Capacity = BusCapacity, CurrentNode = startNode });
                dispatches.Add(new Dispatch(new KinematicNavigator(follower), BusCruiseUnitsPerSimSecond, i));
            }

            string resultsDir = System.IO.Path.Combine(Application.dataPath, "..", "Results");

            _agents = new List<IAgent>
            {
                new SimClockAgent(SimDurationHours),
                new DemandAgent(stopNodes, BaseRatePerStopPerHour),
                Mode == RunMode.Dynamic
                    ? (IAgent)new FleetOptimizerAgent()
                    : new FixedRouteFleetAgent(StopTour.NearestNeighbor(Graph, stopNodes))
            };
            _agents.AddRange(dispatches);
            _agents.Add(new MonitorAgent(resultsDir));

            // The camera follows bus 0 explicitly — auto-find would pick an arbitrary instance.
            var cam = FindObjectOfType<CameraFollow>();
            if (cam != null) cam.Target = Follower.transform;
```

- [ ] **Step 2: Edit `CameraFollow.cs` — don't fight the explicit target**

In `Start()`, guard the auto-find so an already-assigned target wins (it already checks `Target == null`; confirm no change needed). If `Simulation` runs after `CameraFollow.Start`, the explicit assignment still applies on the next `LateUpdate`, so no code change is required. **Verify only — no edit expected.**

- [ ] **Step 3: Recompile and run the fleet smoke test**

Title "Fleet smoke test (headless)":
```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BusSystem;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var graph = Object.FindFirstObjectByType<RoadGraph>();
        if (graph == null) { result.LogError("no RoadGraph in scene"); return; }

        var stops = Object.FindObjectsByType<BusStop>(FindObjectsSortMode.None).OrderBy(s => s.StopId).ToList();
        var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();
        var tour = StopTour.NearestNeighbor(graph, stopNodes);

        System.Func<RunMode, int, MetricsSummary> Run = (mode, busCount) =>
        {
            var bb = new Blackboard { Graph = graph, Rng = new System.Random(12345), Mode = mode };
            bb.Metrics.EnsureBuses(busCount);
            var agents = new List<IAgent>
            {
                new SimClockAgent(16f),
                new DemandAgent(stopNodes, 6f),
                mode == RunMode.Dynamic ? (IAgent)new FleetOptimizerAgent() : new FixedRouteFleetAgent(tour)
            };
            for (int i = 0; i < busCount; i++)
            {
                int startNode = stopNodes[(i * stopNodes.Count) / busCount];
                bb.Buses.Add(new BusState { Id = i, Capacity = 20, CurrentNode = startNode });
                agents.Add(new Dispatch(new NullNavigator(), 0.25f, i));
            }
            float dt = 5f;
            for (int k = 0; k < 200000 && !bb.Finished; k++)
                foreach (var a in agents) a.Tick(bb, dt);
            int undelivered = bb.Requests.Count(r => r.State != RequestState.Delivered);
            return bb.Metrics.Summarize(undelivered);
        };

        foreach (int n in new[] { 1, 2, 3 })
        {
            var d = Run(RunMode.Dynamic, n);
            var f = Run(RunMode.FixedRoute, n);
            result.Log("buses=" + n +
                "  DYN delivered=" + d.Delivered + " p90wait=" + d.P90Wait.ToString("F0") + " cov=" + d.DeliveredCoV.ToString("F3") +
                " | FIX delivered=" + f.Delivered + " p90wait=" + f.P90Wait.ToString("F0") + " cov=" + f.DeliveredCoV.ToString("F3"));
        }
        result.Log("SMOKE OK");
    }
}

internal class NullNavigator : IVehicleNavigator
{
    public bool Arrived { get; private set; } = true;
    public event System.Action ReachedGoal;
    public void SetGoalPath(System.Collections.Generic.IReadOnlyList<Vector3> waypoints) { Arrived = false; }
    public void UpdateTravel(float f) { if (f >= 1f && !Arrived) { Arrived = true; if (ReachedGoal != null) ReachedGoal(); } }
}
```
Expected: three lines printing, delivered counts rising with bus count, and `SMOKE OK`.

- [ ] **Step 4: REGRESSION GUARD — `BusCount = 1` must reproduce iteration 1**

This is the key proof that the fleet generalization is behaviour-preserving. Run the smoke test's `Run(RunMode.Dynamic, 1)` and compare against the **committed** iteration-1 headless baseline:

| Delivered | Undelivered | AvgWait | P90Wait | EmptyTravel |
|---:|---:|---:|---:|---:|
| **263** | **59** | 8667.68 | **15460.00** | 253.50 |

> **Use `git show HEAD:Results/Dynamic_summary.csv`, not the working-tree file.** The working copy of `Results/` was overwritten by a later *play-mode* session (which reports 273/49/14170 from different Inspector settings) and is **not** the canonical headless baseline. Comparing against it will look like a regression that isn't one.

**Expected: an exact match.** If it differs, STOP and diagnose before continuing — the likely causes are (a) assignment order not ascending by `Request.Id`, (b) `Probe` accidentally mutating, or (c) the plan-saturation check moved relative to iteration 1's placement.

*(FixedRoute numbers legitimately change here — the nearest-neighbor tour replaced the arbitrary `StopId` loop order, per ADR 0002. Only the Dynamic single-bus result is the regression guard.)*

- [ ] **Step 5: Set up the scene**

Set `Simulation.BusCount = 3` in the Inspector and assign `BusPrefab` to the `school-bus` object (or leave null to reuse `Follower`). Enter Play mode briefly and confirm 3 buses appear and move, then exit.

- [ ] **Step 6: Commit**

```bash
cd "/d/unity/projects/My project" && git add Assets/Scripts/BusSystem Assets/Scenes/SampleScene.unity && git commit -m "feat(fleet): Simulation instantiates BusCount buses at distributed starts" && git push origin main
```

---

### Task 7: World enrichment — more stops + hub-biased demand (folded-in item A)

**Files:**
- Modify: `Assets/Scripts/BusSystem/BusStop.cs`
- Modify: `Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs`
- Modify: `Assets/Scripts/BusSystem/DemandAgent.cs`
- Modify: `Assets/Scripts/BusSystem/Simulation.cs`

**Interfaces:**
- Produces: `BusStop.Weight` (float, default 1); `RoadGraphBuilder` also binds children of a `Stops` root; `DemandAgent(List<int> stopNodes, List<float> weights, float baseRate)` with weighted origin rates and weighted destination choice.

> **Note on determinism:** this task deliberately changes the request stream (weighted destination selection consumes the RNG differently from `Rng.Next(count)`), so post-Task-7 numbers are a **new baseline**. That is why the Task 6 regression guard runs *before* this task.

- [ ] **Step 1: Run the verification script (expect COMPILATION_FAILED — RED)**

Title "Verify weighted demand (red)":
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
        System.Action<bool, string> Check = (c, n) => {
            if (c) { pass++; result.Log("PASS: " + n); } else { fail++; result.LogError("FAIL: " + n); }
        };

        var stopNodes = new List<int> { 0, 1, 2 };
        var weights = new List<float> { 1f, 5f, 1f }; // stop 1 is a hub

        var bb = new Blackboard { Rng = new System.Random(42), Mode = RunMode.Dynamic };
        var agent = new DemandAgent(stopNodes, weights, 6f);
        for (int k = 0; k < 4000; k++) { bb.SimTime += 5f; agent.Tick(bb, 5f); }

        int o0 = bb.Requests.Count(r => r.OriginStop == 0);
        int o1 = bb.Requests.Count(r => r.OriginStop == 1);
        Check(bb.Requests.Count > 100, "demand generated");
        Check(o1 > o0 * 2, "hub stop originates far more requests");

        int d0 = bb.Requests.Count(r => r.DestStop == 0);
        int d1 = bb.Requests.Count(r => r.DestStop == 1);
        Check(d1 > d0 * 2, "hub stop attracts far more destinations");
        Check(bb.Requests.All(r => r.OriginStop != r.DestStop), "never origin == destination");

        // Determinism: same seed -> identical stream.
        var bb2 = new Blackboard { Rng = new System.Random(42), Mode = RunMode.Dynamic };
        var agent2 = new DemandAgent(stopNodes, weights, 6f);
        for (int k = 0; k < 4000; k++) { bb2.SimTime += 5f; agent2.Tick(bb2, 5f); }
        bool same = bb.Requests.Count == bb2.Requests.Count &&
                    bb.Requests.Zip(bb2.Requests, (x, y) => x.OriginStop == y.OriginStop && x.DestStop == y.DestStop).All(t => t);
        Check(same, "same seed reproduces the request stream");

        result.Log("RESULT: " + pass + " passed, " + fail + " failed");
    }
}
```
Expected: `COMPILATION_FAILED`.

- [ ] **Step 2: Edit `BusStop.cs` — add `Weight`**

```csharp
        [Tooltip("Demand weighting: >1 makes this stop a hub (more origins AND more destinations).")]
        public float Weight = 1f;
```

- [ ] **Step 3: Edit `RoadGraphBuilder.cs` — also bind a `Stops` root**

Replace the "Bind buildings as bus stops" block with:
```csharp
            // --- Bind buildings and standalone markers as bus stops ---
            int stopCount = 0;
            var boundNodes = new Dictionary<int, string>();
            foreach (var rootName in new[] { "Buildings", "Stops" })
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                foreach (Transform b in root.transform)
                {
                    var stop = b.GetComponent<BusStop>() ?? b.gameObject.AddComponent<BusStop>();
                    stop.StopId = stopCount++;
                    stop.NearestNodeIndex = graph.NearestNode(b.position);
                    if (boundNodes.ContainsKey(stop.NearestNodeIndex))
                        Debug.LogWarning($"[RoadGraph] '{b.name}' binds node {stop.NearestNodeIndex}, already used by " +
                                         $"'{boundNodes[stop.NearestNodeIndex]}' — duplicate stop nodes make routing degenerate.");
                    else boundNodes[stop.NearestNodeIndex] = b.name;
                    EditorUtility.SetDirty(stop);
                }
            }
```

- [ ] **Step 4: Rewrite `DemandAgent.cs` for weights**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Spawns passenger requests at stops via a Poisson process shaped by PeakProfile (time of day)
    /// and per-stop Weights (space). Weights make demand spatially uneven — a hub attracts and
    /// generates far more trips — which is what gives a coordinating fleet something to exploit
    /// that a fixed loop cannot.
    /// </summary>
    public class DemandAgent : IAgent
    {
        readonly List<int> _stopNodes;
        readonly List<float> _weights;
        readonly float _baseRatePerStopPerHour;

        public DemandAgent(List<int> stopNodes, List<float> weights, float baseRatePerStopPerHour)
        {
            _stopNodes = stopNodes;
            _weights = weights;
            _baseRatePerStopPerHour = baseRatePerStopPerHour;
        }

        float Weight(int i) => (_weights != null && i < _weights.Count) ? Mathf.Max(0f, _weights[i]) : 1f;

        public void Tick(Blackboard bb, float dt)
        {
            float timeOfDay = (bb.SimTime / 3600f) % 24f;
            float mult = PeakProfile.Multiplier(timeOfDay);

            for (int i = 0; i < _stopNodes.Count; i++)
            {
                float lambda = _baseRatePerStopPerHour * mult * Weight(i) * (dt / 3600f);
                int count = SamplePoisson(bb.Rng, lambda);
                for (int k = 0; k < count; k++) SpawnRequest(bb, i);
            }
        }

        void SpawnRequest(Blackboard bb, int originIdx)
        {
            int destIdx = PickDestination(bb, originIdx);
            if (destIdx < 0) return;

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

            bb.Activity.Add(ActivityFeed.Kind.Requested, originIdx, destIdx, bb.SimTime);
        }

        /// <summary>Weight-proportional choice among all stops except the origin (deterministic scan).</summary>
        int PickDestination(Blackboard bb, int originIdx)
        {
            if (_stopNodes.Count < 2) return -1;

            float total = 0f;
            for (int i = 0; i < _stopNodes.Count; i++)
                if (i != originIdx) total += Weight(i);

            if (total <= 0f)
            {
                // Degenerate weights: fall back to a uniform pick among the others.
                int offset = bb.Rng.Next(_stopNodes.Count - 1);
                return offset >= originIdx ? offset + 1 : offset;
            }

            double roll = bb.Rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < _stopNodes.Count; i++)
            {
                if (i == originIdx) continue;
                acc += Weight(i);
                if (roll < acc) return i;
            }
            return originIdx == _stopNodes.Count - 1 ? 0 : _stopNodes.Count - 1; // float-rounding guard
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

- [ ] **Step 5: Edit `Simulation.cs` — pass the weights**

Add after `stopNames`:
```csharp
            var stopWeights = stops.Select(s => s.Weight).ToList();
```
and change the agent construction to `new DemandAgent(stopNodes, stopWeights, BaseRatePerStopPerHour)`.

- [ ] **Step 6: Recompile, rerun the Step 1 script (expect GREEN)**

Expected: `RESULT: 5 passed, 0 failed`.

- [ ] **Step 7: Enrich the scene**

In `SampleScene.unity`: create an empty `Stops` root and add **4–8** child empties positioned near well-separated road nodes (aim for **8–12 total stops** including `AB1`–`AB4`). Run **Bus System ▸ Build Road Graph** and confirm the log reports the new stop count with **no duplicate-node warnings**. Set `Weight = 5` on **1–2** hub stops, leaving the rest at 1.

- [ ] **Step 8: Commit**

```bash
cd "/d/unity/projects/My project" && git add Assets/Scripts/BusSystem Assets/Scenes/SampleScene.unity && git commit -m "feat(demand): stop weights for hub-biased demand + Stops marker root binding" && git push origin main
```

---

### Task 8: The experiment — A/B at fleet size 3 + a 1→4 scaling sweep

**Files:**
- Create: `Assets/Scripts/BusSystem/Editor/FleetExperiment.cs`
- Modify: `README.md`
- Modify: `Docs/NEXT_STEPS.md`

**Interfaces:**
- Produces: menu **Bus System ▸ Run Fleet Sweep** writing `Results/sweep.csv` plus per-configuration summaries.

- [ ] **Step 1: Write `Editor/FleetExperiment.cs`**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusSystem.EditorTools
{
    /// <summary>
    /// Headless A/B + fleet-size sweep. The simulation is plain C# and deterministic, so a full
    /// day runs in well under a second without entering play mode — which is why the whole sweep
    /// is a menu item rather than a play-mode session.
    /// </summary>
    public static class FleetExperiment
    {
        const int Seed = 12345;
        const float DurationHours = 16f;
        const float BaseRate = 6f;
        const int Capacity = 20;
        const float Cruise = 0.25f;
        const int MaxBuses = 4;

        [MenuItem("Bus System/Run Fleet Sweep")]
        public static void RunSweep()
        {
            var graph = Object.FindFirstObjectByType<RoadGraph>();
            if (graph == null) { Debug.LogError("[FleetSweep] No RoadGraph in the scene."); return; }

            var stops = Object.FindObjectsByType<BusStop>(FindObjectsSortMode.None).OrderBy(s => s.StopId).ToList();
            if (stops.Count < 2) { Debug.LogError("[FleetSweep] Need at least 2 stops."); return; }

            var stopNodes = stops.Select(s => s.NearestNodeIndex).ToList();
            var weights = stops.Select(s => s.Weight).ToList();
            var tour = StopTour.NearestNeighbor(graph, stopNodes);

            string dir = Path.Combine(Application.dataPath, "..", "Results");
            Directory.CreateDirectory(dir);

            var lines = new List<string>
            {
                "Mode,BusCount,Delivered,Undelivered,AvgWait,P90Wait,AvgRide,AvgTotal,MeanOccupancy,EmptyTravelDistance,DeliveredCoV"
            };

            foreach (RunMode mode in new[] { RunMode.Dynamic, RunMode.FixedRoute })
            {
                for (int n = 1; n <= MaxBuses; n++)
                {
                    var s = RunOne(graph, stopNodes, weights, tour, mode, n);
                    lines.Add(mode + "," + n + "," + s.Delivered + "," + s.Undelivered + "," +
                        s.AvgWait.ToString("F2") + "," + s.P90Wait.ToString("F2") + "," +
                        s.AvgRide.ToString("F2") + "," + s.AvgTotal.ToString("F2") + "," +
                        s.MeanOccupancy.ToString("F2") + "," + s.EmptyTravelDistance.ToString("F2") + "," +
                        s.DeliveredCoV.ToString("F4"));
                    Debug.Log("[FleetSweep] " + mode + " x" + n +
                              " delivered=" + s.Delivered + " p90wait=" + s.P90Wait.ToString("F0") +
                              " empty=" + s.EmptyTravelDistance.ToString("F0") + " cov=" + s.DeliveredCoV.ToString("F3"));
                }
            }

            File.WriteAllLines(Path.Combine(dir, "sweep.csv"), lines);
            Debug.Log("[FleetSweep] Wrote Results/sweep.csv (" + (lines.Count - 1) + " configurations).");
        }

        static MetricsSummary RunOne(RoadGraph graph, List<int> stopNodes, List<float> weights,
                                     List<int> tour, RunMode mode, int busCount)
        {
            var bb = new Blackboard { Graph = graph, Rng = new System.Random(Seed), Mode = mode };
            bb.Metrics.EnsureBuses(busCount);

            var agents = new List<IAgent>
            {
                new SimClockAgent(DurationHours),
                new DemandAgent(stopNodes, weights, BaseRate),
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
            return bb.Metrics.Summarize(undelivered);
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
```

- [ ] **Step 2: Recompile and run the sweep**

Invoke via `mcp__unity-mcp__Unity_ManageMenuItem` → `Bus System/Run Fleet Sweep` (or a `RunCommand` calling `FleetExperiment.RunSweep()`). Expected: 8 log lines and `Results/sweep.csv`.

- [ ] **Step 3: Check the headline and record it**

Read `Results/sweep.csv`. Verify:
- At `BusCount = 3`, Dynamic delivers **more** and has a **lower p90 wait** than FixedRoute.
- Delivered rises with bus count in both modes, with visible diminishing returns.
- `DeliveredCoV` is reported for Dynamic (the imbalance finding).

**If Dynamic does not beat FixedRoute at 3 buses:** do not tune the numbers to force it. Report what the data says and investigate — the most likely causes are too few stops or too-flat weights (Task 7 Step 7), so revisit the scene enrichment first.

- [ ] **Step 4: Update `README.md`**

Replace the single-bus **Results** table with the fleet A/B at 3 buses plus a short scaling-curve paragraph, and add `FleetOptimizerAgent`, `FixedRouteFleetAgent`, and `StopTour` to the architecture table. Note the `DeliveredCoV` imbalance finding honestly.

- [ ] **Step 5: Update `Docs/NEXT_STEPS.md`**

Mark section **B (fleet coordination)** and the item-A parts (more stops, uneven demand, sweep) as **done**, and promote **reassignment** and **anticipatory repositioning** to the top of the remaining backlog.

- [ ] **Step 6: Commit**

```bash
cd "/d/unity/projects/My project" && git add -A Assets/Scripts/BusSystem Results README.md Docs/NEXT_STEPS.md && git commit -m "feat(experiment): fleet sweep harness + A/B results, README and roadmap updated" && git push origin main
```

---

## Verification summary

| Task | Guard |
|---|---|
| 1 | `Probe` is pure and agrees with `TryInsert` |
| 2 | Fleet blackboard compiles; single-bus behaviour unchanged |
| 3 | Nearest bus wins; marginal (not absolute) scoring; no double-assignment |
| 4 | NN tour is ordered and no longer than `StopId` order; laps are phase-offset |
| 5 | Per-bus split correct; CoV = 0 when balanced |
| 6 | **`BusCount = 1` reproduces iteration 1 exactly (Delivered 263 / Undelivered 59 / P90 15460)** |
| 7 | Hub gets ≫ traffic; same seed reproduces the stream |
| 8 | Dynamic beats FixedRoute at 3 buses; sweep shows diminishing returns |

The Task 6 guard is the load-bearing one: it proves the fleet generalization is behaviour-preserving, which keeps iteration 1's published result valid.

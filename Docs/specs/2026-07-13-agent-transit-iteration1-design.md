# Agent-Based Campus Transit — Iteration 1 Design Spec

**Date:** 2026-07-13
**Status:** Approved (design), pending implementation plan
**Builds on:** the road-exploration foundation (`RoadGraph`, `BusStop`, `BusPathFollower`,
`BusAgent`, `IBusController`) — see `2026-07-12-bus-road-exploration-design.md`.
**Scene:** `Assets/Scenes/SampleScene.unity` · **Unity:** 6000.5.3f1 · Built-in RP.

## Vision & program decomposition

Long-term goal: an **Agent-Based Autonomous Campus Transit System** in a 3D digital
twin — intelligent agents doing demand forecasting, route optimization, fleet
coordination, and monitoring, using graph-based planning and capacity-aware
scheduling. The program decomposes into sub-projects, each its own spec→plan→build:

1. **Demand model** · 2. **Capacity-aware routing (single bus)** · 3. **Metrics & monitoring**
· 4. Fleet coordination · 5. Demand forecasting · 6. Environment/congestion ·
*(future: energy/terrain, collision, sensor-based autonomous navigation)*.

**This spec covers iteration 1 = sub-projects 1 + 2 + 3**: the smallest slice that both
*demonstrates* and *measures* "path optimization based on load."

## Goal

One bus serves passengers who appear at stops with destinations. A route-optimization
agent dynamically routes it (capacity-aware) to minimize passenger wait and travel
time, and we **measure it against a fixed-route baseline** under identical demand.

## Non-goals (iteration 1 / YAGNI)

- No multiple buses / fleet coordination (sub-project 4).
- No demand *forecasting* (sub-project 5) — demand is generated, not predicted.
- No physics or sensor simulation (LiDAR/proximity/collision) — that is stage 2,
  isolated behind the `IVehicleNavigator` seam (see §8).
- No individual passenger meshes — lightweight labels only.
- No congestion/dynamic edge costs yet (sub-project 6).

## 1. Architecture — agents over a blackboard

A single **`Simulation`** `MonoBehaviour` owns a **`Blackboard`** and advances the world on
a **fixed simulation timestep** (accumulator, default 0.1 sim-s) scaled by a
time-compression factor (sim-seconds per real-second) so a campus "day" plays in
minutes. On each step it calls every agent's `Tick(Blackboard, dt)` in a fixed order.

Agents are **plain C# classes** (not MonoBehaviours) implementing:
```
interface IAgent { void Tick(Blackboard bb, float dt); }   // perceive → decide → act
```
This keeps them deterministic and unit-testable. Fixed step order:
`SimClock → DemandAgent → RouteOptimizerAgent → Dispatch → MonitorAgent`.

The existing `RoadGraph` / `BusStop` / `BusPathFollower` become the physical layer the
agents command through the navigator seam. **`BusAgent`'s orchestration role is
superseded** by `Simulation` + `Dispatch`, and `BusPathFollower` is now driven via
`KinematicNavigator` (§8) rather than directly. `ExploreAllController` / `IBusController`
from iteration 0 are retained only as an optional "free-explore" demo mode, not part of
the graded A/B.

## 2. Blackboard / data model

```
class PassengerRequest {
    int Id; int OriginStop; int DestStop;
    float SpawnTime; float BoardTime = -1; float AlightTime = -1;
    enum State { Waiting, OnBoard, Delivered }
}
class BusState { int CurrentNode; int Capacity; List<int> Onboard; List<Task> Plan; }
class Task { enum Kind { Pickup, Dropoff } int RequestId; int StopNode; }  // node of the stop

class Blackboard {
    float SimTime;
    System.Random Rng;                 // single seeded source
    List<PassengerRequest> Requests;   // active (Waiting/OnBoard)
    Dictionary<int,List<int>> WaitingByStop;
    BusState Bus;
    Metrics Metrics;
    RunMode Mode;                      // Dynamic | FixedRoute
}
```
**Edge weights:** `RoadGraph` gains a per-edge `Length` (sum of polyline segment lengths);
travel time = `Length / vehicleSpeed`. Stops map to nodes via `BusStop.NearestNodeIndex`.

## 3. Agents

- **SimClock** — advances `SimTime`; ends the run after a configured simulated window
  and signals the Monitor to emit the final report.
- **DemandAgent** — per stop, draws arrivals from a **Poisson process** whose rate =
  `baseRate[stop] × peakProfile(SimTime)` (a time-of-day curve with rush-hour peaks).
  Each arrival creates a `PassengerRequest` (origin = this stop, destination = a random
  *other* stop) and enqueues it. All randomness from `Blackboard.Rng`.
- **RouteOptimizerAgent** — the intelligence (§4). On new waiting requests (Dynamic mode)
  it inserts their pickup+dropoff tasks into `Bus.Plan`.
- **Dispatch** — executes the plan: sets the navigator's goal to the next task's stop
  node (path via `GraphRouter`); on arrival performs **board/alight** (respecting
  capacity), stamps `BoardTime`/`AlightTime`, pops the task, requests the next leg.
- **MonitorAgent** — samples occupancy each step; on delivery records wait/ride/total;
  at run end computes aggregates and writes HUD + CSV (§5).

## 4. Routing (the core)

- **`GraphRouter`** — Dijkstra/A* shortest path between two nodes over the weighted
  `RoadGraph`; returns node path + total cost + expanded waypoint polyline. Shared by
  both controllers.
- **`InsertionPlanner`** (Dynamic mode) — tasks are `Pickup(p)` / `Dropoff(p)`. For a new
  request, try inserting its pickup at every position `i` and its dropoff at every
  position `j ≥ i` in the current plan; keep the cheapest (by added path cost via
  `GraphRouter`) that is **feasible**:
  - **Precedence:** pickup precedes dropoff.
  - **Capacity:** onboard count never exceeds `Capacity` at any point along the plan.
  Commit the best insertion; if none feasible (e.g., would always overflow), the
  request stays waiting and is retried on later ticks.

## 5. Baseline & metrics

- **`FixedRouteController`** (FixedRoute mode) — plan = a fixed cyclic sequence of all
  stop nodes (order fixed at startup). Board/alight uses the same Dispatch logic; it
  never re-plans for demand. This is the status-quo fixed route.
- **A/B methodology:** the same **seed** → identical `PassengerRequest` stream. The sim
  runs each mode over the same simulated window; metrics are reported side-by-side.
- **Metrics:** per delivered passenger — `wait = BoardTime − SpawnTime`,
  `ride = AlightTime − BoardTime`, `total = AlightTime − SpawnTime`. Aggregates:
  **avg & p90 wait**, **avg ride**, **avg total**, **passengers delivered**,
  **mean occupancy / utilization**, **empty-travel distance**.
- **Output:** on-screen **HUD** (live counters) during play, and a **CSV** written to a
  `Results/` folder (per-passenger rows + a summary row per mode) for thesis charts.

## 6. Visualization (lightweight)

Per-stop floating label "N waiting"; a bus occupancy readout (e.g., "12/20"); the
isometric `CameraFollow`. No passenger meshes in iteration 1.

## 7. Error handling / edge cases

- Origin == destination request → discarded at creation.
- Bus full → infeasible pickup insertion deferred; passenger keeps waiting (counts in wait).
- `GraphRouter` finds no path (shouldn't happen on the connected graph) → log warning, skip task.
- No active requests → bus holds at current node (Dynamic) / continues loop (FixedRoute).
- Determinism → single seeded `Rng`, fixed agent order, fixed sim timestep (frame-rate independent).
- Run end mid-ride → passengers still onboard/waiting are excluded from delivered aggregates but counted in an "undelivered" tally.

## 8. Vehicle-control seam (Unity → Isaac ready)

Hard separation between deciding **what to drive** and **how to physically drive it**:
```
interface IVehicleNavigator {
    void SetGoalPath(IReadOnlyList<Vector3> waypoints); // a leg to execute
    bool Arrived { get; }
    event Action ReachedGoal;
}
```
- **Now:** `KinematicNavigator` wraps `BusPathFollower` (waypoints, no physics) — ideal
  for testing routing/scheduling.
- **Later (stage 2):** `IsaacNavigator` / `AWSIMNavigator` receives the *same* goals and
  drives a physics rigid body with LiDAR/proximity-based obstacle avoidance & collision
  — swapped in with **zero change** to demand, routing, scheduling, or metrics.

The fleet intelligence stays **platform-agnostic C#**; only this actuator layer is
platform-specific.

## 9. Platform roadmap

- **Iterations 1–N (Unity):** all logistics — routing, scheduling, fleet, demand,
  monitoring. Sufficient and self-contained.
- **Stage 2 (sensor-based autonomous navigation):** implemented behind `IVehicleNavigator`
  via Isaac Sim (native RTX LiDAR/PhysX/ROS2) or in-engine AWSIM/Unity Robotics Hub.
  Decision deferred until reached; no rework of stage-1 intelligence required.

## 10. Testing / verification

- **`GraphRouter`** — shortest path & cost on a known small weighted graph.
- **`InsertionPlanner`** — capacity never exceeded; precedence held; min-cost position
  chosen on a hand-checked case.
- **`DemandAgent`** — same seed → identical request stream; peak profile raises rate at peaks.
- **Determinism** — same seed + mode → identical metrics across runs.
- **Headline result** — under peak demand, Dynamic mode yields lower avg wait & avg total
  than FixedRoute.
- Verified via the `RunCommand` inline harness plus HUD/CSV, per the MCP workflow.

## 11. Extension points (not built now)

- `RouteOptimizerAgent` → fleet version assigning requests across buses (sub-project 4).
- `DemandAgent` → paired with a `ForecastAgent` predicting the same profile (sub-project 5).
- `GraphRouter` edge weights → time-varying congestion costs (sub-project 6).
- `IVehicleNavigator` → sensor-based navigator (stage 2).

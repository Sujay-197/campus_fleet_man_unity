# Fleet Coordination (Iteration 2) — Design Spec

**Date:** 2026-08-23
**Status:** Approved (design), pending implementation plan
**Builds on:** iteration 1 (`Simulation`, `Blackboard`, `DemandAgent`, `RouteOptimizerAgent` +
`InsertionPlanner`, `FixedRouteAgent`, `Dispatch`, `MonitorAgent`) — see
`2026-07-13-agent-transit-iteration1-design.md`.
**Decisions recorded:** [ADR 0001](../adr/0001-centralized-fleet-assignment.md) ·
[ADR 0002](../adr/0002-fair-fixed-route-fleet-baseline.md) · glossary in [`CONTEXT.md`](../../CONTEXT.md)
**Scene:** `Assets/Scenes/SampleScene.unity` · **Unity:** 6000.5.3f1 · Built-in RP.

## Goal

Extend the single-bus system to a **fleet of N buses under one centralized optimizer**, and
measure it against a **fair multi-bus fixed-route baseline** under identical demand — including
a **scaling curve** over fleet size. Ship alongside the minimal world enrichment (more stops,
spatially uneven demand) that makes fleet coordination *mean* anything.

This is sub-project 4 of the program decomposition, plus a folded-in slice of "strengthen the
A/B result" from `Docs/NEXT_STEPS.md` §A.

## Why item A is folded in (not optional)

Today the scene has **4 stops with spatially uniform demand**. In that world a single fixed loop
is naturally efficient and every bus looks alike, so a fleet experiment would only show
"more buses = more throughput," which demonstrates nothing about *coordination*. Fleet results
need a world with enough stops and enough spatial demand structure for assignment choices to
differ. Hence §2 ships with §3–§6.

**Correction to the NEXT_STEPS framing:** *temporal* peaking already exists — `PeakProfile`
applies morning/evening rush multipliers. What is missing is **spatial** structure:
`DemandAgent` uses one identical rate for every stop and picks destinations uniformly at random.
Item A here therefore means **hub-biased spatial demand**, not "add peaks."

## Non-goals (iteration 2 / YAGNI)

- **No reassignment.** A request, once assigned, stays with that bus (§4). Re-auctioning
  not-yet-boarded requests is future work.
- **No anticipatory rebalancing / repositioning.** Idle buses hold in place (§4.4). Moving buses
  toward *expected* demand is demand forecasting — sub-project 5, deliberately kept separate.
- **No decentralized negotiation.** Assignment is centralized (ADR 0001).
- **No inter-bus transfers.** A passenger is served start-to-finish by one bus.
- **No congestion / time-varying edge costs** (sub-project 6).
- **No per-bus heterogeneity.** All buses share one capacity and one cruise speed.
- **No new physical fidelity.** Buses remain visual followers driven by sim time.

## 1. Architecture delta

Iteration 1's structure is kept: plain-C# `IAgent`s over a `Blackboard`, ticked in fixed order by
`Simulation` on a fixed sim-timestep. The change is that the single `Bus` becomes a **fleet**, and
one agent (the optimizer) becomes fleet-aware while another (`Dispatch`) is **replicated per bus**.

Tick order becomes:

```
SimClock → DemandAgent → (FleetOptimizerAgent | FixedRouteFleetAgent) → Dispatch[0..N-1] → MonitorAgent
```

`Dispatch` already holds all its leg state in instance fields (`_traveling`, `_legEndNode`,
`_legStartTime`, `_legArriveTime`) and takes its navigator by constructor, so **one instance per bus
is a drop-in** — no redesign, only wiring.

## 2. World enrichment (folded-in item A)

### 2.1 More stops

**Problem:** `RoadGraphBuilder` binds one `BusStop` per child of the `Buildings` object, so the stop
count is exactly the building count (currently 4: `AB1`–`AB4`).

**Decision:** add a second, optional binding source — a **`Stops` root** whose children are
lightweight marker GameObjects, each bound to its nearest graph node exactly like a building.
`Buildings` children keep working unchanged (backwards compatible). `StopId` is assigned in a
stable order: buildings first (hierarchy order), then `Stops` markers (hierarchy order), so
existing stop ids do not shift.

**Target:** ~**8–12 stops** total on the existing ~36-node graph. Markers are placed at
well-separated nodes so stops are not clustered.

**Guard:** two stops may bind to the same node; the builder logs a warning if so (a duplicate stop
node makes routing degenerate).

### 2.2 Spatially uneven ("hub-biased") demand

**Today:** every stop has the same rate; a request's destination is a uniformly random *other*
stop. Spatially featureless.

**Decision:** introduce a per-stop **`Weight`** (on `BusStop`, default 1) used two ways:

- **Origin rate:** stop *i*'s Poisson rate = `BaseRatePerStopPerHour × PeakProfile(t) × Weight[i]`.
- **Destination choice:** destination drawn **weight-proportionally** among the other stops
  (not uniformly).

A small number of stops designated **hubs** (weight ≫ 1, e.g. an academic core) create the
directional, concentrated flow that distinguishes a coordinating fleet from a looping one.

**Determinism preserved:** all draws still come from `Blackboard.Rng`; weighted selection uses a
deterministic cumulative-weight scan.

**Explicitly not done:** time-varying *direction* (morning inbound → hub, evening outbound). That
couples spatial and temporal structure and belongs with forecasting; noted as an extension.

## 3. Data model changes

```
Blackboard:
    List<BusState> Buses          // replaces the single `Bus`
    (all other fields unchanged)

BusState:
    int Id                        // NEW: stable index, used for per-bus metrics/CSV
    (CurrentNode, Capacity, OnboardRequestIds, Plan unchanged)

PassengerRequest:
    int AssignedBusId = -1        // NEW: -1 = unassigned; set once at assignment (§4)

BusStop:
    float Weight = 1f             // NEW: demand weighting (§2.2)
```

**Migration note:** `Blackboard.Bus` is removed, not kept as an alias. Every reader
(`Dispatch`, `RouteOptimizerAgent`, `MonitorAgent.FormatHud`, `Simulation`) is updated. A lingering
single-bus alias would silently keep working while ignoring the rest of the fleet — a bug factory.

## 4. Fleet assignment (the core)

### 4.1 Centralized optimizer

`RouteOptimizerAgent` → **`FleetOptimizerAgent`** (see ADR 0001 for why centralized). Each planning
pass, for every waiting, unassigned request (in ascending `Request.Id` for determinism), it asks
**every** bus for the cost of its best feasible insertion and commits the request to the **cheapest**
bus. Ties break by lowest `BusState.Id`.

### 4.2 Non-committing probe (required `InsertionPlanner` refactor)

`InsertionPlanner.TryInsert` currently finds the best insertion **and mutates the plan**. Fleet
assignment must evaluate all buses *before* committing to one. Split it:

```
// evaluate only — no mutation
static bool Probe(RoadGraph graph, BusState bus, PassengerRequest req,
                  out float score, out int atI, out int atJ);

// commit a previously probed insertion
static void Commit(BusState bus, PassengerRequest req, int atI, int atJ);

// unchanged public behaviour, now implemented as Probe + Commit
static bool TryInsert(RoadGraph graph, BusState bus, PassengerRequest req);
```

`TryInsert` is retained so iteration-1 verification keeps passing unchanged.

**Comparability of scores across buses:** `PlanScore` is the sum of cumulative service times for a
plan starting at that bus's `CurrentNode`. The optimizer compares the **marginal** score
(`scoreAfter − scoreBefore`) per bus, not the absolute score — otherwise a bus with a long existing
plan is unfairly penalised regardless of the new request's true incremental cost.

### 4.3 Assignment policy

**Greedy one-shot, batched.** Every `ReplanInterval` (60 sim-s, unchanged), all currently waiting
and unassigned requests are assigned in `Request.Id` order. Once assigned, a request is never
reassigned, even if another bus later becomes cheaper.

**Accepted consequence (a measured finding, not a hidden flaw):** greedy assignment is myopic and
can imbalance the fleet. The **utilization-balance metric** (§5) is specifically there to expose
this; the write-up reports it honestly and names reassignment as the fix.

**Per-bus saturation:** the existing plan cap (`2 × Capacity` tasks) is applied **per bus**; a
saturated bus is skipped as a candidate rather than ending the pass. A request that no bus can
feasibly take stays waiting and is retried next pass.

### 4.4 Idle behaviour

A bus with an empty plan **holds at its current node** (iteration-1 behaviour, unchanged). No depot
return (which would be pure empty travel, sabotaging the very metric Dynamic should win) and no
anticipatory repositioning (that is sub-project 5).

## 5. Baseline and metrics

### 5.1 Fixed-route fleet baseline

Per ADR 0002: **N buses on one shared loop of all stops, phase-offset** by their distributed start
positions (a headway model). The loop order is a **nearest-neighbor tour** computed once at startup
from the stop set (start at stop 0, repeatedly hop to the nearest unvisited stop by
`GraphRouter.Cost`), replacing the arbitrary `StopId` order. `FixedRouteAgent` →
**`FixedRouteFleetAgent`**, refilling a lap per bus whose plan drains.

### 5.2 Metrics

Passenger-level metrics (**wait, ride, total, delivered, undelivered, p90**) are per-passenger and
aggregate across the fleet unchanged. Added:

| Metric | Definition |
|---|---|
| Per-bus occupancy | each bus sampled each tick; reported per bus and as a fleet mean |
| Per-bus empty travel | each `Dispatch` accrues its own; reported per bus and summed |
| Per-bus delivered | deliveries credited to the serving bus |
| **Utilization balance** | **coefficient of variation (stddev ÷ mean) of per-bus delivered counts** — 0 = perfectly even, higher = imbalanced |

`MetricsSummary` gains `BusCount` and `DeliveredCoV`. Output adds a **per-bus CSV**
(`{mode}_buses.csv`: `BusId,Delivered,MeanOccupancy,EmptyTravelDistance`) alongside the existing
per-passenger and summary CSVs. The HUD shows fleet totals plus a compact per-bus occupancy line.

**Capacity semantics:** capacity is **per bus and fixed** across the sweep — adding a bus adds a
vehicle *and* its seats, which is the real operational question ("how many buses do we need?").

## 6. Simulation wiring & the experiment

### 6.1 Wiring

`Simulation` gains **`BusCount`** and a **`BusPrefab`**. At startup it instantiates `BusCount`
buses from the prefab (each with its own `BusPathFollower` → `KinematicNavigator` → `Dispatch`),
places them at **distributed** start nodes (evenly spaced across the stop list, deterministic), and
builds the agent list per §1. `CameraFollow` targets bus 0. The scene's hand-placed bus is replaced
by the prefab instance path.

**Start placement is identical for both modes**, so neither is advantaged.

### 6.2 Experiment

- **Headline:** Dynamic fleet vs FixedRoute fleet at a matched size (**3 buses**), identical seed,
  demand, stops, and start placement.
- **Scaling curve:** sweep `BusCount` **1 → 4** in both modes; plot delivered, avg/p90 wait, empty
  travel, and balance vs fleet size, showing where returns diminish.
- Runs are deterministic and ~seconds headless, so the full sweep is cheap. Results land in
  `Results/` per mode and fleet size.

## 7. Error handling / edge cases

- **`BusCount` < 1** → clamped to 1 with a warning.
- **`BusCount` > stop count** → allowed; distributed placement wraps (multiple buses may share a
  start node).
- **No feasible bus for a request** (all saturated / capacity-blocked) → stays waiting, retried next
  pass; counted as undelivered if the run ends first.
- **Request assigned to a bus that can no longer serve it** — cannot occur: assignment only ever
  *adds* to a plan and plans are never truncated.
- **Two stops binding the same node** → builder warning (§2.1).
- **Zero total weight** in destination selection → falls back to uniform choice.
- **Determinism** → single seeded `Rng`; requests assigned in `Id` order; ties broken by `BusState.Id`;
  fixed agent order; buses ticked in `Id` order.

## 8. Testing / verification

Red/green via the `RunCommand` inline harness (per the MCP workflow), consistent with iteration 1:

- **`InsertionPlanner.Probe`** — returns the same score/positions `TryInsert` would choose, and
  **does not mutate** the plan (probe twice → identical plan and identical score).
- **`Commit`** — after `Probe` + `Commit`, the plan equals what `TryInsert` produces.
- **Fleet assignment** — with two buses where bus 1 is adjacent to a request's origin and bus 0 is
  far, the request is assigned to bus 1; assignment is by *marginal* score (a bus with a long
  existing plan is not penalised for its history).
- **Capacity per bus** — no bus's plan ever implies exceeding its own capacity.
- **Uniqueness** — every request is assigned to at most one bus (no double-service).
- **Nearest-neighbor tour** — on a hand-checked stop layout, produces the expected order and is
  no longer than `StopId` order.
- **Weighted demand** — a hub stop with weight 5 receives ≈5× the origins of a weight-1 stop over a
  long run; same seed → identical request stream.
- **Determinism** — same seed + mode + `BusCount` → byte-identical metrics across runs.
- **Fleet ≥ single** — at `BusCount = 1`, Dynamic-fleet metrics **exactly match** iteration 1's
  single-bus Dynamic result (the generalization is behaviour-preserving). This is the key
  regression guard.
- **Headline** — at 3 buses under hub-biased demand, Dynamic beats FixedRoute on delivered and p90 wait.

## 9. Extension points (not built now)

- **Reassignment** — re-auction not-yet-boarded requests each pass (the fix for §4.3 myopia).
- **Anticipatory repositioning** — pair with `ForecastAgent` to pre-spread idle buses (sub-project 5).
- **Time-varying demand direction** — morning inbound / evening outbound hub flow (§2.2).
- **Heterogeneous fleet** — per-bus capacity/speed; the optimizer's cost oracle already supports it.
- **Congestion-aware costs** — time-varying edge weights (sub-project 6).
- **Decentralized assignment** — the insertion-cost probe becomes each bus agent's bid function
  (revisiting ADR 0001).

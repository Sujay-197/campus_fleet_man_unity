# Next Steps — Agent-Based Campus Transit

Prioritized backlog. Iterations 1 and 2 are complete: demand + capacity-aware routing + metrics vs a
fixed-route baseline (iteration 1), and **fleet coordination** with the world enrichment it required
(iteration 2 — see `Docs/specs/2026-08-23-fleet-coordination-design.md`). Each item notes *why* and a
rough *where*.

## Done

- ✅ **Fleet coordination (sub-project 4).** Centralized `FleetOptimizerAgent` assigns each request to
  the bus with the cheapest *marginal* insertion ([ADR 0001](adr/0001-centralized-fleet-assignment.md));
  `FixedRouteFleetAgent` + `StopTour` give a fair, phase-offset baseline
  ([ADR 0002](adr/0002-fair-fixed-route-fleet-baseline.md)); per-bus metrics + `DeliveredCoV`.
- ✅ **World enrichment (old item A).** 10 stops via a `Stops` marker root, hub-biased demand via
  `BusStop.Weight`. *(Note: temporal peaking already existed in `PeakProfile` — what was missing was
  spatial structure.)*
- ✅ **Sweep + editor menu.** **Bus System ▸ Run Fleet Sweep** sweeps fleet size and demand rate
  headlessly into `Results/sweep.csv`.

## A. Request reassignment (highest value — fixes a measured weakness)

The A/B shows Dynamic's **p90 wait is not better under congestion** and per-bus utilisation is uneven
(`DeliveredCoV`), both direct consequences of **greedy one-shot assignment**: a request is bound to one
bus forever, so an unlucky early assignment strands a passenger behind a long plan while another bus
frees up.

- Each planning pass, re-auction **not-yet-boarded** requests across the fleet and migrate a request's
  pickup+dropoff when another bus is now meaningfully cheaper.
- Must never strand a request mid-migration; needs a strict, deterministic re-auction order.
- Success criterion: p90 wait improves and `DeliveredCoV` drops, with delivered ≥ today.
  *Where:* `FleetOptimizerAgent` (+ a `Remove`/`Reinsert` pair alongside `InsertionPlanner.Commit`).

## B. Anticipatory repositioning + demand forecasting (sub-project 5)

Idle buses currently **hold in place**, so the fleet drifts toward wherever demand recently was. Pair
`DemandAgent` with a `ForecastAgent` that predicts the same peak/hub profile and pre-positions idle buses
toward expected demand. Compare reactive vs forecast-aware under one seed.

## C. Environment / congestion-aware costs (sub-project 6)

Make `RoadGraph` edge weights time-varying (rush-hour multipliers) so `GraphRouter` routes around slow
segments. *Where:* `RoadEdge` (time-varying cost), `GraphRouter`, a new `CongestionAgent`.

## D. Heterogeneous fleet

Per-bus capacity and cruise speed (minibus vs coach). The optimizer's cost oracle already supports it —
only `BusState` and the `Simulation` wiring need to vary per bus.

## E. Stage 2 — physical sim-to-real

Now a **separate sub-project**: `rc_transit/` (Ackermann RC car, LiDAR SLAM + Nav2, three-tier
PC/Pi/ESP32 compute). See `Docs/specs/2026-08-16-physical-rc-transit-design.md`. The sim's
`IVehicleNavigator` seam remains the place a hardware-in-the-loop finale would attach.

## Engineering / infra follow-ups

- **Fold the red/green `RunCommand` checks into `Editor/BusSystemSelfTests`** so the agent contracts
  (probe/commit, fleet assignment, tour, metrics) are one click to re-verify.
- **Plot the sweep** — `Results/sweep.csv` is ready for a chart of served-% and empty-travel vs load.
- **Remove `Assets/_Recovery/`** — a Unity crash-recovery backup still sitting untracked in the tree.

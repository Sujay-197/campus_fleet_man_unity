# Next Steps — Agent-Based Campus Transit

Prioritized backlog after iteration 1 (demand + single-bus capacity-aware routing + metrics vs a
fixed-route baseline). Grouped by how much they change the system. Each item notes *why* and a rough
*where*.

## A. Strengthen the current A/B result (small, high-value)

The demand-responsive advantage is real but currently clearest only under load, because the scene has
just **4 stops with uniform demand** — a regime where a fixed loop is naturally efficient. Two cheap
levers widen the gap and better match the thesis framing ("peak-hour congestion"):

1. **Peaked / uneven demand.** Bias origins/destinations toward a hub (e.g. mornings → academic core,
   evenings → residences) instead of uniform random. A fixed route wastes trips on quiet stops while
   Dynamic focuses where people are.
   *Where:* `DemandAgent.SpawnRequest` (destination selection) + optionally a per-stop weight table.
2. **More bus stops.** Bind additional road-graph nodes as stops (the graph has ~36 nodes; only 4 are
   stops today). Demand-responsive routing's benefit grows with stop count.
   *Where:* add stop markers in the scene + `RoadGraphBuilder` stop binding.
3. **Parameter sweep + report.** Runs are deterministic and ~1.5 s headless — sweep cruise speed,
   demand rate, and capacity, and emit a small results table/plot for the write-up.
   *Where:* a headless sweep harness (editor menu or `RunCommand`) writing to `Results/`.

## B. Fleet coordination — multiple buses (sub-project 4)

The single-bus assumption is the biggest limiter. Extend to a small fleet:

- `BusState` → a list of buses; `RouteOptimizerAgent` assigns each new request to the **best** bus
  (cheapest feasible insertion across the fleet), not just one bus.
- `Dispatch` already executes a plan generically — replicate per bus.
- New metrics: fleet utilisation balance, per-bus occupancy.
  *Where:* `Blackboard`, `RouteOptimizerAgent`, `Dispatch`, `Simulation` wiring; add bus GameObjects.

## C. Demand forecasting (sub-project 5)

Pair `DemandAgent` with a `ForecastAgent` that predicts the same peak profile and lets the optimizer
**pre-position** the bus toward expected demand before it materialises. Compare reactive vs
forecast-aware routing under the same seed.

## D. Environment / congestion-aware costs (sub-project 6)

Make `RoadGraph` edge weights time-varying (rush-hour congestion multipliers) so `GraphRouter` routes
around slow segments. Requires a congestion model feeding edge costs per sim-time.
*Where:* `RoadEdge` (time-varying cost), `GraphRouter`, a `CongestionAgent`.

## E. Stage 2 — sensor-based autonomous navigation (behind `IVehicleNavigator`)

The actuator seam already isolates *how the bus physically drives* from all the logistics. Stage 2 swaps
`KinematicNavigator` for a physics + sensor navigator with **zero change** upstream:

- `IsaacNavigator` (NVIDIA Isaac Sim — native RTX LiDAR / PhysX / ROS2) **or** in-engine
  `AWSIMNavigator` / Unity Robotics Hub.
- Adds obstacle/collision detection, proximity-based avoidance, and later battery/energy and
  terrain-aware energy models.
- The routing/scheduling/metrics layers stay platform-agnostic C#.

## Engineering / infra follow-ups

- **Editor menu for the A/B run.** Wrap the headless deterministic run in a `Bus System ▸ Run A/B`
  menu item so results reproduce without the MCP harness.
- **Regression self-tests.** Fold the `RunCommand` red/green checks into `Editor/BusSystemSelfTests`
  so the agent contracts (router, planner, dispatch, metrics) are one click to re-verify.
- **Remove `_Recovery/` clutter** if a Unity crash-recovery scene backup reappears in the working tree.

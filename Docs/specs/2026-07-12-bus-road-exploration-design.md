# Bus Road-Exploration System — Design Spec

**Date:** 2026-07-12
**Status:** Approved (design), pending implementation plan
**Scene:** `Assets/Scenes/SampleScene.unity`
**Unity:** 6000.5.3f1, Built-in Render Pipeline

## Goal

Make the `school-bus` autonomously drive the road network, covering every road
("explore all roads"). This is the **v1 foundation** for a future *autonomous
campus fleet management system*: an agentic "brain" will later plug in to route
the bus to destinations and stops. Therefore v1 must expose a clean, graph-based
map and a swappable control seam — not a throwaway fixed path.

## Non-Goals (v1 / YAGNI)

- No physics-based vehicle (wheel colliders, suspension). Movement is kinematic.
- No fully-built A*/Dijkstra routing or passenger/scheduling logic yet — only the
  interfaces/data needed so the agent work can add them without rework.
- No NavMesh (hides the topology the agent must reason about; unreliable on thin
  road strips).
- No multi-bus fleet yet (architecture should not preclude it).

## Scene Facts (verified via MCP)

- ~30 road tiles at root: `intersectionRoad` (5), `straightRoad` (~22),
  `curvedRoad` (3). All road materials were repaired to Standard shader earlier.
- Tiles sit on a regular **~28-unit grid**, Y-rotations in clean 90° steps,
  shared baseline z ≈ -1.37 (e.g. `straightRoad (1)` x=-2.2, `intersectionRoad`
  x=25.65 → one cell apart).
- 4 apartments: `cb-apartment-A (1..4)` — these are **bus stops**.
- `school-bus` at (50, 0, 3), facing +X, scale (150,200,200).

## Architecture

Four layers with well-defined interfaces so each is independently understandable,
testable, and replaceable.

### 1. Hierarchy grouping
Empty parent GameObjects at origin; existing objects reparented:
- `Roads` — all road tiles
- `Buildings` — the apartments (each also a stop)
- `Vehicles` — `school-bus`

Camera / Directional Light / Global Volume remain at root.

### 2. RoadGraph — the map
Editor-built graph scanned from tiles under `Roads`.
- Each tile yields **connection points** from its type + Y-rotation:
  - straight → 2 opposite sides
  - curve → 2 adjacent sides, plus an arc midpoint waypoint so turns aren't cut
  - intersection → 3–4 sides
- Connection points coincident on the grid merge into shared **nodes**; tile
  centers/arcs become **edges** (each edge carries its polyline for smooth
  following).
- Serialized into a `RoadGraph` MonoBehaviour (nodes, edges, adjacency).
- **Verification safeguard:** draws nodes + edges as gizmos so the topology can be
  visually confirmed against the streets before it is trusted. This is the one
  place auto-detection can misfire (curve/intersection orientation), so it is
  explicitly checked.

### 3. BusStops — destinations
- Each building registers a `BusStop` bound to its **nearest RoadGraph node**.
- Stored as graph data now (for the future agent to route to).
- v1 behavior: bus optionally pauses ~1s at a stop when it passes one.

### 4. BusPathFollower — kinematic steering
- Drives the bus along a supplied list of waypoints: constant speed, smooth
  `RotateTowards` heading toward the next waypoint, snap to road height.
- Transform-based (no physics/NavMesh).
- API: `SetRoute(IReadOnlyList<Vector3> waypoints)`, advances along it, raises a
  `ReachedEndOfRoute` event; `Speed`, `TurnSpeed`, `StopDuration` serialized.

### 5. IBusController — the swappable brain seam
```
interface IBusController {
    // Called when the bus needs its next route (start, or route finished).
    IReadOnlyList<int> NextRoute(RoadGraph graph, int currentNode);
}
```
- **v1:** `ExploreAllController` — graph walk preferring unvisited edges until all
  edges covered, then loops. Guarantees every road is driven.
- **Later:** the agentic brain implements the same interface (or feeds routes via
  shortest-path over `RoadGraph` + `BusStop`s) with **no changes** to graph or
  follower.

## Data Flow

```
RoadGraph (built once, verified via gizmos)
        │  nodes/edges/adjacency + BusStops
        ▼
IBusController.NextRoute(graph, currentNode)  ──►  list of node ids
        │  expanded to edge polylines
        ▼
BusPathFollower.SetRoute(waypoints)  ──►  moves school-bus, fires ReachedEndOfRoute
        │
        └──► on route end, ask controller for the next route (loop)
```

## Error Handling / Edge Cases

- **Graph build finds isolated/duplicate nodes:** merge by grid-snapped position
  (tolerance ~ half a cell); log a warning listing unmerged endpoints.
- **Dead-end tiles:** valid graph leaves; explorer must be able to reverse out.
- **Bus starts off-graph:** snap to nearest node/edge point before first route.
- **Controller returns empty/invalid route:** follower idles and logs; never
  drives to NaN.
- **Topology looks wrong in gizmos:** we fix the connection-point rules before
  wiring movement — movement work does not begin until the graph is confirmed.

## Testing / Verification

- **Graph:** gizmo overlay compared against a top-down scene capture; node/edge
  counts sanity-checked against tile count.
- **Follower:** unit-level check that a known waypoint list produces monotonic
  progress and correct final heading (edit-mode test where feasible).
- **Explorer:** assert every edge's `visited` flag is set within one full cycle.
- **End-to-end:** enter Play mode, capture scene/game view over time, confirm the
  bus traverses all roads and returns to loop; console clean.

## Extension Points (for the fleet system, not built now)

- `IBusController` → agentic brain.
- `RoadGraph` shortest-path query (A*/Dijkstra) — stub interface only.
- `BusStop` metadata (id, capacity, schedule) — id only for now.
- Multiple `BusPathFollower` instances → fleet.

# Campus Fleet Management — Unity

An autonomous **campus bus** that drives a modular low-poly road network, built as the
foundation for an agentic fleet-management system. A road graph is auto-generated from
the scene's road tiles; a bus follows it and explores every road, pausing at buildings
(bus stops). The route-planning layer is a swappable interface, so an agentic "brain"
can later route the bus to specific stops without touching the graph or movement code.

![Preview — the school bus driving the road network](docs/preview.png)

## How it works

| Layer | Type | Responsibility |
|-------|------|----------------|
| `RoadGraph` | `MonoBehaviour` | Serialized nodes + edges (with center-line polylines) of the road network. |
| `RoadGraphBuilder` | Editor tool | Auto-builds the graph from the tiles under `Roads` (menu: **Bus System ▸ Build Road Graph**). |
| `BusPathFollower` | `MonoBehaviour` | Kinematic waypoint follower — constant speed, smooth turning. |
| `IBusController` | interface | The swappable "brain". v1 = `ExploreAllController` (covers every road). |
| `BusStop` | `MonoBehaviour` | Each building bound to its nearest graph node — a routing target for the agent. |
| `BusAgent` | `MonoBehaviour` | Orchestrates graph + controller + follower; loops routes endlessly. |
| `CameraFollow` | `MonoBehaviour` | Isometric eagle-eye camera that tracks the bus. |

The graph builder is robust to the pack's real geometry: straight tiles may be scaled
along their length, curve tiles have off-center pivots (snapped to the two nearest
road-ends), and unconnected intersection arms are pruned so the bus never drives into
empty ground. The result is a single fully-connected network.

## Running it

1. Open the project in **Unity 6000.5.3f1** (Built-in Render Pipeline).
2. Open `Assets/Scenes/SampleScene.unity`.
3. Run **Bus System ▸ Build Road Graph** (already built and saved in the scene; re-run
   if you move or add road tiles).
4. Press **Play**. The bus snaps onto the nearest road and begins exploring; the camera
   follows in an isometric view.

## Project layout

```
Assets/
  Scenes/SampleScene.unity        # the city scene (Roads / Buildings / Vehicles / RoadGraph)
  Scripts/BusSystem/              # runtime system + Editor/ build tool
  Road_Tiles/ , Loading Games/    # art: modular road pack + Toon City pack
Docs/
  specs/  plans/                  # design spec and implementation plan
```

## Roadmap

- Replace `ExploreAllController` with an **agentic controller** that routes to
  `BusStop`s via shortest-path (A\*/Dijkstra) over the same `RoadGraph`.
- Passenger demand / scheduling; multiple buses (fleet).
- Lane-aware driving (right-hand offset instead of centerline).

## Notes

- URP is installed but intentionally inactive; the scene uses Built-in Standard shaders.
- If scripts don't compile after a Unity upgrade, check for packages using obsolete APIs
  (`TreeView`, `GetInstanceID`) — this project pins `com.unity.inputsystem` ≥ 1.19.0 and
  drops unused packages (ai.navigation, timeline, collab-proxy, visualscripting).

# Physical RC Transit — Sim-to-Real Navigation Design Spec

**Date:** 2026-08-16
**Status:** Approved (design), pending Phase-1 implementation plan
**Relationship to the sim:** *Separate system.* The Unity project (`campus_fleet_man_unity`)
keeps the demand / fleet / routing thesis; this is a **standalone autonomous-navigation
demonstrator** with its own request interface. The two are **not** wired together — the
sim's `IVehicleNavigator` seam is deliberately **not** exercised in hardware (see §9).
**Package:** all IRL work lives in `rc_transit/` (a ROS 2 workspace + ESP32 firmware),
kept out of `Assets/`.

## Vision & decomposition

Long-term goal: a real Ackermann RC vehicle that autonomously services checkpoints in
indoor rooms, standing in — conceptually — for the "below the seam" physical layer the
sim abstracts. Delivered in four phases, each its own spec→plan→build:

1. **Phase 1 — classical autonomous navigation (this spec).** LiDAR SLAM + Nav2 on the
   real car; free-navigate to checkpoint poses; static + unknown-obstacle avoidance.
2. **Phase 2 — dynamic obstacles** (moving people/objects) + optional web request UI.
3. **Phase 3 — Isaac Sim digital twin**: synthetic perception, camera policy with lighting
   domain-randomization, begin RL.
4. **Phase 4 — full autonomous stack**: Isaac/Newton-trained policy, camera+LiDAR fusion,
   Ackermann realism; *optionally* a hardware-in-the-loop finale that finally drives one
   sim bus through the real car (closing the seam).

**This spec covers Phase 1 only.**

## Goal

Issue **"go from checkpoint A to checkpoint C"** on a simple interface. The car plans a
path in its SLAM map of the room and drives there autonomously; when an **unknown obstacle
is placed in its path**, it detects it on the live LiDAR costmap and **replans around it**,
arriving **within 10–15 cm** of the target checkpoint (position only).

## Non-goals (Phase 1 / YAGNI)

- **No learning.** No Isaac, no Newton, no RL — Phase 1 is entirely classical Nav2.
  Reaching for ML to solve a solved planning problem is out of scope (deferred to Phase 3–4).
- **No moving obstacles.** Static world + unknown-but-static obstacles only (dynamic → Phase 2).
- **No camera perception.** LiDAR is the only navigation sensor; a camera is a Phase-3 add-on.
- **No web dashboard.** RViz2 + a CLI is the Phase-1 interface; the pretty UI waits until
  the car actually drives (Phase 2).
- **No heading-accurate arrival.** Arrival is a position circle; exact final heading is deferred.
- **No integration with the Unity sim.** Standalone request interface (§9).
- **No multi-vehicle / fleet.** One car. Fleet logistics stay in the sim, permanently.

## 1. Architecture — three compute tiers

Perception-grade autonomy cannot round-trip over WiFi, so compute is split by latency budget:

| Tier | Hardware | Responsibility |
|------|----------|----------------|
| **Brain** | PC + GPU | *Phase 3–4 only:* policy training, heavy offline perception. Idle in Phase 1. |
| **Companion** | Raspberry Pi 4/5 (onboard) | ROS 2 + SLAM Toolbox + Nav2, sensor drivers, the **safety reflex**, the request interface. The whole Phase-1 brain. |
| **MCU** | ESP32 (onboard) | Hard real-time: steering servo, drive ESC, wheel encoders, IMU. Bridged to the Pi via **micro-ROS**. |

**Non-negotiable:** the obstacle-stop reflex ("LiDAR < threshold → cut throttle") runs
**onboard the Pi/ESP32**, never at the far end of a network hop.

## 2. Vehicle & kinematics

- **Drivetrain:** **Ackermann** (front-steer, rear-drive) — reuses a donor RC car's
  dynamics and matches the "bus" framing. Viable because the room ≫ the car, so the
  minimum turning radius is small relative to free floor.
- **Odometry:** bicycle model from steering angle + rear-wheel speed (encoder), fused with
  IMU yaw. Published as `/odom` by the ESP32/micro-ROS node.
- **Consequences (accepted):** Nav2 must use a **kinematically-feasible planner**
  (Smac Hybrid-A* or TEB) and an Ackermann-friendly controller (**Regulated Pure Pursuit**),
  *not* the default DWB. Exact-heading arrival is not attempted in Phase 1.

## 3. Environment

Indoor, **multiple rooms**, **uncontrolled (room) lighting**, **4 checkpoints per room**.
Because localization and avoidance are **2D LiDAR**-based, lighting is irrelevant to Phase 1
(it only re-enters at Phase 3 when a camera is added — where it becomes a *feature* to
domain-randomize over). Each room is mapped once; checkpoints are saved poses in that map.

## 4. Localization & navigation (the core)

- **SLAM:** onboard **2D LiDAR** + **SLAM Toolbox** (ROS 2) builds/serves an occupancy-grid
  map of the room and localizes the car within it. Lighting-immune; one sensor serves both
  localization and obstacle sensing.
- **Checkpoints:** named **poses** `(x, y, θ)` saved in the map — *no* hand-coded checkpoint
  graph. `A, B, C, D` per room are entries in a small pose store (YAML).
- **Planning/driving:** **Nav2**. "Go to C" = set a Nav2 goal to C's pose. The global planner
  finds a path in free space; the controller drives it; the local costmap (fed by live LiDAR)
  handles obstacle avoidance and triggers replanning when an unknown obstacle appears.
- **Path traversal is free-navigation**, not marker-to-marker hops — the planner owns the route.

## 5. The request interface (Phase 1)

- **RViz2** — development visualization: map, live pose, planned path, costmap, goal.
- **`rc_interface` CLI** — the "request A→C" surface: a one-line command / keypress that
  looks up a checkpoint name in the pose store and publishes it as a Nav2 goal, then reports
  arrival (pose within tolerance) or failure.
- The nicer web "request A→C" dashboard (mirroring the sim's activity-bar aesthetic) is
  **Phase 2**, built only after the nav milestone is proven.

## 6. Definition of done (Phase 1)

> From the `rc_interface` CLI, request **A→C**. The car plans a path in its SLAM map and drives
> to C autonomously. A box is **dropped into its path mid-run**; the car detects it on the live
> LiDAR costmap and **replans around it**, arriving **within 10–15 cm** of C (position only).
> Reproducible across rooms and checkpoint pairs.

## 7. Error handling / edge cases

- **Goal unreachable** (fully blocked) → Nav2 reports failure; CLI surfaces it; car holds.
- **Localization lost / SLAM divergence** → stop; require re-localization before accepting goals.
- **Obstacle inside arrival radius** → treat as arrived-blocked; report, do not ram.
- **Comms drop (Pi↔ESP32)** → ESP32 **fail-safe: zero throttle, center steer** on heartbeat loss.
- **LiDAR dropout** → reflex layer commands stop (no obstacle data = unsafe to move).
- **Battery low** → telemetry warning; controlled stop.

## 8. Package layout (`rc_transit/`)

```
rc_transit/
  README.md                     # sub-project overview, 3-tier arch, build/run pointers
  ros2_ws/src/
    rc_bringup/                 # launch + params: LiDAR driver, SLAM Toolbox, Nav2,
                                #   Ackermann robot_description (URDF), checkpoint pose store
    rc_interface/               # "request A->C" CLI: name -> pose -> Nav2 goal, arrival report
  firmware/esp32/               # micro-ROS node: steering servo, ESC, encoders, IMU, /odom, fail-safe
  docs/hardware-bom.md          # bill of materials (chassis, LiDAR, Pi, ESP32, power)
```

*(Phase 3+ adds an `rc_perception/` package and PC-side training — not created now.)*

## 9. Relationship to the Unity sim (explicit)

The car does **not** execute the sim's fleet brain, and the sim's `IVehicleNavigator` seam is
**not** driven across the real boundary. This is a deliberate scope choice: fleet management is
not reproducible in hardware here, so it stays simulated, and the physical work is a *parallel*
autonomous-navigation contribution with its own request surface. **Known tradeoff:** the seam is
left untested sim-to-real — accepted for Phases 1–3, with a Phase-4 hardware-in-the-loop finale
available to close it if desired.

## 10. Standing risks / flags

- **Pi has no CUDA** — the Phase-4 neural policy will need quantization, a Coral/Hailo USB
  accelerator, or PC-offloaded inference. (Jetson Orin Nano if on-car NN is needed sooner.)
- **Newton is bleeding-edge** — quarantined to Phase 3+, never gating an early milestone.
- **Ackermann** — heading-accurate arrival deferred; relying on position-only tolerance.
- **Don't build the dashboard before the car drives.**
- **micro-ROS on ESP32** — keep the ESP32's job to real-time I/O; all planning on the Pi.

## 11. Testing / verification (Phase 1)

- **Odometry** — drive a known straight line / arc; `/odom` matches within tolerance.
- **SLAM** — map a room; loop-closure produces a consistent occupancy grid; saved/reloaded map localizes.
- **Checkpoint store** — named pose resolves to the correct map coordinate.
- **Nav (nominal)** — request A→C in a static room → arrives within 10–15 cm.
- **Nav (unknown obstacle)** — box dropped in path → live costmap shows it → replans → still arrives.
- **Fail-safe** — cut Pi↔ESP32 link mid-drive → car stops (zero throttle, center steer).
- Validated on hardware in the target rooms, not just in a sim.

## 12. Extension points (not built now)

- `rc_interface` CLI → **web dashboard** reusing the sim's activity-bar pattern (Phase 2).
- Nav2 local planner → **dynamic-obstacle** config (MPPI/TEB + tracking) (Phase 2).
- New `rc_perception` + PC training → **camera policy** with lighting domain randomization,
  Isaac Sim digital twin, RL (Phase 3–4).
- Optional **hardware-in-the-loop**: a `RealCarNavigator` implementing the sim's
  `IVehicleNavigator`, driving one sim bus through the real car (Phase 4 finale).

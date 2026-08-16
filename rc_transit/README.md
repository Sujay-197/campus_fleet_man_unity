# rc_transit — Physical Sim-to-Real Navigation

The **hardware** half of the campus-transit thesis: a real Ackermann RC vehicle that
autonomously services checkpoints in indoor rooms. It is a **standalone** project — it does
**not** run the Unity sim's fleet brain; fleet/demand/routing stays in the sim, and this is a
parallel autonomous-navigation demonstrator with its own request interface.

> Full design: [`Docs/specs/2026-08-16-physical-rc-transit-design.md`](../Docs/specs/2026-08-16-physical-rc-transit-design.md)

## Phase 1 goal

Request **"A → C"** on a CLI → the car plans a path in its LiDAR-SLAM map, drives there, and
replans around an **unknown obstacle dropped in its path**, arriving within **10–15 cm** of the
target checkpoint (position only). No learning, no camera, no moving obstacles yet.

## Three compute tiers

| Tier | Hardware | Job (Phase 1) |
|------|----------|---------------|
| Brain | PC + GPU | Idle in Phase 1 (Phase 3–4: policy training / heavy perception) |
| Companion | Raspberry Pi 4/5 (onboard) | ROS 2: SLAM Toolbox + Nav2 + sensor drivers + request interface + safety reflex |
| MCU | ESP32 (onboard) | Real-time: steering servo, drive ESC, encoders, IMU, `/odom`, fail-safe — via micro-ROS |

**Safety reflex (LiDAR-too-close → stop) runs onboard — never over WiFi.**

## Layout

```
rc_transit/
  ros2_ws/src/
    rc_bringup/     launch + params: LiDAR driver, SLAM Toolbox, Nav2 (Ackermann), URDF, checkpoint poses
    rc_interface/   "request A->C" CLI: checkpoint name -> pose -> Nav2 goal -> arrival report
  firmware/esp32/   micro-ROS node: servo/ESC/encoders/IMU, publishes /odom, fail-safe on heartbeat loss
  docs/hardware-bom.md
```

## Stack

- **ROS 2** (target: Jazzy/Humble on the Pi, Ubuntu) — the Pi is Linux; this workspace is
  built/run there, **not** on the Windows machine hosting Unity.
- **SLAM Toolbox** — 2D LiDAR mapping + localization.
- **Nav2** — global planner **Smac Hybrid-A\*** (or TEB) + controller **Regulated Pure Pursuit**
  (Ackermann-feasible; the default DWB is *not* used).
- **micro-ROS** — ESP32 ↔ Pi bridge.

## Status

Phase 1 — **design approved, implementation not started.** The package folders below are
scaffolding; each `README.md` records what will live there. No buildable code yet.

## Build / run (target: Raspberry Pi, once implemented)

```bash
cd rc_transit/ros2_ws
colcon build
source install/setup.bash
ros2 launch rc_bringup bringup.launch.py      # LiDAR + SLAM + Nav2
ros2 run rc_interface request A C             # "go from checkpoint A to C"
```

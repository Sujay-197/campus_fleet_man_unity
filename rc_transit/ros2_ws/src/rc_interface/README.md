# rc_interface

The **"request A → C"** surface — the Phase-1 way to command the car. **Not yet implemented**;
this README records the intended contents.

## Will contain

- `rc_interface/request.py` — CLI entry point: `ros2 run rc_interface request <FROM> <TO>`.
  - Loads the checkpoint pose store (`rc_bringup/config/checkpoints.yaml`).
  - Resolves the destination name → pose, sends it to Nav2 (`NavigateToPose` action).
  - Streams progress and reports **arrived** (pose within 10–15 cm) or **failed** (unreachable /
    aborted), then exits.
  - `<FROM>` is informational in Phase 1 (the car navigates from wherever it currently is);
    it exists so the request reads like a transit request and to validate the start checkpoint.
- `package.xml` / `setup.py` — ament_python package metadata.

## Depends on

`rclpy`, `nav2_msgs` (NavigateToPose action), the pose store from `rc_bringup`.

## Not here (later phases)

- Web dashboard ("request A→C" panel mirroring the sim's activity bar) — **Phase 2**.
- Multi-leg / demand-driven requests — out of scope (fleet logistics stay in the sim).

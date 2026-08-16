# rc_bringup

Launch files and parameters that stand the robot up. **Not yet implemented** — this README
records the intended contents.

## Will contain

- `launch/bringup.launch.py` — brings up, in order: LiDAR driver → SLAM Toolbox →
  robot_state_publisher (URDF) → Nav2. Optional `slam:=false map:=<file>` to run against a
  pre-saved map instead of live mapping.
- `urdf/rc_car.urdf.xacro` — Ackermann robot description (wheelbase, track, steering limits,
  LiDAR mount transform). Feeds odometry and TF.
- `config/`
  - `slam_toolbox.yaml` — 2D mapping/localization params for the room scale.
  - `nav2.yaml` — planner **Smac Hybrid-A\*** (or TEB), controller **Regulated Pure Pursuit**,
    local/global costmap layers (LiDAR obstacle + inflation), Ackermann motion constraints,
    goal tolerance **0.10–0.15 m position** (heading tolerance loose/off).
- `config/checkpoints.yaml` — the pose store: `room -> {A,B,C,D} -> (x, y, theta)` in the map frame.

## Depends on (external, installed on the Pi)

`slam_toolbox`, `nav2_bringup`, a LiDAR driver package (e.g. `rplidar_ros`), `robot_state_publisher`.

## Notes

- Default Nav2 DWB controller is **not** used — it is differential-drive-oriented and fights
  Ackermann constraints.
- Goal tolerance is position-only by design (see spec §2, §6).

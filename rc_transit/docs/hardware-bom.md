# Hardware bill of materials (Phase 1)

Draft — quantities of 1 unless noted. Fill in exact models/prices as procured.

| Category | Item | Notes |
|----------|------|-------|
| Chassis | Ackermann RC car (donor) | Front-steer + rear-drive; source its kinematics/dynamics. 1/16–1/18 scale so turning radius ≪ room. |
| Compute (companion) | Raspberry Pi 4 (4/8 GB) or Pi 5 | Runs ROS 2 + SLAM Toolbox + Nav2. No CUDA — see Phase-4 flag. |
| Compute (MCU) | ESP32 dev board | micro-ROS: servo/ESC/encoders/IMU, `/odom`, fail-safe. |
| LiDAR | 2D LiDAR (e.g. RPLIDAR A1/C1) | Localization **and** obstacle sensing; lighting-immune. |
| IMU | 6/9-DOF IMU (e.g. MPU-6050/BNO055) | Yaw fusion for odometry. |
| Encoders | Wheel/motor encoder | Rear-wheel speed for bicycle-model odometry. |
| Actuation | Steering servo + drive ESC + motor | Usually the donor RC car's existing running gear. |
| Power | LiPo + regulators (5 V for Pi, servo/ESC rail) | Budget for Pi + LiDAR draw; brownout protection. |
| Comms | WiFi (Pi ↔ PC, ROS 2 DDS) + serial/USB (ESP32 ↔ Pi) | Safety reflex stays onboard — never over WiFi. |

## Not in Phase 1 (later)

- Camera (Phase 3 perception).
- Coral/Hailo USB accelerator or Jetson Orin Nano (Phase 4 on-car neural policy).

# ESP32 firmware (micro-ROS)

Hard real-time I/O for the car. Runs on the ESP32, bridged to the Raspberry Pi via
**micro-ROS**. **Not yet implemented**; this README records the intended contents.

## Will contain

- A PlatformIO / ESP-IDF project with a micro-ROS node that:
  - **Subscribes** to a velocity/steer command (e.g. `/cmd_vel` or `/ackermann_cmd`) from Nav2
    on the Pi and drives the **steering servo** + **drive ESC** accordingly.
  - **Publishes** `/odom` from wheel-**encoder** speed + steering angle (bicycle model), fused
    with **IMU** yaw.
  - Enforces a **fail-safe**: on command-heartbeat loss (Pi link down) → **zero throttle,
    center steer** within a few tens of ms.
  - Optionally runs the lowest-level obstacle **reflex** (if a bumper/ranging sensor is wired
    directly), independent of the Pi.

## Why the ESP32 (and only this)

Sub-millisecond PWM and encoder timing are what an MCU is for. Planning, SLAM, and costmaps
live on the Pi; perception/training live on the PC. Keep the ESP32 dumb, fast, and safe.

## Toolchain (installed separately)

PlatformIO or ESP-IDF + the `micro_ros_arduino` / `micro-ROS component` for the ESP32, and a
micro-ROS agent running on the Pi.

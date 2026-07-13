using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// The actuator seam: the routing brain issues "go here" goals through this interface
    /// and knows nothing about how movement physically happens. KinematicNavigator (now)
    /// wraps BusPathFollower; a future IsaacNavigator/AWSIMNavigator with LiDAR-based
    /// obstacle avoidance can implement this same interface with zero change upstream.
    /// </summary>
    public interface IVehicleNavigator
    {
        void SetGoalPath(IReadOnlyList<Vector3> waypoints);
        bool Arrived { get; }
        event Action ReachedGoal;
    }
}

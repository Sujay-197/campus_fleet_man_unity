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
        /// <summary>Drive the vehicle to <paramref name="legFraction01"/> of the current leg (0 = start, 1 = goal).</summary>
        void UpdateTravel(float legFraction01);
        bool Arrived { get; }
        event Action ReachedGoal;
    }
}

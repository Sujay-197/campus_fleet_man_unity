using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    public class KinematicNavigator : IVehicleNavigator
    {
        readonly BusPathFollower _follower;

        public bool Arrived { get; private set; } = true;
        public event Action ReachedGoal;

        public KinematicNavigator(BusPathFollower follower)
        {
            _follower = follower;
            _follower.ReachedEndOfRoute += () =>
            {
                Arrived = true;
                ReachedGoal?.Invoke();
            };
        }

        public void SetGoalPath(IReadOnlyList<Vector3> waypoints)
        {
            Arrived = false;
            _follower.SetRoute(waypoints);
        }
    }
}

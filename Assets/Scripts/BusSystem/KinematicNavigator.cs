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
        }

        public void SetGoalPath(IReadOnlyList<Vector3> waypoints)
        {
            Arrived = false;
            _follower.SetPath(waypoints);
        }

        public void UpdateTravel(float legFraction01)
        {
            _follower.SetProgress(legFraction01);
            if (legFraction01 >= 1f && !Arrived)
            {
                Arrived = true;
                ReachedGoal?.Invoke();
            }
        }
    }
}

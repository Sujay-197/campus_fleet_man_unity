using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Kinematic waypoint follower: drives its transform along a supplied list of
    /// world-space waypoints at constant speed with smooth turning. Exposes a
    /// testable Step(dt) so movement can be verified without entering play mode.
    /// </summary>
    public class BusPathFollower : MonoBehaviour
    {
        public float Speed = 12f;
        public float TurnSpeed = 180f;   // degrees/sec
        public float StopDuration = 1f;  // pause after finishing a route
        public float ArriveThreshold = 0.15f;

        readonly List<Vector3> _route = new List<Vector3>();
        int _index;
        float _stopTimer;

        public bool HasRoute => _route.Count > 0 && _index < _route.Count;
        public event Action ReachedEndOfRoute;

        public void SetRoute(IReadOnlyList<Vector3> waypoints)
        {
            _route.Clear();
            if (waypoints != null) _route.AddRange(waypoints);
            _stopTimer = 0f;
            if (_route.Count > 0)
            {
                Vector3 p = _route[0]; p.y = transform.position.y;
                transform.position = p;
            }
            _index = _route.Count > 1 ? 1 : _route.Count;
        }

        void Update() { Step(Time.deltaTime); }

        /// <summary>Advances along the route. Returns true on the frame the route completes.</summary>
        public bool Step(float dt)
        {
            if (!HasRoute) return false;
            if (_stopTimer > 0f) { _stopTimer -= dt; return false; }

            Vector3 target = _route[_index];
            Vector3 to = target - transform.position; to.y = 0f;

            if (to.sqrMagnitude > 1e-5f)
            {
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * dt);
            }

            Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
            transform.position = Vector3.MoveTowards(transform.position, flatTarget, Speed * dt);

            if ((new Vector3(target.x, 0, target.z) - new Vector3(transform.position.x, 0, transform.position.z)).sqrMagnitude
                <= ArriveThreshold * ArriveThreshold)
            {
                _index++;
                if (_index >= _route.Count)
                {
                    _stopTimer = StopDuration;
                    ReachedEndOfRoute?.Invoke();
                    return true;
                }
            }
            return false;
        }
    }
}

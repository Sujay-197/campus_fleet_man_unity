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

        // Parametric ("driven") mode: cumulative arc-length per waypoint so the bus can be
        // positioned at any fraction of the leg. Used when an external clock (the sim) owns
        // progress; the follower no longer self-animates, so it can never teleport ahead.
        readonly List<float> _cum = new List<float>();
        float _total;
        bool _parametric;

        public bool HasRoute => _route.Count > 0 && _index < _route.Count;
        public event Action ReachedEndOfRoute;

        public void SetRoute(IReadOnlyList<Vector3> waypoints)
        {
            _parametric = false;
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

        /// <summary>
        /// Load a leg for sim-driven (parametric) playback. Unlike <see cref="SetRoute"/> this
        /// does NOT snap or self-animate — the owner calls <see cref="SetProgress"/> each frame
        /// with the leg's sim-time fraction, so the bus stays exactly on the road line and
        /// advances monotonically. Consecutive legs share their boundary node, so there is no
        /// jump between them.
        /// </summary>
        public void SetPath(IReadOnlyList<Vector3> waypoints)
        {
            _parametric = true;
            _route.Clear();
            if (waypoints != null) _route.AddRange(waypoints);
            _cum.Clear();
            _cum.Add(0f);
            float acc = 0f;
            for (int k = 1; k < _route.Count; k++)
            {
                Vector3 d = _route[k] - _route[k - 1]; d.y = 0f;
                acc += d.magnitude;
                _cum.Add(acc);
            }
            _total = acc;
            if (_route.Count > 0) SetProgress(0f);
        }

        /// <summary>Position the bus at <paramref name="fraction01"/> of the current parametric leg.</summary>
        public void SetProgress(float fraction01)
        {
            if (_route.Count == 0) return;
            if (_route.Count == 1) { PlaceAt(_route[0], _route[0]); return; }

            float target = Mathf.Clamp01(fraction01) * _total;
            int i = 1;
            while (i < _cum.Count - 1 && _cum[i] < target) i++;

            float segStart = _cum[i - 1];
            float segLen = _cum[i] - segStart;
            float t = segLen > 1e-5f ? Mathf.Clamp01((target - segStart) / segLen) : 0f;
            Vector3 pos = Vector3.Lerp(_route[i - 1], _route[i], t);
            PlaceAt(pos, _route[i]);
        }

        // Ground the bus at its authored height and face it along the road.
        void PlaceAt(Vector3 pos, Vector3 lookTarget)
        {
            pos.y = transform.position.y;
            Vector3 dir = lookTarget - pos; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.position = pos;
        }

        void Update() { if (!_parametric) Step(Time.deltaTime); }

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

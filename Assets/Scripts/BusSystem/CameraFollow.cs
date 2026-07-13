using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Isometric "eagle-eye" follow camera: holds a fixed high angle (like the
    /// editor's ISO scene view) and smoothly tracks a target so it stays centered.
    /// The offset is in world space, so the view angle never rotates with the bus.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        [Tooltip("World-space offset from the target. High + diagonal = isometric eagle-eye.")]
        public Vector3 Offset = new Vector3(40f, 60f, -40f);
        [Tooltip("Higher = snappier follow.")]
        public float FollowSharpness = 3f;
        public float LookAtHeight = 2f;
        public bool AutoFindBus = true;

        void Start()
        {
            if (Target == null && AutoFindBus)
            {
                // Anchor on the follower (the component actually on the bus). The old
                // iteration-0 BusAgent orchestrator was removed when the agent-based
                // Simulation superseded it, so finding the bus by BusAgent no longer works.
                var bus = FindObjectOfType<BusPathFollower>();
                if (bus != null) Target = bus.transform;
            }
            if (Target != null)
            {
                transform.position = Target.position + Offset;
                transform.LookAt(Target.position + Vector3.up * LookAtHeight);
            }
        }

        void LateUpdate()
        {
            if (Target == null) return;
            Vector3 desired = Target.position + Offset;
            float t = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime); // frame-rate independent
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(Target.position + Vector3.up * LookAtHeight);
        }
    }
}

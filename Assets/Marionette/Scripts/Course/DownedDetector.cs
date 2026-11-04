using UnityEngine;

namespace Marionette.Course
{
    /// <summary>
    /// Added to the head and center at spawn. If either lands on a surface the puppet is down:
    /// that is the spec's failure condition, body contact with the ground.
    /// </summary>
    public class DownedDetector : MonoBehaviour
    {
        [HideInInspector] public CourseManager manager;

        void OnCollisionEnter(Collision c)
        {
            if (manager == null || c.collider.GetComponentInParent<MarionetteRig>() != null) return;
            // Ground-like contacts only, so brushing a wall with the head doesn't end the run.
            if (Mathf.Abs(c.GetContact(0).normal.y) > 0.6f)
                manager.OnFall($"{name} hit the ground");
        }
    }
}

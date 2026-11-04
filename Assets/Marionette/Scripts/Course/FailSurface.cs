using UnityEngine;

namespace Marionette.Course
{
    /// <summary>Any puppet part touching this (spilled coffee, the floor) counts as a fall.</summary>
    public class FailSurface : MonoBehaviour
    {
        [HideInInspector] public CourseManager manager;

        void OnCollisionEnter(Collision c)
        {
            if (manager != null && c.collider.GetComponent<PuppetNode>() != null)
                manager.OnFall($"touched {name}");
        }
    }
}

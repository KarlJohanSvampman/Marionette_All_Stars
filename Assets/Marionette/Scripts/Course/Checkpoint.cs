using UnityEngine;

namespace Marionette.Course
{
    /// <summary>Trigger the puppet's center passes through. Checkpoints must be reached in order.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Checkpoint : MonoBehaviour
    {
        public int index;
        public string label;
        [Tooltip("Where the puppet respawns after falling past this checkpoint.")]
        public Transform respawnPoint;
        [Tooltip("Seconds the puppet must stay inside to count (0 = just pass through). Used for the stop test.")]
        public float dwellSeconds;
        [HideInInspector] public CourseManager manager;

        float m_Inside;

        static bool IsCenter(Collider c)
        {
            var node = c.GetComponent<PuppetNode>();
            return node != null && node.id == PuppetNodeId.Center;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsCenter(other)) return;
            m_Inside = 0f;
            if (dwellSeconds <= 0f && manager != null) manager.OnCheckpoint(this);
        }

        void OnTriggerStay(Collider other)
        {
            if (dwellSeconds <= 0f || !IsCenter(other)) return;
            float before = m_Inside;
            m_Inside += Time.fixedDeltaTime;
            if (before < dwellSeconds && m_Inside >= dwellSeconds && manager != null) manager.OnCheckpoint(this);
        }

        void OnTriggerExit(Collider other)
        {
            if (IsCenter(other)) m_Inside = 0f;
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}

using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Tracks whether a foot or hand is resting on something (anything that isn't the puppet).
    /// BalanceMonitor uses the grounded feet and hands as the puppet's support area.
    /// </summary>
    [RequireComponent(typeof(PuppetNode))]
    public class GroundContact : MonoBehaviour
    {
        [Tooltip("Contacts steeper than this (normal's up component) don't count: touching a wall isn't standing.")]
        [Range(0f, 1f)] public float minNormalUp = 0.4f;

        float m_LastContact = -1f;
        MarionetteRig m_Rig;
        Collider m_Collider;

        /// <summary>Touching a floor-like surface during the last physics step.</summary>
        public bool Grounded => m_LastContact >= 0f && Time.fixedTime - m_LastContact <= Time.fixedDeltaTime * 1.5f;

        /// <summary>Surface normal of the last floor-like contact.</summary>
        public Vector3 Normal { get; private set; } = Vector3.up;

        public Collider Collider => m_Collider != null ? m_Collider : m_Collider = GetComponent<Collider>();

        void Awake() => m_Rig = GetComponentInParent<MarionetteRig>();

        void OnCollisionEnter(Collision c) => Record(c);
        void OnCollisionStay(Collision c) => Record(c);

        void Record(Collision c)
        {
            if (m_Rig != null && c.collider.GetComponentInParent<MarionetteRig>() == m_Rig) return;
            for (int i = 0; i < c.contactCount; i++)
            {
                Vector3 n = c.GetContact(i).normal;
                if (n.y < 0f) n = -n; // normal direction depends on which side reports it
                if (n.y < minNormalUp) continue;
                m_LastContact = Time.fixedTime;
                Normal = n;
                return;
            }
        }

        /// <summary>
        /// Adds this part's footprint on the ground (XZ) to points: the four lowest corners of a
        /// box foot, or a small square under a round hand.
        /// </summary>
        public void AddFootprint(System.Collections.Generic.List<Vector2> points)
        {
            var col = Collider;
            if (col is BoxCollider box)
            {
                var corners = new Vector3[8];
                int k = 0;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    corners[k++] = transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, new Vector3(x, y, z)));
                System.Array.Sort(corners, (a, b) => a.y.CompareTo(b.y));
                for (int i = 0; i < 4; i++) points.Add(new Vector2(corners[i].x, corners[i].z));
            }
            else
            {
                Bounds b = col.bounds;
                float r = Mathf.Min(b.extents.x, b.extents.z) * 0.5f;
                Vector3 c = b.center;
                points.Add(new Vector2(c.x - r, c.z - r));
                points.Add(new Vector2(c.x + r, c.z - r));
                points.Add(new Vector2(c.x + r, c.z + r));
                points.Add(new Vector2(c.x - r, c.z + r));
            }
        }
    }
}

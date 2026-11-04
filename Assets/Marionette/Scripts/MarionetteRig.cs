using UnityEngine;

namespace Marionette
{
    /// <summary>Root component of a built puppet. Gives other systems one place to find its parts.</summary>
    public class MarionetteRig : MonoBehaviour
    {
        [Tooltip("Feet-to-crown height in metres this puppet was built at.")]
        public float height = MarionetteBuilder.ReferenceHeight;
        [Tooltip("height / reference height. Physics values in MarionetteTuning are scaled by this.")]
        public float scale = 1f;
        public MarionetteBuilder.Materials materials;
        public PuppetNode[] nodes;
        public TextileLink[] textile;
        public ControlCross leftCross;
        public ControlCross rightCross;
        public PuppetAttachment[] attachments;
        [Tooltip("Holds the head on the body (no head string).")]
        public ConfigurableJoint neckJoint;
        [Tooltip("Limit how far each hand and foot can rotate relative to the body, so they don't spin.")]
        public ConfigurableJoint[] limbJoints;

        /// <summary>Built by an older version (six strings, or no automatic steps); PuppetSpawner rebuilds these.</summary>
        public bool IsOutdated => limbJoints == null || limbJoints.Length == 0 || neckJoint == null || GetComponent<AutoStep>() == null;

        public PuppetNode Node(PuppetNodeId id)
        {
            foreach (var n in nodes)
                if (n.id == id) return n;
            return null;
        }

        /// <summary>
        /// Grow or shrink the live puppet in place by factor k about a ground pivot, without
        /// rebuilding it. Called once per physics step by SizeChanger for a smooth size change.
        /// Node shapes and their child attachments scale with the node transforms; positions are
        /// scaled about the pivot so the feet stay on the ground; lengths, masses and springs follow.
        /// </summary>
        public void ScaleBy(float k, Vector3 pivot)
        {
            height *= k;
            scale *= k;
            foreach (var n in nodes)
            {
                var rb = n.Body;
                n.transform.localScale *= k;
                Vector3 p = pivot + (rb.position - pivot) * k;
                rb.position = p;
                n.transform.position = p;
                foreach (var c in n.GetComponents<Collider>())
                    c.contactOffset = Mathf.Max(0.0005f, 0.006f * scale);
            }
            foreach (var t in textile) t.Rescale(k);
            foreach (var cross in new[] { leftCross, rightCross })
            {
                cross.Rescale(k, pivot);
                foreach (var c in cross.channels) c.controlString.Rescale(k);
            }
            if (neckJoint != null) TextileLink.RefreshAnchors(neckJoint);
            if (limbJoints != null)
                foreach (var j in limbJoints) if (j != null) TextileLink.RefreshAnchors(j);
            var tuning = GetComponent<MarionetteTuning>();
            if (tuning != null) tuning.Apply(); // masses, springs, dampers, lift ranges for the new scale
        }

        /// <summary>Point on the ground under the center node, used to respawn / resize in place.</summary>
        public Vector3 GroundPoint
        {
            get
            {
                Vector3 c = Node(PuppetNodeId.Center).Body.position;
                var hits = Physics.RaycastAll(c, Vector3.down, height * 4f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 ground = new Vector3(c.x, c.y - height * 0.5f, c.z);
                foreach (var h in hits)
                {
                    if (h.collider.GetComponentInParent<MarionetteRig>() == this) continue;
                    if (h.distance < best) { best = h.distance; ground = h.point; }
                }
                return ground;
            }
        }

        /// <summary>Mass-weighted centre of the six nodes, used later by the balance system.</summary>
        public Vector3 CenterOfMass
        {
            get
            {
                Vector3 sum = Vector3.zero;
                float mass = 0f;
                foreach (var n in nodes)
                {
                    sum += n.Body.worldCenterOfMass * n.Body.mass;
                    mass += n.Body.mass;
                }
                return mass > 0f ? sum / mass : transform.position;
            }
        }

        void OnDrawGizmos()
        {
            if (nodes == null || nodes.Length == 0) return;
            Gizmos.color = Color.yellow;
            Vector3 com = CenterOfMass;
            Gizmos.DrawSphere(com, 0.012f);
            Gizmos.DrawLine(com, new Vector3(com.x, transform.position.y, com.z));
        }
    }
}

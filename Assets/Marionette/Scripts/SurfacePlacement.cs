using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Foot and hand placement: when a foot (or hand) comes close to the surface below it, it turns to
    /// lie flat on that surface (a ramp, a step) instead of landing on an edge or corner.
    ///
    /// The puppet has no rigid legs or arms, so this is what "IK" means for it: a gentle physical
    /// spring toward the surface angle, strongest at contact and fading out with height. It only
    /// tilts (pitch and roll); which way the foot points is left to the strings. The puppet can still fall.
    /// </summary>
    [RequireComponent(typeof(PuppetNode))]
    public class SurfacePlacement : MonoBehaviour
    {
        [Tooltip("Starts turning toward the surface when this close to it, in puppet heights.")]
        [Range(0.01f, 0.5f)] public float reach = 0.1f;
        [Tooltip("How quickly it turns to lie flat, Hz.")]
        [Range(0.5f, 15f)] public float frequency = 6f;
        [Tooltip("1 = settles without overshoot.")]
        [Range(0f, 2f)] public float dampingRatio = 1f;

        static readonly RaycastHit[] s_Hits = new RaycastHit[8];

        MarionetteRig m_Rig;
        Rigidbody m_Body;
        Collider m_Collider;
        ConfigurableJoint m_Ankle;      // feet: the sprung joint to the body; placement moves its target
        Quaternion m_RestRelative;      // rotation relative to the body as built
        BalanceMonitor m_Balance;

        /// <summary>Angle between the part's up and the surface below, degrees. 0 = flat. -1 = no surface in reach.</summary>
        public float Misalignment { get; private set; } = -1f;

        void Awake()
        {
            m_Rig = GetComponentInParent<MarionetteRig>();
            m_Body = GetComponent<Rigidbody>();
            m_Collider = GetComponent<Collider>();
        }

        // Found on first use: when a puppet is built at runtime, Awake runs before its joints exist.
        bool m_LookedForAnkle;

        void FindAnkle()
        {
            m_LookedForAnkle = true;
            var node = GetComponent<PuppetNode>();
            if (m_Rig.limbJoints == null || !(node.id is PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot)) return;
            foreach (var j in m_Rig.limbJoints)
                if (j != null && j.gameObject == gameObject) m_Ankle = j;
            // The joint's own rest pose: how the foot sat on the body when the joint was made.
            if (m_Ankle != null)
                m_RestRelative = Quaternion.Inverse(m_Ankle.connectedBody.rotation) * m_Body.rotation;
        }

        /// <summary>
        /// Feet: steer the ankle spring toward the surface angle (proper foot IK). weight 0 = rest angle.
        /// Joint target rotations are relative to the start pose: target = inverse(desired) * start.
        /// </summary>
        void AimAnkle(Vector3 normal, float weight)
        {
            // Relative to the body standing UPRIGHT (same heading), not the body as it leans now. If the
            // target followed the lean, the ankle would stop pushing the body back up over the feet.
            // The player's left palm can ask for a lean: the ankles then hold the body leaning that way.
            Vector3 heading = Vector3.ProjectOnPlane(m_Ankle.connectedBody.rotation * Vector3.forward, Vector3.up);
            Quaternion upright = heading.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(heading, Vector3.up) : Quaternion.identity;
            if (m_Balance == null) m_Balance = m_Rig.GetComponent<BalanceMonitor>();
            if (m_Balance != null) upright = m_Balance.LeanTarget * upright;
            Quaternion rest = upright * m_RestRelative;
            Quaternion flat = Quaternion.FromToRotation(rest * Vector3.up, normal) * rest;
            Quaternion desired = Quaternion.Slerp(rest, flat, weight);
            Quaternion desiredRelative = Quaternion.Inverse(upright) * desired;
            m_Ankle.targetRotation = Quaternion.Inverse(desiredRelative) * m_RestRelative;
        }

        void OnDisable()
        {
            if (m_Ankle != null) m_Ankle.targetRotation = Quaternion.identity; // back to the plain ankle spring
        }

        void FixedUpdate()
        {
            Misalignment = -1f;
            if (m_Rig == null) return;
            if (!m_LookedForAnkle) FindAnkle();
            float h = m_Rig.height;
            float half = m_Collider.bounds.extents.y;
            float range = reach * h;
            Vector3 origin = m_Body.position;

            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_Hits, half + range, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (s_Hits[i].collider.GetComponentInParent<MarionetteRig>() == m_Rig) continue;
                if (best < 0 || s_Hits[i].distance < s_Hits[best].distance) best = i;
            }
            if (best < 0)
            {
                if (m_Ankle != null) m_Ankle.targetRotation = Quaternion.identity;
                return;
            }

            Vector3 normal = s_Hits[best].normal;
            Vector3 up = m_Body.rotation * Vector3.up;
            Misalignment = Vector3.Angle(up, normal);

            // Full strength at contact, fading to zero at the edge of reach.
            float gap = Mathf.Max(0f, s_Hits[best].distance - half);
            float weight = 1f - Mathf.Clamp01(gap / range);

            if (m_Ankle != null)
            {
                AimAnkle(normal, weight);
                return;
            }
            if (Vector3.Dot(up, normal) <= 0f) return; // upside down: leave it to the strings

            Vector3 axis = Vector3.Cross(up, normal);
            float sin = axis.magnitude;
            if (sin < 1e-5f) return;
            Vector3 error = axis / sin * Mathf.Asin(Mathf.Min(1f, sin));

            float omega = 2f * Mathf.PI * frequency;
            Vector3 spin = m_Body.angularVelocity;
            spin -= Vector3.Project(spin, normal); // don't damp turning about the surface normal
            Vector3 acceleration = (error * omega * omega - spin * (2f * dampingRatio * omega)) * weight;
            m_Body.AddTorque(acceleration, ForceMode.Acceleration);
        }
    }
}

using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Flexible cloth connection between two nodes.
    /// Stretch: a soft distance limit, the cloth cannot stretch much past its length.
    /// Compression: a weak push-apart spring below minLengthRatio, so a sleeve resists folding
    /// a little instead of collapsing to nothing (high values give the stiff Prototype B feel).
    /// Visually it is a thick line that sags when slack.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TextileLink : MonoBehaviour
    {
        public PuppetNode from;
        public PuppetNode to;
        public PuppetAttachment fromPoint;
        public PuppetAttachment toPoint;
        [Tooltip("Distance between the two points when the puppet was built.")]
        public float restLength = 0.2f;
        [Tooltip("Max length of the cloth (rest length * (1 + slack)).")]
        public float length = 0.2f;
        [Tooltip("Below restLength * this ratio the cloth starts pushing back.")]
        public float minLengthRatio = 0.6f;
        public float compressStiffness = 40f;
        public float compressDamper = 2f;

        [SerializeField, HideInInspector] ConfigurableJoint m_Joint;

        const int k_Segments = 10;
        LineRenderer m_Line;

        public void Configure(float clothRestLength, float slack)
        {
            restLength = clothRestLength;

            m_Joint = to.gameObject.AddComponent<ConfigurableJoint>();
            m_Joint.connectedBody = from.Body;
            m_Joint.autoConfigureConnectedAnchor = false;
            m_Joint.anchor = toPoint.LocalToNode;
            m_Joint.connectedAnchor = fromPoint.LocalToNode;
            m_Joint.xMotion = m_Joint.yMotion = m_Joint.zMotion = ConfigurableJointMotion.Limited;
            m_Joint.angularXMotion = m_Joint.angularYMotion = m_Joint.angularZMotion = ConfigurableJointMotion.Free;
            m_Joint.enableCollision = false;
            SetStretch(slack, 3000f, 40f);
        }

        /// <summary>Spring/damper make the limit soft, which avoids the jitter of a hard rope limit.</summary>
        public void SetStretch(float slack, float spring, float damper)
        {
            length = restLength * (1f + slack);
            if (m_Joint == null) return;
            m_Joint.linearLimit = new SoftJointLimit { limit = m_Loose ? restLength * 10f : length };
            m_Joint.linearLimitSpring = new SoftJointLimitSpring { spring = spring, damper = damper };
        }

        bool m_Loose;

        /// <summary>
        /// Loose cloth neither stops stretching nor pushes back: a flicked limb moves freely and the body
        /// gets only the flick's calculated momentum, not a yank from the cloth.
        /// </summary>
        public void SetLoose(bool loose)
        {
            m_Loose = loose;
            if (m_Joint != null) m_Joint.linearLimit = new SoftJointLimit { limit = loose ? restLength * 10f : length };
        }

        public bool IsLoose => m_Loose;

        /// <summary>Called every physics step while the puppet grows or shrinks.</summary>
        public void Rescale(float k)
        {
            restLength *= k;
            length *= k;
            if (m_Joint != null)
            {
                m_Joint.linearLimit = new SoftJointLimit { limit = length };
                RefreshAnchors(m_Joint);
            }
            var line = GetComponent<LineRenderer>();
            line.widthMultiplier *= k;
        }

        /// <summary>
        /// Joints cache their frames using the body's scale at the time the anchor was set,
        /// so after a node's scale changes the anchors must be re-assigned to take effect.
        /// Re-setting the anchor alone doesn't do it; detaching and reattaching the connected
        /// body makes Unity rebuild both joint frames with the current scales.
        /// </summary>
        public static void RefreshAnchors(Joint j)
        {
            Vector3 a = j.anchor, c = j.connectedAnchor;
            var body = j.connectedBody;
            j.connectedBody = null;
            j.connectedBody = body;
            j.anchor = a;
            j.connectedAnchor = c;
        }

        void Awake()
        {
            m_Line = GetComponent<LineRenderer>();
            m_Line.positionCount = k_Segments + 1;
            m_Line.useWorldSpace = true;
        }

        void FixedUpdate()
        {
            if (m_Loose) return;
            float minLength = restLength * minLengthRatio;
            if (compressStiffness <= 0f || minLength <= 0f) return;

            Vector3 a = fromPoint.transform.position;
            Vector3 b = toPoint.transform.position;
            Vector3 delta = b - a;
            float d = delta.magnitude;
            if (d >= minLength || d < 1e-5f) return;

            Vector3 dir = delta / d;
            Rigidbody ra = from.Body, rb = to.Body;
            float closing = Vector3.Dot(rb.GetPointVelocity(b) - ra.GetPointVelocity(a), dir);
            float force = compressStiffness * (minLength - d) - compressDamper * closing;
            if (force <= 0f) return;
            rb.AddForceAtPosition(dir * force, b);
            ra.AddForceAtPosition(-dir * force, a);
        }

        void LateUpdate()
        {
            Vector3 a = fromPoint.transform.position;
            Vector3 b = toPoint.transform.position;
            float d = Vector3.Distance(a, b);
            // Approximate cloth sag: the slacker the link, the further the middle droops.
            float sag = d < length ? Mathf.Sqrt(length * length - d * d) * 0.5f : 0f;
            for (int i = 0; i <= k_Segments; i++)
            {
                float t = i / (float)k_Segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                p += Vector3.down * (sag * 4f * t * (1f - t));
                m_Line.SetPosition(i, p);
            }
        }
    }
}

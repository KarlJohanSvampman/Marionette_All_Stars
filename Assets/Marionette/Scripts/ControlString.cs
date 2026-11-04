using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// One external marionette string. The top end is a kinematic handle that the control cross
    /// moves; the bottom end is a rope constraint on an outside attachment of a node.
    /// A rope only pulls: when the handle comes closer than the length, the string goes slack.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ControlString : MonoBehaviour
    {
        public PuppetAttachment attachment;
        public Rigidbody handle;
        public float length = 0.6f;

        [Tooltip("Drawn in the highlight colour, e.g. while selected by a pinch.")]
        [System.NonSerialized] public bool highlighted;
        public static readonly Color HighlightColor = new Color(1f, 0.85f, 0.1f);

        [SerializeField, HideInInspector] ConfigurableJoint m_Joint;
        LineRenderer m_Line;

        public void Configure(float stringLength)
        {
            length = stringLength;

            m_Joint = attachment.node.gameObject.AddComponent<ConfigurableJoint>();
            m_Joint.connectedBody = handle;
            m_Joint.autoConfigureConnectedAnchor = false;
            m_Joint.anchor = attachment.LocalToNode;
            m_Joint.connectedAnchor = Vector3.zero;
            m_Joint.xMotion = m_Joint.yMotion = m_Joint.zMotion = ConfigurableJointMotion.Limited;
            m_Joint.angularXMotion = m_Joint.angularYMotion = m_Joint.angularZMotion = ConfigurableJointMotion.Free;
            m_Joint.enableCollision = false;
            SetElasticity(4000f, 60f);
            ApplyLength();
        }

        void Awake()
        {
            m_Line = GetComponent<LineRenderer>();
            m_Line.positionCount = 2;
            m_Line.useWorldSpace = true;
        }

        /// <summary>
        /// A soft limit: a stiff spring with damping instead of a hard rope. A 2 kg center on a
        /// 4000 N/m string stretches ~5 mm, invisible, but it removes the hard-limit jitter.
        /// </summary>
        public void SetElasticity(float spring, float damper)
        {
            if (m_Joint != null)
                m_Joint.linearLimitSpring = new SoftJointLimitSpring { spring = spring, damper = damper };
        }

        public void SetLength(float newLength)
        {
            if (Mathf.Approximately(newLength, length)) return;
            length = Mathf.Max(0.01f, newLength);
            ApplyLength();
        }

        /// <summary>Called every physics step while the puppet grows or shrinks.</summary>
        public void Rescale(float k)
        {
            length *= k;
            ApplyLength();
            if (m_Joint != null) TextileLink.RefreshAnchors(m_Joint);
        }

        /// <summary>Call from FixedUpdate.</summary>
        public void MoveTop(Vector3 worldPosition)
        {
            handle.MovePosition(worldPosition);
        }

        void ApplyLength()
        {
            if (m_Joint != null) m_Joint.linearLimit = new SoftJointLimit { limit = length };
        }

        public bool IsTaut => Vector3.Distance(handle.position, attachment.transform.position) >= length * 0.98f;

        void LateUpdate()
        {
            m_Line.SetPosition(0, handle.transform.position);
            m_Line.SetPosition(1, attachment.transform.position);
            // Slack strings read dimmer so the player can see which ones carry weight.
            float v = IsTaut ? 1f : 0.45f;
            m_Line.startColor = m_Line.endColor = highlighted ? HighlightColor : new Color(v, v, v, 1f);
        }
    }
}

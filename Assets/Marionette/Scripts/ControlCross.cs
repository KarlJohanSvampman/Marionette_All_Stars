using System;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// One player hand = one control cross with three string channels.
    ///
    /// The core coupling of the control model lives here:
    ///     string top = (hand transform H) applied to the channel's offset  +  world up * (finger value F) * liftRange
    /// So moving the hand across the horizontal plane drags every string of this cross along,
    /// while a finger on the same hand lifts its own string vertically at the same time.
    /// Example: right hand slides forward while the ring finger lifts -> the foot moves forward AND up.
    /// </summary>
    public class ControlCross : MonoBehaviour
    {
        [Serializable]
        public class Channel
        {
            public string name;
            public ControlString controlString;
            [Tooltip("Where this string leaves the cross, in cross-local space.")]
            public Vector3 topOffset;
            [Tooltip("Metres the string top rises at full finger value.")]
            public float liftRange = 0.25f;
            [HideInInspector] public float value;
        }

        public ControlSide side;
        public ControlInputSource input;
        [Tooltip("Neutral pose of the cross. Input translation/yaw are applied relative to this.")]
        public Transform neutral;
        [Tooltip("Multiplies hand translation, so small real hand motions cover more puppet space.")]
        public float translationGain = 1f;
        [Tooltip("How quickly finger values are smoothed (per second). Higher = snappier.")]
        public float channelResponse = 12f;
        public Channel[] channels = Array.Empty<Channel>();

        CrossInput m_Input;
        Vector3 m_Translation;
        float m_Yaw;
        Quaternion m_Tilt = Quaternion.identity;

        /// <summary>Current offset of the cross from its neutral pose, in world metres.</summary>
        public Vector3 Translation => m_Translation;

        /// <summary>Current twist of the cross around world up relative to its neutral pose, degrees.</summary>
        public float Yaw => m_Yaw;

        /// <summary>Current palm tilt applied to the cross (world space). BalanceMonitor reads the left one as upper-body lean.</summary>
        public Quaternion Tilt => m_Tilt;

        /// <summary>
        /// Set while the puppet is tripping or toppling: input is ignored, the cross holds its last pose
        /// and fingers relax, and the momentum runs its course.
        /// </summary>
        [NonSerialized] public bool overridden;

        /// <summary>When false the hand's yaw is still read (Yaw) but doesn't turn the cross: StepTurn uses it to turn the puppet instead.</summary>
        [NonSerialized] public bool applyYaw = true;

        void FixedUpdate()
        {
            // Allocated lazily: when the puppet is built at runtime, Awake runs before channels exist.
            if (m_Input.Channels == null || m_Input.Channels.Length != channels.Length)
                m_Input.Channels = new float[channels.Length];

            m_Input.Tilt = default; // sources that don't tilt (keyboard) leave it unset = level
            // Read even when overridden, so inputs keep their own state (calibration, pinch takes) current.
            bool read = input != null && input.Read(side, ref m_Input) && m_Input.Tracked;
            if (overridden) m_Input.Tracked = false; // fingers relax, pose holds
            else if (read)
            {
                m_Translation = m_Input.Translation * translationGain;
                m_Yaw = m_Input.Yaw;
                m_Tilt = m_Input.TiltOrIdentity;
            }
            // When tracking is lost the cross holds its last pose and fingers relax.
            float k = 1f - Mathf.Exp(-channelResponse * Time.fixedDeltaTime);
            for (int i = 0; i < channels.Length; i++)
            {
                float target = m_Input.Tracked ? Mathf.Clamp01(m_Input.Channels[i]) : 0f;
                channels[i].value = Mathf.Lerp(channels[i].value, target, k);
            }

            // Tilting the palm tilts the cross: one side's strings rise, the other's drop. That's how
            // the player counters a lean, like tipping a real marionette control bar.
            transform.SetPositionAndRotation(
                neutral.position + m_Translation,
                m_Tilt * Quaternion.AngleAxis(applyYaw ? m_Yaw : 0f, Vector3.up) * neutral.rotation);

            foreach (var c in channels)
            {
                Vector3 top = transform.TransformPoint(c.topOffset) + Vector3.up * (c.value * c.liftRange);
                c.controlString.MoveTop(top);
            }
        }

        /// <summary>
        /// Scale the cross about a ground pivot while the puppet grows or shrinks. The cross's own
        /// scale carries the string offsets and the visible bars; the neutral pose moves outward.
        /// </summary>
        public void Rescale(float k, Vector3 pivot)
        {
            neutral.position = pivot + (neutral.position - pivot) * k;
            transform.localScale *= k;
            m_Translation *= k;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = side == ControlSide.Left ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.5f, 0.3f);
            foreach (var c in channels)
            {
                Vector3 p = transform.TransformPoint(c.topOffset);
                Gizmos.DrawLine(transform.position, p);
                Gizmos.DrawWireCube(p, Vector3.one * 0.02f);
            }
        }
    }
}

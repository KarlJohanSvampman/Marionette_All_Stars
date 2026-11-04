using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Turning on the spot, one step at a time. Rotate the right hand about the vertical (yaw) past
    /// yawThreshold while the puppet stands still, and it turns stepAngle that way: clockwise (seen from
    /// above) steps right, counter-clockwise steps left.
    ///
    /// Each turn: the foot on the turning side steps round, then the other foot closes up beside it,
    /// while the body twists to the new heading and the strings' resting poses swing round with it, so
    /// the follow view comes round behind the puppet. Then it waits interval seconds; if the hand is still
    /// rotated, it takes the next turn.
    ///
    /// The right hand's yaw no longer twists the leg strings (ControlCross.applyYaw), it only turns.
    /// </summary>
    [RequireComponent(typeof(MarionetteRig))]
    public class StepTurn : MonoBehaviour
    {
        [Tooltip("Rotate the right hand this many degrees about the vertical to start turning.")]
        [Range(5f, 60f)] public float yawThreshold = 15f;
        [Tooltip("Degrees the puppet turns per turn step.")]
        [Range(5f, 90f)] public float stepAngle = 30f;
        [Tooltip("Seconds to wait after a turn before checking the hand again.")]
        [Range(0f, 3f)] public float interval = 1f;
        [Tooltip("How quickly the body twists to the new heading, Hz.")]
        [Range(0.5f, 6f)] public float twistResponse = 2.5f;

        MarionetteRig m_Rig;
        AutoStep m_Steps;
        BalanceMonitor m_Balance;

        enum Phase { Idle, Lead, Trail, Wait }
        Phase m_Phase;
        int m_Lead;           // 0 left foot, 1 right foot
        float m_Sign, m_Heading, m_Clock, m_Swung;
        Vector3 m_Pivot;

        /// <summary>Turns completed since spawning.</summary>
        public int Turns { get; private set; }
        public bool Turning => m_Phase is Phase.Lead or Phase.Trail;

        void Awake()
        {
            m_Rig = GetComponent<MarionetteRig>();
            m_Steps = GetComponent<AutoStep>();
            m_Balance = GetComponent<BalanceMonitor>();
        }

        void Start()
        {
            if (m_Rig.rightCross != null) m_Rig.rightCross.applyYaw = false;
        }

        void FixedUpdate()
        {
            if (m_Steps == null || m_Rig.rightCross == null || m_Rig.leftCross == null) return;
            float dt = Time.fixedDeltaTime;
            bool overridden = m_Balance != null && m_Balance.ControlsOverridden;
            if (overridden) { m_Phase = Phase.Idle; return; }

            switch (m_Phase)
            {
                case Phase.Idle:
                    float yaw = m_Rig.rightCross.Yaw;
                    if (Mathf.Abs(yaw) < yawThreshold || !StandingStill()) return;
                    Begin(Mathf.Sign(yaw));
                    break;
                case Phase.Lead:
                    Twist(dt);
                    if (!m_Steps.Stepping) { StepFoot(1 - m_Lead); m_Phase = Phase.Trail; }
                    break;
                case Phase.Trail:
                    Twist(dt);
                    if (!m_Steps.Stepping) { m_Phase = Phase.Wait; m_Clock = 0f; Turns++; }
                    break;
                case Phase.Wait:
                    Twist(dt);
                    if ((m_Clock += dt) >= interval) m_Phase = Phase.Idle;
                    break;
            }
        }

        bool StandingStill()
        {
            if (m_Steps.Stepping || m_Steps.IsHeld(0) || m_Steps.IsHeld(1)) return false;
            if (m_Balance != null && m_Balance.Current is not (BalanceMonitor.State.Stable or BalanceMonitor.State.Unstable)) return false;
            if (m_Balance != null && m_Balance.GroundedFeet < 2) return false;
            Vector3 v = m_Rig.Node(PuppetNodeId.Center).Body.linearVelocity;
            v.y = 0f;
            return v.magnitude < 0.3f * m_Rig.height;
        }

        void Begin(float sign)
        {
            m_Sign = sign;
            m_Lead = sign > 0f ? 1 : 0; // turning right (clockwise from above) leads with the right foot
            Vector3 l = m_Steps.Foot(0).position, r = m_Steps.Foot(1).position;
            m_Pivot = (l + r) * 0.5f;
            m_Heading = HeadingOf(m_Rig.Node(PuppetNodeId.Center).Body) + sign * stepAngle;
            m_Swung = 0f;
            StepFoot(m_Lead);
            m_Phase = Phase.Lead;
        }

        /// <summary>Step a foot to its stance place around the pivot at the new heading.</summary>
        void StepFoot(int leg)
        {
            var foot = m_Steps.Foot(leg);
            float half = Vector3.Distance(m_Steps.Foot(0).position, m_Steps.Foot(1).position) * 0.5f;
            half = Mathf.Clamp(half, 0.08f * m_Rig.height, 0.2f * m_Rig.height);
            Vector3 right = Quaternion.Euler(0f, m_Heading, 0f) * Vector3.right;
            Vector3 target = m_Pivot + right * ((leg == 1 ? 1f : -1f) * half);
            target.y = foot.GetComponent<Collider>().bounds.min.y;
            m_Steps.Begin(leg, target, stagger: false);
        }

        /// <summary>Body twists to the new heading; the strings' resting poses (and so the view) swing round with it.</summary>
        void Twist(float dt)
        {
            var center = m_Rig.Node(PuppetNodeId.Center).Body;
            float error = Mathf.DeltaAngle(HeadingOf(center), m_Heading) * Mathf.Deg2Rad;
            float omega = 2f * Mathf.PI * twistResponse;
            float accel = error * omega * omega - center.angularVelocity.y * 2f * omega;
            var head = m_Rig.Node(PuppetNodeId.Head).Body;
            float inertia = center.inertiaTensor.y + head.inertiaTensor.y; // the head turns with the body
            center.AddTorque(Vector3.up * (accel * inertia), ForceMode.Force);

            // Swing the strings round over the turn, at most stepAngle in total.
            float remaining = m_Sign * stepAngle - m_Swung;
            if (Mathf.Abs(remaining) < 1e-3f) return;
            float step = Mathf.Clamp(remaining, -stepAngle * 2f * dt, stepAngle * 2f * dt);
            m_Swung += step;
            var turn = Quaternion.AngleAxis(step, Vector3.up);
            foreach (var cross in new[] { m_Rig.leftCross, m_Rig.rightCross })
            {
                Vector3 p = cross.neutral.position;
                Vector3 pivot = new Vector3(m_Pivot.x, p.y, m_Pivot.z);
                cross.neutral.position = pivot + turn * (p - pivot);
                cross.neutral.rotation = turn * cross.neutral.rotation;
            }
        }

        static float HeadingOf(Rigidbody body)
        {
            Vector3 f = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            return f.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}

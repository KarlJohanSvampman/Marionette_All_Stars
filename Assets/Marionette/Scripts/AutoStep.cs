using System;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Automatic steps (the legs' IK). BalanceMonitor decides when a step is needed and where to;
    /// this moves the foot there: a quick arc, lifted at mid-step, landing flat (SurfacePlacement).
    ///
    /// Each foot is sprung to the body (MarionetteRig.limbJoints). During a swing that spring follows the
    /// foot, so it doesn't fight the step. On landing the foot's new place becomes the spring's rest, then
    /// eases back to the built stance (legRecenter): with the foot planted, that draws the body forward over
    /// it, and with the foot unweighted it draws the foot back under the body. Each landing absorbs part of
    /// the body's momentum, except while staggering.
    /// </summary>
    [RequireComponent(typeof(MarionetteRig))]
    public class AutoStep : MonoBehaviour
    {
        [Tooltip("Seconds per step at gnome size. Smaller puppets step faster (scaled by the square root of size).")]
        [Range(0.1f, 1f)] public float stepSeconds = 0.45f;
        [Tooltip("How high the foot lifts mid-step, in puppet heights.")]
        [Range(0f, 0.4f)] public float liftHeight = 0.1f;
        [Tooltip("How tightly the foot follows the step arc, Hz.")]
        [Range(2f, 20f)] public float response = 10f;
        [Tooltip("Fraction of the body's horizontal speed lost on each landing (not while staggering).")]
        [Range(0f, 1f)] public float absorb = 0.35f;
        [Tooltip("Same, for stumbling steps while staggering: small, so a hard shove takes several steps to die out.")]
        [Range(0f, 1f)] public float staggerAbsorb = 0.15f;
        [Tooltip("Stumbling steps take this fraction of a normal step's time: a stagger is hurried.")]
        [Range(0.3f, 1f)] public float staggerStepTime = 0.6f;
        [Tooltip("How quickly a leg settles back to its normal stance after a step, per second.")]
        public float legRecenter = 2.5f;

        MarionetteRig m_Rig;
        ConfigurableJoint[] m_Legs;     // left foot, right foot
        Vector3[] m_RestAnchor;         // built stance, in the body's local space

        // Active step
        JointDrive m_SwingDrive;        // the stepping leg's spring, switched off during the swing
        JointDrive m_SwingAnkle;        // and its ankle, softened: a lifted foot hangs loose
        int m_Leg = -1;
        Vector3 m_From, m_To;
        float m_Time, m_Duration;
        bool m_Stagger;

        public bool Stepping => m_Leg >= 0;
        readonly bool[] m_Held = new bool[2];
        /// <summary>A foot the player is dragging (StringFlick): automatic steps leave it alone.</summary>
        public void SetHeld(int leg, bool held) { if (leg is 0 or 1) m_Held[leg] = held; }
        public bool IsHeld(int leg) => leg is 0 or 1 && m_Held[leg];
        public int StepCount { get; private set; }
        /// <summary>Seconds one step takes at this puppet's size.</summary>
        public float Duration => stepSeconds * Mathf.Sqrt(m_Rig != null ? m_Rig.scale : 1f);
        public event Action<PuppetNodeId> Landed;

        void Awake() => m_Rig = GetComponent<MarionetteRig>();

        bool Init()
        {
            if (m_Legs != null) return true;
            if (m_Rig.limbJoints == null) return false;
            m_Legs = new ConfigurableJoint[2];
            m_RestAnchor = new Vector3[2];
            foreach (var j in m_Rig.limbJoints)
            {
                if (j == null) continue;
                var id = j.GetComponent<PuppetNode>().id;
                int i = id == PuppetNodeId.LeftFoot ? 0 : id == PuppetNodeId.RightFoot ? 1 : -1;
                if (i < 0) continue;
                m_Legs[i] = j;
                m_RestAnchor[i] = j.connectedAnchor;
            }
            return m_Legs[0] != null && m_Legs[1] != null;
        }

        public Rigidbody Foot(int leg) => Init() ? m_Legs[leg].GetComponent<Rigidbody>() : null;

        /// <summary>Step foot `leg` (0 left, 1 right) to a point on the ground.</summary>
        public void Begin(int leg, Vector3 target, bool stagger)
        {
            if (!Init()) return;
            if (m_Leg >= 0) RestoreDrive();
            m_Leg = leg;
            // Spring and damper off while swinging: the damper alone would drag the body along at the foot's speed.
            m_SwingDrive = m_Legs[leg].xDrive;
            var free = m_SwingDrive; free.positionSpring = 0f; free.positionDamper = 0f;
            m_Legs[leg].xDrive = m_Legs[leg].yDrive = m_Legs[leg].zDrive = free;
            // A stiff ankle on a lifted foot twists the body as the foot swings; let it hang loose instead.
            m_SwingAnkle = m_Legs[leg].slerpDrive;
            var loose = m_SwingAnkle; loose.positionSpring *= 0.05f; loose.positionDamper *= 0.05f;
            m_Legs[leg].slerpDrive = loose;
            m_Legs[leg].angularXMotion = m_Legs[leg].angularYMotion = m_Legs[leg].angularZMotion = ConfigurableJointMotion.Free;
            m_Legs[leg].xMotion = m_Legs[leg].yMotion = m_Legs[leg].zMotion = ConfigurableJointMotion.Free;
            m_From = Foot(leg).position;
            m_To = target + Vector3.up * (m_From.y - FootBottom(leg)); // target is on the ground; aim the foot's centre
            m_Time = 0f;
            m_Duration = Duration * (stagger ? staggerStepTime : 1f);
            m_Stagger = stagger;
        }

        float FootBottom(int leg)
        {
            var col = m_Legs[leg].GetComponent<Collider>();
            return col.bounds.min.y;
        }

        void FixedUpdate()
        {
            if (!Init()) return;
            float dt = Time.fixedDeltaTime;
            Rigidbody body = m_Rig.Node(PuppetNodeId.Center).Body;

            // Legs settle back toward the built stance (not the one mid-swing).
            float k = 1f - Mathf.Exp(-legRecenter * dt);
            for (int i = 0; i < 2; i++)
                if (i != m_Leg)
                    m_Legs[i].connectedAnchor = Vector3.Lerp(m_Legs[i].connectedAnchor, m_RestAnchor[i], k);

            if (m_Leg < 0) return;
            var foot = Foot(m_Leg);
            m_Time += dt;
            float t = Mathf.Clamp01(m_Time / m_Duration);
            float s = t * t * (3f - 2f * t);
            Vector3 target = Vector3.Lerp(m_From, m_To, s) + Vector3.up * (liftHeight * m_Rig.height * Mathf.Sin(t * Mathf.PI));
            float ds = 6f * t * (1f - t) / m_Duration;
            Vector3 targetVelocity = (m_To - m_From) * ds + Vector3.up * (liftHeight * m_Rig.height * Mathf.PI / m_Duration * Mathf.Cos(t * Mathf.PI));

            // Spring to the body follows the swinging foot, so it doesn't fight the step.
            m_Legs[m_Leg].connectedAnchor = body.transform.InverseTransformPoint(foot.position);

            float omega = 2f * Mathf.PI * response;
            Vector3 accel = (target - foot.worldCenterOfMass) * (omega * omega) + (targetVelocity - foot.linearVelocity) * (2f * omega);
            accel = Vector3.ClampMagnitude(accel, 300f * m_Rig.height);
            var tuning = m_Rig.GetComponent<MarionetteTuning>();
            accel -= Physics.gravity * (tuning != null ? tuning.gravityScale : 1f); // the leg carries its own weight
            foot.AddForce(accel * foot.mass, ForceMode.Force);

            if (t >= 1f) Land();
        }

        void RestoreDrive()
        {
            if (m_Leg < 0) return;
            m_Legs[m_Leg].xDrive = m_Legs[m_Leg].yDrive = m_Legs[m_Leg].zDrive = m_SwingDrive;
            m_Legs[m_Leg].slerpDrive = m_SwingAnkle;
            m_Legs[m_Leg].angularXMotion = m_Legs[m_Leg].angularYMotion = m_Legs[m_Leg].angularZMotion = ConfigurableJointMotion.Limited;
            m_Legs[m_Leg].xMotion = m_Legs[m_Leg].yMotion = m_Legs[m_Leg].zMotion = ConfigurableJointMotion.Limited;
        }

        void Land()
        {
            int leg = m_Leg;
            RestoreDrive();
            m_Leg = -1;
            StepCount++;
            float keep = 1f - (m_Stagger ? staggerAbsorb : absorb);
            foreach (var id in new[] { PuppetNodeId.Center, PuppetNodeId.Head })
            {
                var rb = m_Rig.Node(id).Body;
                Vector3 v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(v.x * keep, v.y, v.z * keep);
            }
            Landed?.Invoke(leg == 0 ? PuppetNodeId.LeftFoot : PuppetNodeId.RightFoot);
        }

        /// <summary>Cancel any step and put both legs back to their built stance (after a fall or respawn).</summary>
        public void ResetLegs()
        {
            if (!Init()) return;
            RestoreDrive();
            m_Leg = -1;
            for (int i = 0; i < 2; i++) m_Legs[i].connectedAnchor = m_RestAnchor[i];
        }
    }
}

using Unity.XR.CoreUtils;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Third-person VR view: keeps the player behind the puppet and a little above it, moving and
    /// turning with it.
    ///
    /// Position: the view follows the strings' resting point (the crosses' neutral poses), not the
    /// puppet's body, so it doesn't bob with every step. Travel moves that resting point, so the
    /// view glides along when you travel.
    ///
    /// Turning: the view faces the way the strings' resting poses face. StepTurn swings those round when
    /// the right hand's yaw turns the puppet, so the view comes round behind it. (The older smooth turn,
    /// twisting both hands, is still there behind twistToTurn.)
    ///
    /// Your own head movement still shows (lean in to look closer). Standing somewhere else in the
    /// room or crouching is corrected slowly (headRecenter).
    /// </summary>
    [DefaultExecutionOrder(100)] // after ViewScaler, so the final position is ours
    [RequireComponent(typeof(PuppetSpawner))]
    public class PuppetFollowView : MonoBehaviour
    {
        public XROrigin origin;

        [Header("Where the view sits, in puppet heights")]
        public float distanceBehind = 1.5f;
        [Tooltip("Eye height above the puppet's feet.")]
        public float eyeHeight = 1.6f;

        [Header("Smoothing (per second)")]
        public float followSharpness = 3f;
        public float heightSharpness = 2f;
        [Tooltip("How quickly standing elsewhere in the room or crouching is corrected. Low = head movement stays free.")]
        public float headRecenter = 0.5f;

        [Header("Turning (hand tracking)")]
        [Tooltip("Old turning: twist both hands to turn the view and strings smoothly. Off by default: the right hand's yaw " +
                 "turns the puppet a step at a time instead (StepTurn), and the view follows.")]
        public bool twistToTurn = false;
        [Tooltip("Twist both hands past this many degrees to turn the view and the puppet's strings with it.")]
        [Range(0f, 60f)] public float turnDeadzone = 20f;
        [Tooltip("Degrees per second of turn per degree of twist past the deadzone.")]
        public float turnGain = 2f;
        [Tooltip("Top turning speed, degrees per second. Fast view turns can feel uncomfortable in VR.")]
        public float maxTurnSpeed = 60f;

        PuppetSpawner m_Spawner;
        SizeChanger m_Size;
        MarionetteRig m_Rig;
        float m_Heading;
        Vector3 m_Anchor;
        float m_GroundY;
        Vector3 m_Head;

        /// <summary>Direction the view faces, degrees around world up (0 = +Z).</summary>
        public float Heading => m_Heading;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            m_Size = GetComponent<SizeChanger>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var spawner = FindAnyObjectByType<PuppetSpawner>();
            if (spawner == null || spawner.GetComponent<PuppetFollowView>() != null) return;
            if (FindAnyObjectByType<XROrigin>() == null) return;
            spawner.gameObject.AddComponent<PuppetFollowView>();
        }

        // Switched back on (the puppeteer reconnected): jump straight behind the puppet instead of gliding there.
        void OnEnable() => m_Rig = null;

        void FixedUpdate()
        {
            var rig = m_Spawner.rig;
            var input = m_Spawner.input;
            if (rig == null || input == null || rig != m_Rig) return;
            if (!twistToTurn || !input.SupportsTravel || (m_Size != null && m_Size.IsResizing)) return;

            float twist = (rig.leftCross.Yaw + rig.rightCross.Yaw) * 0.5f;
            float excess = Mathf.Abs(twist) - turnDeadzone;
            if (excess <= 0f) return;
            float step = Mathf.Sign(twist) * Mathf.Min(excess * turnGain, maxTurnSpeed) * Time.fixedDeltaTime;

            // Turn the strings' resting poses about their midpoint; the view follows in LateUpdate.
            var turn = Quaternion.AngleAxis(step, Vector3.up);
            Vector3 pivot = Anchor(rig);
            foreach (var cross in new[] { rig.leftCross, rig.rightCross })
            {
                cross.neutral.position = pivot + turn * (cross.neutral.position - pivot);
                cross.neutral.rotation = turn * cross.neutral.rotation;
            }
        }

        void LateUpdate()
        {
            var rig = m_Spawner.rig;
            if (rig == null || origin == null || origin.Camera == null) return;
            Transform t = origin.transform;
            float dt = Time.deltaTime;

            bool snap = rig != m_Rig;
            if (snap) m_Rig = rig; // first frame or respawn: jump straight there

            float target = YawOf(rig.leftCross.neutral.rotation);
            var input = m_Spawner.input;
            if (input != null && !input.SupportsTravel && twistToTurn)
                target += (rig.leftCross.Yaw + rig.rightCross.Yaw) * 0.5f; // keyboard: swing round behind the cross
            m_Heading = snap ? target : Mathf.LerpAngle(m_Heading, target, Smooth(followSharpness, dt));

            Vector3 anchor = Anchor(rig);
            m_Anchor = snap ? anchor : Vector3.Lerp(m_Anchor, anchor, Smooth(followSharpness, dt));
            float ground = rig.GroundPoint.y;
            m_GroundY = snap ? ground : Mathf.Lerp(m_GroundY, ground, Smooth(heightSharpness, dt));

            // The head's position in the play space, unscaled. Smoothed, so head motion shows but drift is corrected.
            Vector3 head = t.InverseTransformPoint(origin.Camera.transform.position);
            m_Head = snap ? head : Vector3.Lerp(m_Head, head, Smooth(headRecenter, dt));

            float h = rig.height;
            var rotation = Quaternion.Euler(0f, m_Heading, 0f);
            Vector3 eye = new Vector3(m_Anchor.x, m_GroundY, m_Anchor.z)
                          + rotation * (Vector3.back * (distanceBehind * h))
                          + Vector3.up * (eyeHeight * h);
            t.rotation = rotation;
            t.position = eye - rotation * (m_Head * t.lossyScale.x);
        }

        static Vector3 Anchor(MarionetteRig rig)
        {
            Vector3 a = (rig.leftCross.neutral.position + rig.rightCross.neutral.position) * 0.5f;
            return new Vector3(a.x, 0f, a.z);
        }

        static float Smooth(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);

        static float YawOf(Quaternion r)
        {
            Vector3 f = Vector3.ProjectOnPlane(r * Vector3.forward, Vector3.up);
            return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}

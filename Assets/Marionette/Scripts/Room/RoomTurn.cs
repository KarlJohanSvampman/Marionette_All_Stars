using UnityEngine;

namespace Marionette.Room
{
    /// <summary>
    /// Turning in the room without turning your real body: hold an open hand still at the left or right
    /// edge of your view and you turn that way, a notch at a time, for as long as you keep it there. The
    /// hand turns with you, so it stays at the edge: just hold it. A ring at the hand fills while it waits.
    ///
    /// Not while seated (the chair faces the puppet) or connected to the puppet, and not while pinching
    /// (that's pointing).
    /// </summary>
    [RequireComponent(typeof(RoomHands))]
    public class RoomTurn : MonoBehaviour
    {
        public PuppeteerRoom room;
        [Tooltip("How close to the side of the view the hand must be, as a fraction of the view's width.")]
        [Range(0.02f, 0.4f)] public float edge = 0.15f;
        [Tooltip("Degrees per notch.")]
        [Range(5f, 90f)] public float notch = 22.5f;
        [Tooltip("Seconds the hand must be held still at the edge before the first notch.")]
        public float dwell = 0.5f;
        [Tooltip("Seconds between notches while the hand stays there.")]
        public float repeat = 0.45f;
        [Tooltip("The hand counts as still below this speed, metres per second (relative to the head).")]
        public float stillSpeed = 0.25f;

        RoomHands m_Hands;
        readonly float[] m_Held = new float[2];
        readonly Vector3[] m_Last = new Vector3[2];
        readonly LineRenderer[] m_Rings = new LineRenderer[2];
        float m_Cooldown;

        /// <summary>Notches turned since start (positive = right).</summary>
        public int Notches { get; private set; }

        void Awake()
        {
            m_Hands = GetComponent<RoomHands>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "LeftTurnRing" : "RightTurnRing");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.widthMultiplier = 0.004f;
                if (shader != null) line.sharedMaterial = new Material(shader);
                line.startColor = line.endColor = new Color(0.6f, 0.9f, 1f, 0.9f);
                line.enabled = false;
                m_Rings[i] = line;
            }
        }

        bool Usable => room == null || (!room.Seated && (room.link == null || !room.link.IsConnected));

        void Update()
        {
            var cam = m_Hands.origin != null && m_Hands.origin.Camera != null ? m_Hands.origin.Camera : Camera.main;
            m_Cooldown -= Time.deltaTime;
            for (int i = 0; i < 2; i++)
            {
                var hand = m_Hands[i];
                int side = cam != null && Usable && !m_Hands.UsingMouse ? EdgeSide(cam, hand) : 0;
                Vector3 local = cam != null ? cam.transform.InverseTransformPoint(hand.palm) * cam.transform.lossyScale.x : Vector3.zero;
                float speed = Time.deltaTime > 0f ? (local - m_Last[i]).magnitude / Time.deltaTime : 0f;
                m_Last[i] = local;

                if (side == 0 || speed > stillSpeed) { m_Held[i] = 0f; m_Rings[i].enabled = false; continue; }
                m_Held[i] += Time.deltaTime;
                float progress = Mathf.Clamp01(m_Held[i] / dwell);
                DrawRing(m_Rings[i], cam, hand.palm, progress);
                if (m_Held[i] >= dwell && m_Cooldown <= 0f)
                {
                    Turn(cam, side);
                    m_Held[i] = dwell - repeat; // the next notch after `repeat` more seconds
                    m_Cooldown = repeat * 0.5f; // both hands at once don't double the speed
                }
            }
        }

        /// <summary>-1 at the left edge, +1 at the right edge, 0 elsewhere. Just past the edge counts too.</summary>
        int EdgeSide(Camera cam, RoomHands.Hand hand)
        {
            if (!hand.tracked || hand.pinching) return 0;
            Vector3 v = cam.WorldToViewportPoint(hand.palm);
            if (v.z <= 0f || v.y < 0.1f || v.y > 0.9f) return 0;
            if (v.x < edge && v.x > -0.2f) return -1;
            if (v.x > 1f - edge && v.x < 1.2f) return 1;
            return 0;
        }

        /// <summary>Turn the player about their head by one notch.</summary>
        public void Turn(Camera cam, int side)
        {
            var origin = m_Hands.origin;
            if (origin == null || cam == null) return;
            origin.transform.RotateAround(cam.transform.position, Vector3.up, notch * side);
            Notches += side;
        }

        static void DrawRing(LineRenderer line, Camera cam, Vector3 centre, float progress)
        {
            const int n = 24;
            float scale = cam.transform.lossyScale.x;
            line.enabled = true;
            line.widthMultiplier = 0.004f * scale;
            int count = Mathf.Max(2, Mathf.CeilToInt(n * progress) + 1);
            line.positionCount = count;
            Vector3 right = cam.transform.right, up = cam.transform.up;
            float r = 0.05f * scale;
            for (int k = 0; k < count; k++)
            {
                float a = (k / (float)n) * Mathf.PI * 2f;
                line.SetPosition(k, centre + (right * Mathf.Sin(a) + up * Mathf.Cos(a)) * r);
            }
        }
    }
}

using Unity.XR.CoreUtils;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Edge-push travel: lets the player follow the puppet across a course much bigger than arm's reach.
    ///
    /// Inside the reach radius around a cross's neutral pose, the hand puppeteers as normal. Reach
    /// past it and the player, the view and both crosses' neutral poses glide in that direction, faster
    /// the further past the edge you reach. The puppet is drawn along by its strings; walk it with your
    /// fingers as you go. Pull your hands back inside the radius to stop.
    ///
    /// Works because hand input is measured relative to the player: carrying the player carries the
    /// hands, so the reach doesn't change while travelling, and travel continues until you pull back.
    /// Only used by inputs where reach is bounded (hand tracking). The keyboard moves the crosses directly.
    /// </summary>
    [RequireComponent(typeof(PuppetSpawner))]
    public class PuppetTravel : MonoBehaviour
    {
        [Tooltip("Optional. Carried along with the puppet so the player keeps up.")]
        public XROrigin origin;
        [Tooltip("Horizontal hand reach, in puppet heights, before travel starts.")]
        [Range(0.1f, 1.5f)] public float reachRadius = 0.35f;
        [Tooltip("Travel speed per unit of reach past the edge, per second.")]
        public float speedGain = 3f;
        [Tooltip("Top travel speed, in puppet heights per second.")]
        public float maxSpeed = 1f;


        PuppetSpawner m_Spawner;
        SizeChanger m_Size;
        ViewScaler m_View;

        /// <summary>Current travel velocity in world metres per second.</summary>
        public Vector3 Velocity { get; private set; }

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            m_Size = GetComponent<SizeChanger>();
            m_View = GetComponent<ViewScaler>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var spawner = FindAnyObjectByType<PuppetSpawner>();
            if (spawner == null || spawner.GetComponent<PuppetTravel>() != null) return;
            if (FindAnyObjectByType<XROrigin>() == null) return;
            spawner.gameObject.AddComponent<PuppetTravel>();
        }

        void FixedUpdate()
        {
            Velocity = Vector3.zero;
            var rig = m_Spawner.rig;
            var input = m_Spawner.input;
            if (rig == null || input == null || !input.SupportsTravel) return;
            if (m_Size != null && m_Size.IsResizing) return;

            float radius = reachRadius * rig.height;
            Vector3 push = (Excess(rig.leftCross, radius) + Excess(rig.rightCross, radius)) * 0.5f;
            Vector3 velocity = Vector3.ClampMagnitude(push * speedGain, maxSpeed * rig.height);
            Velocity = velocity;

            Vector3 delta = velocity * Time.fixedDeltaTime;
            if (delta.sqrMagnitude < 1e-14f) return;
            rig.leftCross.neutral.position += delta;
            rig.rightCross.neutral.position += delta;
            if (origin != null)
            {
                origin.transform.position += delta;
                if (m_View == null) m_View = GetComponent<ViewScaler>(); // may be auto-installed after us
                if (m_View != null) m_View.Shift(delta);
            }
        }

        /// <summary>How far the cross is past the reach radius, horizontally.</summary>
        static Vector3 Excess(ControlCross cross, float radius)
        {
            Vector3 t = cross.Translation;
            t.y = 0f;
            float d = t.magnitude;
            return d > radius ? t * ((d - radius) / d) : Vector3.zero;
        }
    }
}

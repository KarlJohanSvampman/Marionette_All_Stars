using Unity.XR.CoreUtils;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Scales the player with the puppet, so the puppet always looks the same size in the headset.
    /// The XR Origin is scaled by puppet height / apparentHeight: at mouse size the player is
    /// shrunk, and the desk becomes a landscape. Scaling the origin also scales hand motion, so
    /// moving your hand 10 cm always moves the puppet by the same fraction of its own height.
    ///
    /// The origin scales about the puppet's ground point, so the view relative to the puppet stays
    /// the same: same distance, same height above it. During a smooth size change the view grows
    /// or shrinks with the puppet. When the puppet respawns elsewhere the view moves with it.
    /// </summary>
    [RequireComponent(typeof(PuppetSpawner))]
    public class ViewScaler : MonoBehaviour
    {
        public XROrigin origin;
        [Tooltip("How tall the puppet looks in the headset, in metres. 0.575 = a gnome seen at real size.")]
        [Range(0.2f, 2f)] public float apparentHeight = 0.8f;
        [Tooltip("Move the view along when the puppet respawns somewhere else.")]
        public bool followRespawn = true;

        PuppetSpawner m_Spawner;
        MarionetteRig m_Rig;
        Vector3 m_SpawnGround;
        Camera m_Camera;
        float m_BaseNearClip;

        /// <summary>Current player scale (1 = real size).</summary>
        public float Scale => origin != null ? origin.transform.localScale.x : 1f;

        /// <summary>Player scale that makes a puppet of this height look apparentHeight tall.</summary>
        public float ScaleFor(float puppetHeight) => puppetHeight / apparentHeight;

        /// <summary>
        /// Tell the scaler the player was carried by delta (PuppetTravel), so a later respawn puts the
        /// player back at the same offset from the puppet instead of adding the travelled distance.
        /// </summary>
        public void Shift(Vector3 delta) => m_SpawnGround += delta;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null)
            {
                m_Camera = origin.Camera;
                m_BaseNearClip = m_Camera.nearClipPlane / Scale;
            }
        }

        // Adds itself to scenes built before it existed, as long as they have an XR rig.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var spawner = FindAnyObjectByType<PuppetSpawner>();
            if (spawner == null || spawner.GetComponent<ViewScaler>() != null) return;
            if (FindAnyObjectByType<XROrigin>() == null) return;
            spawner.gameObject.AddComponent<ViewScaler>();
        }

        // After SizeChanger's FixedUpdate and the spawner, before rendering.
        void LateUpdate()
        {
            if (origin == null || m_Spawner.rig == null) return;
            var rig = m_Spawner.rig;
            Transform t = origin.transform;

            if (rig != m_Rig)
            {
                // New puppet: first spawn, respawn at a checkpoint, or an instant resize.
                Vector3 ground = rig.GroundPoint;
                if (m_Rig != null && followRespawn) t.position += ground - m_SpawnGround;
                m_Rig = rig;
                m_SpawnGround = ground;
            }

            float k = ScaleFor(rig.height) / Scale;
            if (Mathf.Abs(k - 1f) < 1e-4f) return;
            Vector3 pivot = rig.GroundPoint;
            t.position = pivot + (t.position - pivot) * k;
            t.localScale *= k;
            if (m_Camera != null) m_Camera.nearClipPlane = Mathf.Max(0.0005f, m_BaseNearClip * Scale);
        }
    }
}

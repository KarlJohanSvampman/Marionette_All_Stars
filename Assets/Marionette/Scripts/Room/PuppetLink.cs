using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marionette.Room
{
    /// <summary>
    /// The connection between the puppeteer in the chair and the puppet.
    ///
    /// Connect (the armrest's Take Control, only while seated): the view jumps to the third-person camera
    /// behind the puppet, scaled to it, and your hands work the strings. The room's pointer is off.
    ///
    /// Disconnect: the puppet's strings hold where they are, the view returns to the chair and the room's
    /// pointer comes back. Happens when:
    ///   - anything physically touches the puppeteer's body in the room (PuppeteerAvatar's sensors): the
    ///     link breaks and Take Control has to be pressed again
    ///   - you hold your right palm up with a pinch for holdToLeave seconds (or press Escape at a desk)
    ///   - you leave the chair
    /// </summary>
    [RequireComponent(typeof(PuppetSpawner))]
    public class PuppetLink : MonoBehaviour
    {
        public PuppeteerRoom room;
        public XROrigin origin;
        public RoomHands hands;
        [Tooltip("Seconds of right palm up + pinch to let go of the puppet.")]
        public float holdToLeave = 1f;
        [Tooltip("Seconds after connecting before touches count, so the finger that pressed Take Control doesn't break it.")]
        public float contactGrace = 0.6f;

        PuppetSpawner m_Spawner;
        Behaviour[] m_PuppetView;
        float m_Hold, m_ConnectedAt;
        float m_BaseNearClip = 0.01f;

        public bool IsConnected { get; private set; }
        public string LastReason { get; private set; } = "";
        /// <summary>True once the grace period after connecting is over: touches break the link from here.</summary>
        public bool ContactArmed => IsConnected && Time.time - m_ConnectedAt >= contactGrace;
        public event Action<bool, string> Changed;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null) m_BaseNearClip = origin.Camera.nearClipPlane;
            var cam = origin != null ? origin.Camera : Camera.main;
            m_PuppetView = new Behaviour[]
            {
                GetComponent<ViewScaler>(), GetComponent<PuppetFollowView>(), GetComponent<PuppetTravel>(),
                GetComponent<StringFlick>(), GetComponent<SizeChanger>(), cam != null ? cam.GetComponent<PuppetCamera>() : null
            };
            m_Spawner.Spawned += OnSpawned;
        }

        void Start() => Apply(false);

        void OnDestroy()
        {
            if (m_Spawner != null) m_Spawner.Spawned -= OnSpawned;
        }

        // Respawns (a fall, a beam) give the new puppet the input; take it away again while disconnected.
        void OnSpawned(MarionetteRig rig)
        {
            if (!IsConnected) SetPuppetInput(rig, null);
        }

        /// <summary>Take control of the puppet. Only from the chair.</summary>
        public bool Connect()
        {
            if (IsConnected) return true;
            if (room != null && !room.Seated) { LastReason = "sit in the chair first"; return false; }
            if (m_Spawner.rig == null) return false;
            Apply(true);
            return true;
        }

        public void Disconnect(string reason)
        {
            if (!IsConnected) return;
            LastReason = reason;
            Apply(false);
        }

        /// <summary>Something touched the puppeteer: the connection breaks.</summary>
        public void Interrupt(string what)
        {
            if (!ContactArmed) return;
            Disconnect("interrupted: " + what);
            Debug.Log($"[Room] Connection broken: {what}. Press Take Control again.");
        }

        void Apply(bool connected)
        {
            IsConnected = connected;
            foreach (var b in m_PuppetView) if (b != null) b.enabled = connected;
            if (room != null && room.pointer != null) room.pointer.enabled = !connected;
            var rig = m_Spawner.rig;
            if (connected)
            {
                SetPuppetInput(rig, m_Spawner.input);
                if (m_Spawner.input != null) m_Spawner.input.ResetPose(); // your hands' pose now is neutral
                m_ConnectedAt = Time.time;
                m_Hold = 0f;
            }
            else
            {
                SetPuppetInput(rig, null); // the strings hold where they are
                if (origin != null)
                {
                    origin.transform.localScale = Vector3.one;
                    if (origin.Camera != null) origin.Camera.nearClipPlane = m_BaseNearClip;
                }
                if (room != null) room.ReturnToSeat();
            }
            Changed?.Invoke(connected, LastReason);
        }

        static void SetPuppetInput(MarionetteRig rig, ControlInputSource input)
        {
            if (rig == null) return;
            if (rig.leftCross != null) rig.leftCross.input = input;
            if (rig.rightCross != null) rig.rightCross.input = input;
        }

        void Update()
        {
            if (!IsConnected) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Disconnect("let go"); return; }
            var right = hands != null ? hands.right : null;
            bool leaving = right != null && right.tracked && right.palmUp && right.pinching;
            m_Hold = leaving ? m_Hold + Time.deltaTime : 0f;
            if (m_Hold >= holdToLeave) Disconnect("let go");
        }
    }
}

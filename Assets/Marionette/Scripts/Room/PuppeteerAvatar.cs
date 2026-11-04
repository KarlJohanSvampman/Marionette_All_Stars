using System.Collections.Generic;
using UnityEngine;

namespace Marionette.Room
{
    /// <summary>Colliders under this don't count as touching the puppeteer (the chair he sits in, holograms, the floor).</summary>
    public class RoomNoInterrupt : MonoBehaviour { }

    /// <summary>
    /// The puppeteer's body: a simple figure that stands where you stand, or sits in the chair. Your own
    /// head is drawn only as a shadow, so it doesn't block the view; your hands are the tracked hand meshes.
    ///
    /// While connected to the puppet, the view is away with it but the body stays in the chair, and its
    /// head and hands keep following your real head and hands (relative to how you sat when you
    /// connected). Anything they touch, or anything that bumps into them, breaks the connection.
    /// </summary>
    public class PuppeteerAvatar : MonoBehaviour
    {
        public PuppeteerRoom room;
        public RoomHands hands;
        public Transform standing, sitting;
        public ContactSensor head, leftHand, rightHand;

        [Header("Model (optional, kept local: never committed)")]
        [Tooltip("A character model for the standing puppeteer. Empty: loads Resources/<modelResource> if there is one " +
                 "(e.g. Assets/Marionette/Models/Resources/PuppeteerModel.glb or .fbx), otherwise the stand-in figure is used.")]
        public GameObject model;
        public string modelResource = "PuppeteerModel";
        [Tooltip("Height the model is scaled to, metres.")]
        public float modelHeight = 1.75f;
        [Tooltip("Turn the model about the vertical so it faces forward, degrees. Flip by 180 if it faces backwards.")]
        public float modelYaw = -90f;
        [Tooltip("The body sits this far behind the eyes, metres, so looking down shows your chest instead of the inside of your head.")]
        public float bodyBehindEyes = 0.2f;

        /// <summary>The model in use, if any.</summary>
        public GameObject ModelInstance { get; private set; }

        // Tracked pose when the link was made, and where the head sat in the room then.
        Vector3 m_TrackHead;
        Quaternion m_Remap = Quaternion.identity;
        bool m_Connected;

        void Start()
        {
            if (room != null && room.link != null) room.link.Changed += OnLinkChanged;
            SetSensors(false);
            UseModel();
        }

        /// <summary>
        /// Put the character model in place of the standing stand-in, scaled to modelHeight with its feet on the
        /// floor. Until the model is rigged (Mixamo) it can't sit, so the seated stand-in stays.
        /// </summary>
        void UseModel()
        {
            var source = model != null ? model : (string.IsNullOrEmpty(modelResource) ? null : Resources.Load<GameObject>(modelResource));
            if (source == null || standing == null) return;
            var holder = new GameObject("Model").transform;
            holder.SetParent(standing, false);
            holder.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
            var instance = Instantiate(source, holder, false);
            instance.name = source.name;
            foreach (var c in instance.GetComponentsInChildren<Collider>()) Destroy(c);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(holder.gameObject); return; }
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            // Feet on the floor, centred under the body (measured before scaling), then scaled to height.
            Vector3 feet = holder.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            instance.transform.localPosition -= feet;
            float k = b.size.y > 1e-4f ? modelHeight / b.size.y : 1f;
            holder.localScale = Vector3.one * k;
            foreach (Transform child in standing)
                if (child != holder) child.gameObject.SetActive(false);
            ModelInstance = instance;
        }

        void OnLinkChanged(bool connected, string reason)
        {
            m_Connected = connected;
            if (connected)
            {
                var cam = room.origin.Camera.transform;
                m_TrackHead = cam.localPosition;
                float trackYaw = Yaw(cam.localRotation * Vector3.forward);
                float seatYaw = Yaw(room.chair.Facing);
                m_Remap = Quaternion.Euler(0f, seatYaw - trackYaw, 0f);
            }
            SetSensors(connected);
        }

        void SetSensors(bool on)
        {
            foreach (var s in new[] { head, leftHand, rightHand })
                if (s != null) { s.avatar = this; s.Arm(on); }
        }

        /// <summary>Called by a sensor when something new touches it.</summary>
        public void Touched(Collider other)
        {
            if (!m_Connected || room == null || room.link == null) return;
            room.link.Interrupt(other.name);
        }

        void LateUpdate()
        {
            if (room == null || room.origin == null) return;
            var cam = room.origin.Camera.transform;
            bool seated = room.Seated;
            if (standing != null) standing.gameObject.SetActive(!seated);
            if (sitting != null) sitting.gameObject.SetActive(seated);

            if (m_Connected)
            {
                // Body stays in the chair; head and hands follow the real ones, mapped onto the seat.
                Vector3 eye = room.chair.eyePoint.position;
                Place(head, eye + m_Remap * (cam.localPosition - m_TrackHead), true);
                Place(leftHand, MapHand(hands != null ? hands.left : null, eye, out bool l), l);
                Place(rightHand, MapHand(hands != null ? hands.right : null, eye, out bool r), r);
                return;
            }

            if (seated)
            {
                transform.SetPositionAndRotation(room.chair.transform.position, Quaternion.LookRotation(room.chair.Facing, Vector3.up));
            }
            else
            {
                // Stand under the head, facing where you look.
                float floorY = room.floor != null ? room.floor.Snap(cam.position).y : 0f;
                Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
                if (fwd.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                Vector3 back = ModelInstance != null && fwd.sqrMagnitude > 1e-6f ? -fwd.normalized * bodyBehindEyes : Vector3.zero;
                transform.position = new Vector3(cam.position.x, floorY, cam.position.z) + back;
            }
            if (head != null) head.transform.position = cam.position;
        }

        Vector3 MapHand(RoomHands.Hand hand, Vector3 eye, out bool tracked)
        {
            tracked = hand != null && hand.tracked && hands != null && !hands.UsingMouse;
            return tracked ? eye + m_Remap * (hand.trackingPalm - m_TrackHead) : Vector3.zero;
        }

        static void Place(ContactSensor s, Vector3 p, bool active)
        {
            if (s == null) return;
            s.Track(active);
            if (active) s.transform.position = p;
        }

        static float Yaw(Vector3 v)
        {
            v = Vector3.ProjectOnPlane(v, Vector3.up);
            return v.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// A trigger on the puppeteer's head or hand. While armed, anything new that touches it is reported.
    /// Things already touching when it's armed (the finger on the Take Control button) are ignored until
    /// they've moved away.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class ContactSensor : MonoBehaviour
    {
        public PuppeteerAvatar avatar;
        readonly HashSet<Collider> m_Ignored = new HashSet<Collider>();
        bool m_Armed, m_Tracked = true;
        SphereCollider m_Collider;

        void Awake()
        {
            m_Collider = GetComponent<SphereCollider>();
            m_Collider.isTrigger = true;
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        public void Arm(bool on)
        {
            m_Armed = on;
            m_Ignored.Clear();
            UpdateCollider();
        }

        public void Track(bool tracked)
        {
            if (tracked == m_Tracked) return;
            m_Tracked = tracked;
            UpdateCollider();
        }

        void UpdateCollider()
        {
            if (m_Collider != null) m_Collider.enabled = m_Armed && m_Tracked;
        }

        static bool Counts(Collider other) =>
            !other.isTrigger
            && other.GetComponentInParent<RoomNoInterrupt>() == null
            && other.GetComponentInParent<PuppeteerAvatar>() == null
            && other.GetComponentInParent<MarionetteRig>() == null;

        void OnTriggerEnter(Collider other) => Consider(other);
        void OnTriggerStay(Collider other) => Consider(other);
        void OnTriggerExit(Collider other) => m_Ignored.Remove(other);

        void Consider(Collider other)
        {
            if (!m_Armed || avatar == null || !Counts(other) || m_Ignored.Contains(other)) return;
            var link = avatar.room != null ? avatar.room.link : null;
            if (link == null || !link.ContactArmed) { m_Ignored.Add(other); return; } // already touching at connect
            avatar.Touched(other);
        }
    }
}

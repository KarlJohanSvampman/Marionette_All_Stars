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

        // Tracked pose when the link was made, and where the head sat in the room then.
        Vector3 m_TrackHead;
        Quaternion m_Remap = Quaternion.identity;
        bool m_Connected;

        void Start()
        {
            if (room != null && room.link != null) room.link.Changed += OnLinkChanged;
            SetSensors(false);
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
                transform.position = new Vector3(cam.position.x, floorY, cam.position.z);
                if (fwd.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
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

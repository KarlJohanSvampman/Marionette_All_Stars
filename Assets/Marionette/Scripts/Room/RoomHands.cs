using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace Marionette.Room
{
    /// <summary>
    /// The puppeteer's hands in the room, read straight from hand tracking: pinch (thumb to index),
    /// a pointing ray, the index fingertip and the palm, per hand.
    ///
    /// The pointing ray starts at the pinch and runs out from an estimated shoulder, which is steadier
    /// than the finger direction and points where you'd expect.
    ///
    /// Without a headset (desktop): the mouse is the right hand. Its ray comes from the camera, the left
    /// button pinches, and dragging with the right button looks around.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RoomHands : MonoBehaviour
    {
        public XROrigin origin;
        [Tooltip("Thumb and index closer than this (metres) starts a pinch.")]
        public float pinchStart = 0.02f;
        [Tooltip("Further apart than this ends it.")]
        public float pinchRelease = 0.035f;
        [Tooltip("Desktop look speed, degrees per pixel.")]
        public float mouseLook = 0.2f;

        public class Hand
        {
            public bool tracked, pinching, pinchStarted, pinchEnded, palmUp;
            public Vector3 pinchPoint, indexTip, palm; // world
            public Vector3 trackingPalm, trackingIndex; // tracking space (real metres), for the body's contact sensors
            public Ray aim;
        }

        public readonly Hand left = new Hand(), right = new Hand();
        public Hand this[int i] => i == 0 ? left : right;
        /// <summary>No hand tracking: the mouse stands in for the right hand.</summary>
        public bool UsingMouse { get; private set; }

        XRHandSubsystem m_Subsystem;
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        float m_Pitch;

        Transform Space => origin != null && origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : null;
        Camera Cam => origin != null && origin.Camera != null ? origin.Camera : Camera.main;

        void Awake()
        {
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
        }

        void Update()
        {
            bool hands = EnsureSubsystem() && (m_Subsystem.leftHand.isTracked || m_Subsystem.rightHand.isTracked);
            UsingMouse = !hands && !XRSettings.isDeviceActive;
            if (hands)
            {
                Read(m_Subsystem.leftHand, left, -1f);
                Read(m_Subsystem.rightHand, right, 1f);
            }
            else
            {
                Clear(left);
                if (UsingMouse) ReadMouse(right); else Clear(right);
            }
        }

        void Read(XRHand hand, Hand h, float side)
        {
            bool was = h.pinching;
            h.pinchStarted = h.pinchEnded = false;
            h.tracked = hand.isTracked
                        && hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)
                        && hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)
                        && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)
                        && Fill(h, thumb, index, palm, side);
            if (!h.tracked) { Release(h, was); return; }
        }

        bool Fill(Hand h, Pose thumb, Pose index, Pose palm, float side)
        {
            var space = Space;
            Vector3 W(Vector3 p) => space != null ? space.TransformPoint(p) : p;
            float scale = space != null ? space.lossyScale.x : 1f;
            bool was = h.pinching;
            float gap = Vector3.Distance(thumb.position, index.position); // tracking space = real metres
            h.pinching = was ? gap < pinchRelease : gap < pinchStart;
            h.pinchStarted = h.pinching && !was;
            h.pinchEnded = !h.pinching && was;
            h.pinchPoint = W((thumb.position + index.position) * 0.5f);
            h.indexTip = W(index.position);
            h.palm = W(palm.position);
            h.trackingPalm = palm.position;
            h.trackingIndex = index.position;
            // The palm joint's up axis is the back of the hand: pointing down = palm up.
            Vector3 back = (space != null ? space.rotation : Quaternion.identity) * (palm.rotation * Vector3.up);
            h.palmUp = Vector3.Dot(back, Vector3.down) > 0.6f;

            var cam = Cam;
            Vector3 head = cam != null ? cam.transform.position : h.pinchPoint;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 sideways = Vector3.Cross(Vector3.up, fwd);
            Vector3 shoulder = head + (Vector3.down * 0.18f + sideways * (0.17f * side)) * scale;
            Vector3 dir = h.pinchPoint - shoulder;
            h.aim = new Ray(h.pinchPoint, dir.sqrMagnitude > 1e-8f ? dir.normalized : fwd);
            return true;
        }

        static void Release(Hand h, bool was)
        {
            h.pinching = false;
            h.pinchStarted = false;
            h.pinchEnded = was;
        }

        static void Clear(Hand h)
        {
            bool was = h.pinching;
            h.tracked = false;
            Release(h, was);
        }

        void ReadMouse(Hand h)
        {
            var mouse = Mouse.current;
            var cam = Cam;
            if (mouse == null || cam == null) { Clear(h); return; }
            bool was = h.pinching;
            h.tracked = true;
            h.pinching = mouse.leftButton.isPressed;
            h.pinchStarted = h.pinching && !was;
            h.pinchEnded = !h.pinching && was;
            h.aim = cam.ScreenPointToRay(mouse.position.ReadValue());
            h.pinchPoint = h.indexTip = h.palm = h.aim.origin;
            h.palmUp = false;

            // Right-drag to look around.
            if (mouse.rightButton.isPressed && origin != null)
            {
                Vector2 d = mouse.delta.ReadValue() * mouseLook;
                origin.transform.RotateAround(cam.transform.position, Vector3.up, d.x);
                m_Pitch = Mathf.Clamp(m_Pitch - d.y, -80f, 80f);
                cam.transform.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            }
        }

        bool EnsureSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return true;
            SubsystemManager.GetSubsystems(s_Subsystems);
            m_Subsystem = null;
            foreach (var s in s_Subsystems)
                if (s.running) { m_Subsystem = s; break; }
            return m_Subsystem != null;
        }
    }
}

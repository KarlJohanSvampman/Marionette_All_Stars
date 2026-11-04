using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace Marionette
{
    /// <summary>
    /// In-headset hand tracking diagnostics. Installs itself in Play mode whenever the scene has an
    /// XRHandsControlInput and an XR display is running.
    ///   - A small text panel below your view: subsystem state, whether each hand is tracked, input source.
    ///   - Bright spheres on your palm and fingertips drawn straight from the XR Hands joint data.
    ///     If you see the spheres but not the hand meshes, tracking works and only the mesh visuals are broken.
    ///     If you see neither, Unity is not receiving hand data from the runtime.
    /// State changes are also written to the Console / Editor.log with the [HandStatus] tag.
    /// Toggle with the `show` field.
    /// </summary>
    public class HandTrackingStatus : MonoBehaviour
    {
        public bool show = true;

        static readonly XRHandJointID[] k_Joints =
        {
            XRHandJointID.Palm, XRHandJointID.ThumbTip, XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        TextMesh m_Text;
        Transform m_Panel;
        readonly Transform[] m_LeftDots = new Transform[6];
        readonly Transform[] m_RightDots = new Transform[6];
        Transform m_TrackingSpace;
        AutoControlInput m_Auto;
        XRHandsControlInput m_Hands;
        string m_LastState = "";
        float m_NextSummary;
        float m_LastTracked;
        string m_DeviceReport = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Object.FindAnyObjectByType<XRHandsControlInput>() == null) return;
            if (Object.FindAnyObjectByType<HandTrackingStatus>() != null) return;
            new GameObject("Hand Tracking Status").AddComponent<HandTrackingStatus>();
        }

        void Start()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.CameraFloorOffsetObject != null) m_TrackingSpace = origin.CameraFloorOffsetObject.transform;
            m_Auto = FindAnyObjectByType<AutoControlInput>();
            m_Hands = FindAnyObjectByType<XRHandsControlInput>();

            m_Panel = new GameObject("Status Panel").transform;
            m_Panel.SetParent(transform, false);
            m_Text = m_Panel.gameObject.AddComponent<TextMesh>();
            m_Text.characterSize = 0.004f;
            m_Text.fontSize = 48;
            m_Text.anchor = TextAnchor.MiddleCenter;
            m_Text.alignment = TextAlignment.Center;
            m_Text.color = Color.white;

            for (int i = 0; i < k_Joints.Length; i++)
            {
                m_LeftDots[i] = Dot(new Color(0.3f, 0.8f, 1f), i == 0 ? 0.02f : 0.012f);
                m_RightDots[i] = Dot(new Color(1f, 0.6f, 0.2f), i == 0 ? 0.02f : 0.012f);
            }
        }

        Transform Dot(Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) r.material = new Material(shader);
            r.material.color = color;
            go.SetActive(false);
            return go.transform;
        }

        void Update()
        {
            XRHandSubsystem subsystem = null;
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var s in s_Subsystems) if (s.running) { subsystem = s; break; }

            bool left = subsystem != null && subsystem.leftHand.isTracked;
            bool right = subsystem != null && subsystem.rightHand.isTracked;
            string source = m_Auto != null && m_Auto.Active != null ? m_Auto.Active.GetType().Name.Replace("ControlInput", "") : "-";

            string subsystemState = subsystem != null ? "running" : s_Subsystems.Count > 0 ? "found but stopped" : "NOT FOUND";
            string state = $"hand subsystem {subsystemState} | left {(left ? "TRACKED" : "not tracked")} | right {(right ? "TRACKED" : "not tracked")} | input {source} | xr display {(XRSettings.isDeviceActive ? "on" : "off")}";
            if (state != m_LastState)
            {
                Debug.Log("[HandStatus] " + state);
                m_LastState = state;
            }
            if (Time.unscaledTime >= m_NextSummary)
            {
                m_NextSummary = Time.unscaledTime + 5f;
                if (subsystem != null)
                    Debug.Log($"[HandStatus] update flags L={subsystem.leftHand.isTracked} R={subsystem.rightHand.isTracked}");
                m_DeviceReport = DescribeHandDevices();
                Debug.Log("[HandStatus] input devices:\n" + m_DeviceReport);
            }

            DrawHand(subsystem != null ? subsystem.leftHand : default, left, m_LeftDots);
            DrawHand(subsystem != null ? subsystem.rightHand : default, right, m_RightDots);
            UpdatePanel(subsystemState, left, right, source);
        }

        static readonly string[] k_ReportControls =
        {
            "isTracked", "trackingState", "devicePosition", "pointerPosition",
            "pinchValue", "graspValue", "pinchStrengthIndex", "pinchStrengthMiddle", "aimFlags"
        };

        /// <summary>
        /// The other hand paths besides XR Hands joint data: the Hand Interaction Profile device and
        /// Meta's aim hand (per-finger pinch). If these move while joints stay untracked, the runtime
        /// is sending hands but withholding joints.
        /// </summary>
        static string DescribeHandDevices()
        {
            var sb = new StringBuilder();
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                string id = device.name + " " + device.layout;
                if (id.IndexOf("hand", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append("  ").Append(device.name).Append(" (").Append(device.layout).Append(")");
                foreach (var name in k_ReportControls)
                {
                    var control = device.TryGetChildControl(name);
                    if (control == null) continue;
                    string value = control switch
                    {
                        UnityEngine.InputSystem.InputControl<float> c => c.ReadValue().ToString("0.00"),
                        UnityEngine.InputSystem.InputControl<int> c => c.ReadValue().ToString(),
                        UnityEngine.InputSystem.InputControl<Vector3> c => c.ReadValue().ToString("F2"),
                        _ => "?"
                    };
                    sb.Append(' ').Append(name).Append('=').Append(value);
                }
                sb.AppendLine();
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "  (no hand input devices)";
        }

        void DrawHand(XRHand hand, bool tracked, Transform[] dots)
        {
            for (int i = 0; i < dots.Length; i++)
            {
                Pose pose = default;
                bool ok = show && tracked && hand.GetJoint(k_Joints[i]).TryGetPose(out pose);
                dots[i].gameObject.SetActive(ok);
                if (ok) dots[i].position = m_TrackingSpace != null ? m_TrackingSpace.TransformPoint(pose.position) : pose.position;
            }
        }

        void UpdatePanel(string subsystemState, bool left, bool right, string source)
        {
            m_Panel.gameObject.SetActive(show);
            var cam = Camera.main;
            if (!show || cam == null) return;

            // Float 0.6 m ahead and a little below eye level, turning to face the player. Distances
            // and sizes follow the player's scale (ViewScaler shrinks the XR Origin at mouse size).
            float scale = m_TrackingSpace != null ? m_TrackingSpace.lossyScale.x : 1f;
            transform.localScale = Vector3.one * scale;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 target = cam.transform.position + (fwd * 0.6f + Vector3.down * 0.25f) * scale;
            m_Panel.position = Vector3.Lerp(m_Panel.position, target, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            m_Panel.rotation = Quaternion.LookRotation(m_Panel.position - cam.transform.position, Vector3.up);

            if (left || right) m_LastTracked = Time.unscaledTime;
            bool starved = subsystemState == "running" && Time.unscaledTime - m_LastTracked > 5f;

            var sb = new StringBuilder();
            sb.AppendLine($"Hand subsystem: {subsystemState}");
            sb.AppendLine($"Left hand: {(left ? "TRACKED" : "not tracked")}    Right hand: {(right ? "TRACKED" : "not tracked")}");
            sb.AppendLine($"Puppet input: {source}");
            if (m_Hands != null && m_Hands.fingerAction == XRHandsControlInput.FingerAction.PinchFlick)
            {
                var picked = m_Hands.SelectedTarget;
                sb.AppendLine(m_Hands.Phase switch
                {
                    XRHandsControlInput.TakePhase.Idle => "Pinch left fingers to pick a string",
                    XRHandsControlInput.TakePhase.Recording => $"{(picked.HasValue ? picked.Value.ToString() : "?")}: draw with right index  {m_Hands.TakeAge:0.0}/{m_Hands.recordSeconds:0} s",
                    XRHandsControlInput.TakePhase.Full => $"{picked}: release to apply",
                    _ => "Held too long: release and pinch again"
                });
            }
            sb.Append(m_DeviceReport);
            if (starved)
            {
                // Seen on Quest Link: hands stream to the PC but the runtime withholds them from apps.
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("No hand data from the runtime. Controllers down?");
                sb.AppendLine("Quest Link: Meta Horizon Link app > Settings > Developer >");
                sb.AppendLine("Developer Runtime Features ON, then restart Link.");
                sb.Append("Still nothing: update the headset (Settings > System > Software update).");
            }
            m_Text.text = sb.ToString();
        }
    }
}

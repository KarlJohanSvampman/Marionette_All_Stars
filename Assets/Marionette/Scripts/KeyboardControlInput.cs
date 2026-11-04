using UnityEngine;
using UnityEngine.InputSystem;

namespace Marionette
{
    /// <summary>
    /// Desktop stand-in for the hands so the puppet can be tested without a headset.
    ///
    ///   WASD        move both crosses (the whole puppeteer)       Q / E   yaw both crosses (palm axis)
    ///   I J K L     move only the right cross (feet + center)     U / O   yaw only the right cross
    ///   Z X C       left cross fingers:  left hand, right hand, head
    ///   B N M       right cross fingers: left foot, right foot, center
    ///   R           reset crosses to neutral
    ///   G (hold)    grow       H (hold)   shrink
    /// </summary>
    public class KeyboardControlInput : ControlInputSource
    {
        [Tooltip("Puppet heights per second.")]
        public float moveSpeed = 0.8f;
        public float yawSpeed = 90f;

        Vector3 m_Both, m_RightOnly;
        float m_YawBoth, m_YawRight;

        static readonly Key[] k_LeftKeys = { Key.Z, Key.X, Key.C };
        static readonly Key[] k_RightKeys = { Key.B, Key.N, Key.M };

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float dt = Time.deltaTime * puppetHeight;

            m_Both += Axis(kb, Key.D, Key.A, Key.W, Key.S) * (moveSpeed * dt);
            m_RightOnly += Axis(kb, Key.L, Key.J, Key.I, Key.K) * (moveSpeed * dt);
            m_YawBoth += ((kb[Key.E].isPressed ? 1f : 0f) - (kb[Key.Q].isPressed ? 1f : 0f)) * yawSpeed * Time.deltaTime;
            m_YawRight += ((kb[Key.O].isPressed ? 1f : 0f) - (kb[Key.U].isPressed ? 1f : 0f)) * yawSpeed * Time.deltaTime;

            if (kb[Key.R].wasPressedThisFrame) ResetPose();
        }

        public override void ResetPose()
        {
            m_Both = m_RightOnly = Vector3.zero;
            m_YawBoth = m_YawRight = 0f;
        }

        public override SizeGesture ReadSizeGesture()
        {
            var kb = Keyboard.current;
            if (kb == null) return SizeGesture.None;
            if (kb[Key.G].isPressed) return SizeGesture.Grow;
            if (kb[Key.H].isPressed) return SizeGesture.Shrink;
            return SizeGesture.None;
        }

        static Vector3 Axis(Keyboard kb, Key right, Key left, Key fwd, Key back)
        {
            float x = (kb[right].isPressed ? 1f : 0f) - (kb[left].isPressed ? 1f : 0f);
            float z = (kb[fwd].isPressed ? 1f : 0f) - (kb[back].isPressed ? 1f : 0f);
            return new Vector3(x, 0f, z);
        }

        public override bool Read(ControlSide side, ref CrossInput input)
        {
            var kb = Keyboard.current;
            input.Tracked = kb != null;
            if (kb == null) return false;

            bool right = side == ControlSide.Right;
            input.Translation = right ? m_Both + m_RightOnly : m_Both;
            input.Yaw = right ? m_YawBoth + m_YawRight : m_YawBoth;

            var keys = right ? k_RightKeys : k_LeftKeys;
            for (int i = 0; i < input.Channels.Length && i < keys.Length; i++)
                input.Channels[i] = kb[keys[i]].isPressed ? 1f : 0f;
            return true;
        }
    }
}

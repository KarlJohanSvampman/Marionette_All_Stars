using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Uses finger tracking whenever a hand tracking subsystem is running (headset on), and the
    /// keyboard otherwise, so the same scene works in VR and at the desk. Checked every frame,
    /// so putting the headset on mid-session switches over. Controllers are never used.
    /// </summary>
    public class AutoControlInput : ControlInputSource
    {
        public XRHandsControlInput hands;
        public KeyboardControlInput keyboard;

        ControlInputSource m_Active;

        public ControlInputSource Active => m_Active;

        ControlInputSource Pick()
        {
            var next = hands != null && hands.IsAvailable ? (ControlInputSource)hands : keyboard;
            if (next != m_Active)
            {
                m_Active = next;
                if (m_Active != null)
                {
                    m_Active.ResetPose();
                    Debug.Log($"[Marionette] Puppet input: {(m_Active == hands ? "finger tracking" : "keyboard")}");
                }
            }
            if (m_Active != null) m_Active.puppetHeight = puppetHeight;
            return m_Active;
        }

        public override bool Read(ControlSide side, ref CrossInput input)
        {
            var src = Pick();
            if (src != null) return src.Read(side, ref input);
            input.Tracked = false;
            return false;
        }

        public override SizeGesture ReadSizeGesture()
        {
            var src = Pick();
            return src != null ? src.ReadSizeGesture() : SizeGesture.None;
        }

        public override bool SupportsTravel => m_Active != null && m_Active.SupportsTravel;

        public override bool ReadLean(out Quaternion lean)
        {
            var src = Pick();
            if (src != null) return src.ReadLean(out lean);
            lean = Quaternion.identity;
            return false;
        }

        public override void ResetPose()
        {
            if (hands != null) hands.ResetPose();
            if (keyboard != null) keyboard.ResetPose();
        }
    }
}

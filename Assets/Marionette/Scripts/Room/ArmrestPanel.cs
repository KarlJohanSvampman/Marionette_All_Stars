using UnityEngine;

namespace Marionette.Room
{
    /// <summary>
    /// The panel on the chair's right armrest:
    ///   Take Control - connect to the puppet (view jumps behind it)
    ///   Globe        - show or hide the hologram globe
    ///   Beam         - send whoever is on the platform to the place picked on the globe
    /// A status line underneath says what's going on.
    /// </summary>
    public class ArmrestPanel : MonoBehaviour
    {
        public PuppeteerRoom room;
        public HoloGlobe globe;
        public Beamer beamer;
        public RoomButton takeControl, globeButton, beam;
        public TextMesh status;
        string m_Message = "";
        float m_MessageUntil;

        public void Wire()
        {
            takeControl.onPress = TakeControl;
            globeButton.onPress = ToggleGlobe;
            beam.onPress = Beam;
        }

        public void TakeControl()
        {
            if (room.link == null) return;
            if (!room.link.Connect()) Say(room.link.LastReason);
        }

        public void ToggleGlobe()
        {
            if (globe == null) return;
            globe.gameObject.SetActive(!globe.gameObject.activeSelf);
        }

        public void Beam()
        {
            if (beamer == null) return;
            beamer.Beam(globe != null ? globe.Selected : null);
            Say(beamer.Status);
        }

        void Say(string message)
        {
            m_Message = message;
            m_MessageUntil = Time.time + 4f;
        }

        void Update()
        {
            if (status == null) return;
            string line;
            if (Time.time < m_MessageUntil) line = m_Message;
            else if (beamer != null && beamer.Busy) line = beamer.Status;
            else if (room.link != null && !string.IsNullOrEmpty(room.link.LastReason) && room.link.LastReason.StartsWith("interrupted"))
                line = "Connection broken - press Take Control";
            else
            {
                string where = beamer != null && beamer.Current != null ? beamer.Current.displayName : "?";
                string pick = globe != null && globe.Selected != null ? globe.Selected.displayName : "none";
                line = $"Puppet: {where}   Destination: {pick}";
            }
            status.text = line;
        }
    }
}

using UnityEngine;

namespace Marionette
{
    /// <summary>The six rigid physics nodes. Everything between them is textile.</summary>
    public enum PuppetNodeId
    {
        Head,
        Center,
        LeftHand,
        RightHand,
        LeftFoot,
        RightFoot
    }

    public enum AttachmentType
    {
        ExternalControl, // marionette string, always on the OUTSIDE surface of a node
        Grapple,         // internal grappling line exit point (hand suction cup)
        Suction,         // suction cup contact point (palms and soles)
        Textile          // where a textile link meets a node
    }

    public enum ControlSide
    {
        Left,
        Right
    }

    /// <summary>
    /// One frame of input for a control cross.
    /// Translation/Yaw are the hand transform (H), Channels are the finger values (F) in 0..1.
    /// </summary>
    public struct CrossInput
    {
        public bool Tracked;
        public Vector3 Translation; // world-space offset from the neutral cross position
        public float Yaw;           // degrees around world up, the palm axis
        public float[] Channels;    // one value per string channel, 0 = rest, 1 = full lift
        public Quaternion Tilt;     // world-space pitch/roll of the cross (palm tilt). Unset (all zero) = level.

        /// <summary>Tilt, with an unset value read as level.</summary>
        public Quaternion TiltOrIdentity => Tilt.x == 0f && Tilt.y == 0f && Tilt.z == 0f && Tilt.w == 0f ? Quaternion.identity : Tilt;
    }

    public enum SizeGesture
    {
        None,
        Grow,   // both fists, palms down
        Shrink  // both fists, palms up
    }

    /// <summary>Anything that can drive the two control crosses (keyboard, XR hands, AI, replay...).</summary>
    public abstract class ControlInputSource : MonoBehaviour
    {
        public abstract bool Read(ControlSide side, ref CrossInput input);

        /// <summary>Which size-change gesture is being held right now.</summary>
        public virtual SizeGesture ReadSizeGesture() => SizeGesture.None;

        /// <summary>Make the current hand pose the new neutral, e.g. after a respawn or resize.</summary>
        public virtual void ResetPose() { }

        /// <summary>
        /// True if this input's translation is measured relative to the player and stays within arm's
        /// reach, so reaching past the edge can mean "travel" (see PuppetTravel). The keyboard is
        /// unbounded and moves the crosses directly, so it doesn't use travel.
        /// </summary>
        public virtual bool SupportsTravel => false;

        /// <summary>Puppet height in metres, so inputs can scale speeds to the puppet's size.</summary>
        [System.NonSerialized] public float puppetHeight = 0.1f;
    }
}

using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Input driven from code: automated tests, AI puppeteers, replays.
    /// Optional noise imitates hand-tracking jitter. The noise is in absolute metres because
    /// tracking error doesn't shrink with the puppet, which is what makes small puppets harder.
    /// </summary>
    public class ScriptedControlInput : ControlInputSource
    {
        public Vector3 leftTranslation, rightTranslation;
        public float leftYaw, rightYaw;
        [Tooltip("Palm tilt in degrees (x = pitch, z = roll), world space.")]
        public Vector3 leftTilt, rightTilt;
        public float[] leftChannels = new float[3];
        public float[] rightChannels = new float[3];
        public SizeGesture sizeGesture;
        [Tooltip("Act like hand tracking: reaching past the travel radius moves the player along.")]
        public bool supportsTravel;

        [Tooltip("Hand position jitter amplitude in metres (Quest hand tracking is roughly 1-3 mm).")]
        public float positionNoise;
        [Tooltip("Finger value jitter, 0..1.")]
        public float fingerNoise;
        [Tooltip("Jitter frequency in Hz.")]
        public float noiseFrequency = 8f;
        public int seed;

        public override bool Read(ControlSide side, ref CrossInput input)
        {
            bool left = side == ControlSide.Left;
            float t = Time.time * noiseFrequency;
            float o = seed * 17.3f + (left ? 0f : 100f);

            input.Tracked = true;
            input.Translation = (left ? leftTranslation : rightTranslation) + positionNoise * new Vector3(
                Noise(t, o), Noise(t, o + 10f), Noise(t, o + 20f));
            input.Yaw = left ? leftYaw : rightYaw;
            input.Tilt = Quaternion.Euler(left ? leftTilt : rightTilt);

            var src = left ? leftChannels : rightChannels;
            for (int i = 0; i < input.Channels.Length; i++)
            {
                float v = i < src.Length ? src[i] : 0f;
                input.Channels[i] = Mathf.Clamp01(v + fingerNoise * Noise(t, o + 30f + i * 10f));
            }
            return true;
        }

        public override SizeGesture ReadSizeGesture() => sizeGesture;

        public override bool SupportsTravel => supportsTravel;

        static float Noise(float t, float offset) => Mathf.PerlinNoise(t, offset) * 2f - 1f;
    }
}

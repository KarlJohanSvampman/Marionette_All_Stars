using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Hold the size gesture without interruption to change size.
    /// Both fists palms down = grow one step, both fists palms up = shrink one step.
    /// Letting go, or changing gesture, resets the timer. After a change the gesture must be
    /// released before it can charge again, so holding doesn't chain through every size.
    /// A ring at the puppet's feet fills up while charging. Once charged, the live puppet grows or
    /// shrinks smoothly in place over growSeconds, with physics running the whole time.
    /// </summary>
    [RequireComponent(typeof(PuppetSpawner))]
    public class SizeChanger : MonoBehaviour
    {
        [Tooltip("Available heights in metres, smallest first.")]
        public float[] sizes = { 0.12f, MarionetteBuilder.ReferenceHeight };
        [Tooltip("Seconds the gesture must be held without interruption.")]
        [Range(5f, 10f)] public float holdSeconds = 7f;
        [Tooltip("Seconds the size change itself takes once charged. 0 = instant (rebuilds the puppet).")]
        [Range(0f, 5f)] public float growSeconds = 2f;
        public Color growColor = new Color(0.4f, 1f, 0.5f);
        public Color shrinkColor = new Color(1f, 0.5f, 0.3f);

        PuppetSpawner m_Spawner;
        SizeGesture m_Charging;
        float m_Progress;
        bool m_WaitForRelease;
        LineRenderer m_Ring;

        // Active size change
        bool m_Resizing;
        float m_FromHeight, m_ToHeight, m_ResizeTime;
        Vector3 m_Pivot;

        const int k_RingSegments = 48;

        /// <summary>0..1 charge progress, for UI.</summary>
        public float Progress => m_Progress / holdSeconds;
        public SizeGesture Charging => m_Charging;
        public bool IsResizing => m_Resizing;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            var ringGo = new GameObject("SizeChargeRing");
            ringGo.transform.SetParent(transform, false);
            m_Ring = ringGo.AddComponent<LineRenderer>();
            m_Ring.useWorldSpace = true;
            m_Ring.enabled = false;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) m_Ring.sharedMaterial = new Material(shader);
        }

        void Update()
        {
            var rig = m_Spawner.rig;
            var input = m_Spawner.input;
            if (rig == null || input == null) return;
            if (m_Resizing) { m_Ring.enabled = false; return; }

            SizeGesture gesture = input.ReadSizeGesture();
            int index = CurrentSizeIndex(rig.height);
            if (gesture == SizeGesture.Grow && index >= sizes.Length - 1) gesture = SizeGesture.None;
            if (gesture == SizeGesture.Shrink && index <= 0) gesture = SizeGesture.None;

            if (m_WaitForRelease)
            {
                if (gesture == SizeGesture.None) m_WaitForRelease = false;
                gesture = SizeGesture.None;
            }

            if (gesture == SizeGesture.None || gesture != m_Charging)
                m_Progress = 0f;
            m_Charging = gesture;

            if (gesture != SizeGesture.None)
            {
                m_Progress += Time.deltaTime;
                if (m_Progress >= holdSeconds)
                {
                    int next = index + (gesture == SizeGesture.Grow ? 1 : -1);
                    BeginResize(rig, sizes[next]);
                    m_Progress = 0f;
                    m_Charging = SizeGesture.None;
                    m_WaitForRelease = true;
                }
            }
            DrawRing(m_Spawner.rig);
        }

        void BeginResize(MarionetteRig rig, float target)
        {
            if (growSeconds <= 0f)
            {
                m_Spawner.Resize(target);
                return;
            }
            m_Resizing = true;
            m_FromHeight = rig.height;
            m_ToHeight = target;
            m_ResizeTime = 0f;
            m_Pivot = rig.GroundPoint;
            m_Spawner.input.ResetPose(); // crosses return to neutral so the puppet grows from rest
            var balance = rig.GetComponent<BalanceMonitor>();
            if (balance != null) balance.Pause(true); // the feet slide while scaling
        }

        void FixedUpdate()
        {
            if (!m_Resizing) return;
            var rig = m_Spawner.rig;
            m_ResizeTime += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(m_ResizeTime / growSeconds);
            float eased = t * t * (3f - 2f * t);
            // Geometric interpolation: each moment of the growth multiplies size by the same
            // factor, so 12 cm -> 57 cm doesn't look slow at the start and rushed at the end.
            float target = m_FromHeight * Mathf.Pow(m_ToHeight / m_FromHeight, eased);
            rig.ScaleBy(target / rig.height, m_Pivot);

            if (t >= 1f)
            {
                m_Resizing = false;
                var balance = rig.GetComponent<BalanceMonitor>();
                if (balance != null) balance.Pause(false);
                m_Spawner.input.puppetHeight = rig.height;
                m_Spawner.input.ResetPose();
            }
        }

        int CurrentSizeIndex(float height)
        {
            int best = 0;
            for (int i = 1; i < sizes.Length; i++)
                if (Mathf.Abs(sizes[i] - height) < Mathf.Abs(sizes[best] - height)) best = i;
            return best;
        }

        void DrawRing(MarionetteRig rig)
        {
            m_Ring.enabled = m_Charging != SizeGesture.None && m_Progress > 0f;
            if (!m_Ring.enabled) return;

            Vector3 c = rig.GroundPoint + Vector3.up * (rig.height * 0.02f);
            float radius = rig.height * 0.5f;
            int count = Mathf.Max(2, Mathf.CeilToInt(k_RingSegments * Progress) + 1);
            m_Ring.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)(count - 1)) * Progress * Mathf.PI * 2f;
                m_Ring.SetPosition(i, c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
            }
            m_Ring.widthMultiplier = rig.height * 0.04f;
            m_Ring.startColor = m_Ring.endColor = m_Charging == SizeGesture.Grow ? growColor : shrinkColor;
        }
    }
}

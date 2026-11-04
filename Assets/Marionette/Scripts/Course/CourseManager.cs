using UnityEngine;

namespace Marionette.Course
{
    /// <summary>
    /// Runs a course: checkpoints in order, timer, falls and respawn at the last checkpoint.
    /// Draws a small on-screen readout for desktop testing.
    /// </summary>
    public class CourseManager : MonoBehaviour
    {
        public PuppetSpawner spawner;
        public Checkpoint[] checkpoints;
        public FailSurface[] failSurfaces;
        [Tooltip("Seconds between a fall and the respawn, so the player sees what happened.")]
        public float respawnDelay = 1.2f;

        int m_Reached = -1;
        int m_Falls;
        float m_StartTime = -1f, m_EndTime = -1f;
        float m_RespawnAt = -1f;
        string m_LastEvent = "";
        SizeChanger m_Size;

        public int Falls => m_Falls;
        public int ReachedCheckpoint => m_Reached;

        // Start, not Awake: when the course is built at runtime, Awake runs before fields are set.
        void Start()
        {
            foreach (var c in checkpoints) if (c != null) c.manager = this;
            foreach (var f in failSurfaces) if (f != null) f.manager = this;
            if (spawner == null)
            {
                Debug.LogError("[Marionette] CourseManager has no spawner. Rebuild with Marionette > Create Obstacle Course.", this);
                enabled = false;
                return;
            }
            spawner.Spawned += OnSpawned;
            m_Size = spawner.GetComponent<SizeChanger>();
            // The spawner may already have started and spawned before we subscribed.
            if (spawner.rig != null) OnSpawned(spawner.rig);
        }

        void OnDestroy()
        {
            if (spawner != null) spawner.Spawned -= OnSpawned;
        }

        void OnSpawned(MarionetteRig rig)
        {
            foreach (var id in new[] { PuppetNodeId.Head, PuppetNodeId.Center })
            {
                var node = rig.Node(id).gameObject;
                var detector = node.GetComponent<DownedDetector>();
                if (detector == null) detector = node.AddComponent<DownedDetector>();
                detector.manager = this;
            }
        }

        public void OnCheckpoint(Checkpoint cp)
        {
            if (cp.index != m_Reached + 1) return;
            m_Reached = cp.index;
            if (cp.index == 0) m_StartTime = Time.time;
            if (cp.index == checkpoints.Length - 1)
            {
                m_EndTime = Time.time;
                m_LastEvent = $"Finished in {m_EndTime - m_StartTime:0.0}s with {m_Falls} falls";
                Debug.Log($"[Course] {m_LastEvent}");
            }
            else
            {
                m_LastEvent = $"Checkpoint: {cp.label}";
            }
        }

        public void OnFall(string reason)
        {
            if (m_RespawnAt > 0f || m_EndTime > 0f) return;
            m_Falls++;
            m_LastEvent = $"Fall: {reason}";
            Debug.Log($"[Course] {m_LastEvent}");
            m_RespawnAt = Time.time + respawnDelay;
        }

        void Update()
        {
            if (m_RespawnAt > 0f && Time.time >= m_RespawnAt)
            {
                m_RespawnAt = -1f;
                var cp = checkpoints[Mathf.Max(0, m_Reached)];
                spawner.Respawn((cp.respawnPoint != null ? cp.respawnPoint : cp.transform).position);
            }
        }

        /// <summary>Restart the whole course from the start pad.</summary>
        public void Restart()
        {
            m_Reached = -1; m_Falls = 0; m_StartTime = m_EndTime = -1f; m_LastEvent = "";
            var start = checkpoints[0];
            spawner.Respawn((start.respawnPoint != null ? start.respawnPoint : start.transform).position);
        }

        void OnGUI()
        {
            float t = m_StartTime < 0f ? 0f : (m_EndTime > 0f ? m_EndTime : Time.time) - m_StartTime;
            string next = m_Reached + 1 < checkpoints.Length ? checkpoints[m_Reached + 1].label : "done";
            string size = spawner.rig != null ? $"{spawner.rig.height * 100f:0.#} cm" : "-";
            var balance = spawner.rig != null ? spawner.rig.GetComponent<BalanceMonitor>() : null;
            string balanceText = balance != null ? $"   Balance {balance.Current}" : "";
            string text = $"Time {t:0.0}s   Falls {m_Falls}   Next: {next}   Size {size}{balanceText}\n{m_LastEvent}";
            if (m_Size != null && m_Size.IsResizing)
                text += "\nChanging size...";
            else if (m_Size != null && m_Size.Charging != SizeGesture.None)
                text += $"\n{m_Size.Charging} {m_Size.Progress * 100f:0}%";
            GUI.Label(new Rect(12, 10, 700, 70), text);
            if (GUI.Button(new Rect(12, 80, 110, 26), "Restart course")) Restart();
        }
    }
}

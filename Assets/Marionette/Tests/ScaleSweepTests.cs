using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>
    /// Builds the same puppet at several heights side by side and puts each through an identical
    /// scripted routine, measuring things that decide the best size:
    ///   - physics stability at rest (jitter, sag, explosions)
    ///   - how much 2 mm of hand-tracking jitter moves the puppet, relative to its size
    ///   - whether a scripted step lifts and advances a foot and stays upright
    ///   - natural swing period (how fast/twitchy the puppet looks)
    /// Results go to the console and Logs/ScaleSweep.md.
    /// </summary>
    public class ScaleSweepTests
    {
        static readonly float[] k_Heights = { 0.05f, 0.07f, 0.09f, 0.12f, 0.15f, 0.25f, MarionetteBuilder.ReferenceHeight };

        const float k_HandJitter = 0.002f; // metres, typical Quest hand tracking noise
        const float k_FingerJitter = 0.03f;
        const float k_Fps = 90f;

        class Run
        {
            public float h;
            public MarionetteRig rig;
            public ScriptedControlInput input;
            public Vector3 startCenter, startFoot;
            public float sag, quietJitter, noisyWobble, maxClearance, footAdvance, centerAdvance, maxStretch, maxSpeed;
            public bool exploded, uprightAtEnd;
            public readonly List<Vector3> quietHead = new List<Vector3>();
            public readonly List<Vector3> noisyHead = new List<Vector3>();
        }

        float m_ContactOffset;

        [SetUp]
        public void SetUp() => m_ContactOffset = Physics.defaultContactOffset;

        /// <summary>Put the global physics setting back and remove every puppet, so later tests start clean.</summary>
        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Physics.defaultContactOffset = m_ContactOffset;
            foreach (var go in Object.FindObjectsByType<GameObject>())
                if (go.transform.parent == null) { go.SetActive(false); Object.Destroy(go); }
        }

        [UnityTest]
        public IEnumerator ScaleSweep()
        {
            Time.captureFramerate = (int)k_Fps; // deterministic: every frame is exactly 1/90 s
            Physics.defaultContactOffset = 0.0005f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(20f, 0.1f, 6f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.GetComponent<Collider>().contactOffset = 0.0005f;

            var runs = new List<Run>();
            for (int i = 0; i < k_Heights.Length; i++)
            {
                float h = k_Heights[i];
                var pos = new Vector3((i - k_Heights.Length / 2f) * 1.6f, 0f, 0f);
                var rig = MarionetteBuilder.Build(pos, null, new MarionetteBuilder.Settings { height = h });
                var input = new GameObject($"Input_{h}").AddComponent<ScriptedControlInput>();
                input.seed = i;
                input.puppetHeight = h;
                rig.leftCross.input = input;
                rig.rightCross.input = input;
                runs.Add(new Run { h = h, rig = rig, input = input });
            }

            yield return new WaitForFixedUpdate();
            foreach (var r in runs)
            {
                r.startCenter = Pos(r, PuppetNodeId.Center);
                r.startFoot = Pos(r, PuppetNodeId.LeftFoot);
            }

            float t = 0f;
            while (t < 8f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                foreach (var r in runs) Step(r, t);
            }

            foreach (var r in runs) Finish(r);
            Report(runs);
            Time.captureFramerate = 0;
        }

        static Vector3 Pos(Run r, PuppetNodeId id) => r.rig.Node(id).Body.position;

        /// <summary>The routine. Motions scale with the puppet; jitter does not.</summary>
        static void Step(Run r, float t)
        {
            var inp = r.input;
            float h = r.h;

            // 0-1.5 settle, 1.5-3 quiet idle, 3-4.5 idle with hand jitter
            inp.positionNoise = t >= 3f && t < 4.5f ? k_HandJitter : 0f;
            inp.fingerNoise = t >= 3f && t < 4.5f ? k_FingerJitter : 0f;
            if (t >= 1.5f && t < 3f) r.quietHead.Add(Pos(r, PuppetNodeId.Head));
            if (t >= 3f && t < 4.5f) r.noisyHead.Add(Pos(r, PuppetNodeId.Head));
            if (Mathf.Abs(t - 1.5f) < 0.006f) r.sag = (r.startCenter.y - Pos(r, PuppetNodeId.Center).y) / h;

            // 4.5-6: lift the left foot and move the legs cross forward 0.35 H, body follows half.
            // 6-6.5: lower the foot. 6.5-8: settle.
            float lift = t < 4.5f ? 0f : t < 4.8f ? (t - 4.5f) / 0.3f : t < 6f ? 1f : t < 6.5f ? 1f - (t - 6f) / 0.5f : 0f;
            float advance = Mathf.Clamp01((t - 4.6f) / 1.0f);
            inp.rightChannels[0] = lift;
            inp.rightTranslation = new Vector3(0f, 0f, 0.35f * h * advance);
            inp.leftTranslation = new Vector3(0f, 0f, 0.175f * h * advance);

            var foot = Pos(r, PuppetNodeId.LeftFoot);
            r.maxClearance = Mathf.Max(r.maxClearance, (foot.y - r.startFoot.y) / h);

            foreach (var n in r.rig.nodes)
            {
                Vector3 p = n.Body.position, v = n.Body.linearVelocity;
                if (float.IsNaN(p.x) || (p - r.startCenter).magnitude > 4f * h) r.exploded = true;
                if (t > 1.5f) r.maxSpeed = Mathf.Max(r.maxSpeed, v.magnitude / h);
            }
            foreach (var cross in new[] { r.rig.leftCross, r.rig.rightCross })
            foreach (var c in cross.channels)
            {
                var s = c.controlString;
                float d = Vector3.Distance(s.handle.position, s.attachment.transform.position);
                if (t > 0.5f) r.maxStretch = Mathf.Max(r.maxStretch, (d - s.length) / s.length);
            }
        }

        static void Finish(Run r)
        {
            r.quietJitter = Spread(r.quietHead) / r.h;
            r.noisyWobble = Spread(r.noisyHead) / r.h;
            r.footAdvance = (Pos(r, PuppetNodeId.LeftFoot).z - r.startFoot.z) / r.h;
            r.centerAdvance = (Pos(r, PuppetNodeId.Center).z - r.startCenter.z) / r.h;
            Vector3 head = Pos(r, PuppetNodeId.Head), center = Pos(r, PuppetNodeId.Center);
            float feet = Mathf.Max(Pos(r, PuppetNodeId.LeftFoot).y, Pos(r, PuppetNodeId.RightFoot).y);
            r.uprightAtEnd = head.y > center.y + 0.1f * r.h && center.y > feet + 0.1f * r.h;
        }

        /// <summary>RMS distance from the mean position.</summary>
        static float Spread(List<Vector3> ps)
        {
            if (ps.Count == 0) return 0f;
            Vector3 mean = Vector3.zero;
            foreach (var p in ps) mean += p;
            mean /= ps.Count;
            float sum = 0f;
            foreach (var p in ps) sum += (p - mean).sqrMagnitude;
            return Mathf.Sqrt(sum / ps.Count);
        }

        static void Report(List<Run> runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Height | Stable | Upright after step | Sag %H | Rest jitter %H | Wobble from 2mm hand jitter %H | Foot lift %H | Foot advance %H | Body advance %H | Max string stretch % | Peak speed H/s | Hand swing period s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in runs)
            {
                var hand = r.rig.leftCross.channels[0].controlString;
                float g = 9.81f * r.rig.GetComponent<MarionetteTuning>().gravityScale;
                float period = 2f * Mathf.PI * Mathf.Sqrt(hand.length / g);
                sb.AppendLine($"| {r.h * 100f:0.#} cm | {(r.exploded ? "NO" : "yes")} | {(r.uprightAtEnd ? "yes" : "NO")} | {r.sag * 100f:0.0} | " +
                              $"{r.quietJitter * 100f:0.00} | {r.noisyWobble * 100f:0.00} | {r.maxClearance * 100f:0.0} | {r.footAdvance * 100f:0.0} | " +
                              $"{r.centerAdvance * 100f:0.0} | {r.maxStretch * 100f:0.0} | {r.maxSpeed:0.0} | {period:0.00} |");
            }
            string table = sb.ToString();
            Debug.Log("[ScaleSweep]\n" + table);
            string path = Path.Combine(Application.dataPath, "..", "Logs", "ScaleSweep.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, table);
            Assert.Pass(table);
        }
    }
}

using System.Collections;
using Marionette.Course;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    public class SizeAndCourseTests
    {
        const float k_Mouse = 0.12f;

        [SetUp]
        public void SetUp() => Time.captureFramerate = 90;

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>())
                if (go.transform.parent == null) { go.SetActive(false); Object.Destroy(go); } // off first: out of physics now, not at the end of the frame
        }

        static (PuppetSpawner, ScriptedControlInput, SizeChanger) MakePuppeteer(Vector3 at)
        {
            var go = new GameObject("Puppeteer");
            go.transform.position = at;
            var input = go.AddComponent<ScriptedControlInput>();
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            var sizes = go.AddComponent<SizeChanger>();
            sizes.sizes = new[] { k_Mouse, MarionetteBuilder.ReferenceHeight };
            sizes.holdSeconds = 7f;
            return (spawner, input, sizes);
        }

        static IEnumerator Hold(ScriptedControlInput input, SizeGesture g, float seconds)
        {
            input.sizeGesture = g;
            yield return Wait(seconds);
            input.sizeGesture = SizeGesture.None;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static GameObject Floor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            return floor;
        }

        [UnityTest]
        public IEnumerator HoldingGrowThenShrinkChangesSize()
        {
            Floor();
            var (spawner, input, _) = MakePuppeteer(Vector3.zero);
            yield return Wait(1f);
            Assert.AreEqual(k_Mouse, spawner.rig.height, 1e-4f);

            yield return Hold(input, SizeGesture.Grow, 7.5f);
            yield return Wait(2f); // the growth itself
            Assert.AreEqual(MarionetteBuilder.ReferenceHeight, spawner.rig.height, 1e-3f, "should be gnome after holding grow");

            yield return Wait(1.5f);
            Vector3 c = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Assert.IsFalse(float.IsNaN(c.x), "gnome puppet exploded after growing");

            yield return Hold(input, SizeGesture.Shrink, 7.5f);
            yield return Wait(2f);
            Assert.AreEqual(k_Mouse, spawner.rig.height, 1e-3f, "should be mouse after holding shrink");
        }

        /// <summary>
        /// Grows a live mouse puppet next to a freshly built gnome and checks the grown one ends up
        /// hanging the same way: proves lengths, anchors and masses all followed the scale.
        /// </summary>
        [UnityTest]
        public IEnumerator GrowthIsSmoothAndEndsLikeAFreshGnome()
        {
            Floor();
            var (spawner, input, _) = MakePuppeteer(Vector3.zero);
            var reference = MarionetteBuilder.Build(new Vector3(1.5f, 0f, 0f), null);
            reference.leftCross.input = reference.rightCross.input = reference.gameObject.AddComponent<ScriptedControlInput>();
            yield return Wait(1f);

            input.sizeGesture = SizeGesture.Grow;
            yield return Wait(7.05f);
            input.sizeGesture = SizeGesture.None;

            var heights = new System.Collections.Generic.List<float>();
            float end = Time.time + 2.5f;
            while (Time.time < end)
            {
                yield return new WaitForFixedUpdate();
                heights.Add(spawner.rig.height);
                foreach (var n in spawner.rig.nodes)
                    Assert.IsFalse(float.IsNaN(n.Body.position.x) || n.Body.position.magnitude > 5f, "exploded while growing");
            }

            int distinct = 0;
            float maxStep = 1f;
            for (int i = 1; i < heights.Count; i++)
            {
                Assert.GreaterOrEqual(heights[i], heights[i - 1] - 1e-6f, "growth must never reverse");
                if (heights[i] > heights[i - 1] + 1e-6f) distinct++;
                maxStep = Mathf.Max(maxStep, heights[i] / heights[i - 1]);
            }
            Assert.Greater(distinct, 100, "growth should take many small steps, not a pop");
            Assert.Less(maxStep, 1.03f, "no single step should grow more than 3%");

            yield return Wait(2f); // settle
            var rig = spawner.rig;
            Debug.Log("[Growth] grown\n" + Describe(rig) + "\n[Growth] fresh\n" + Describe(reference));
            float h = rig.height;
            Vector3 head = rig.Node(PuppetNodeId.Head).Body.position, center = rig.Node(PuppetNodeId.Center).Body.position;
            float footY = Mathf.Min(rig.Node(PuppetNodeId.LeftFoot).Body.position.y, rig.Node(PuppetNodeId.RightFoot).Body.position.y);
            Assert.Greater(head.y, center.y + 0.1f * h, "grown puppet upright");
            Assert.Less(footY, 0.1f * h, "feet stayed on the ground");

            float grownCenter = center.y / h;
            float freshCenter = reference.Node(PuppetNodeId.Center).Body.position.y / reference.height;
            Debug.Log($"[Growth] center height grown {grownCenter:0.000} H vs fresh {freshCenter:0.000} H, max step x{maxStep:0.0000}, {distinct} steps");
            Assert.AreEqual(freshCenter, grownCenter, 0.05f, "grown puppet should hang like a freshly built gnome");
        }

        /// <summary>Every length in puppet heights, so a grown and a fresh puppet can be compared line by line.</summary>
        static string Describe(MarionetteRig rig)
        {
            float h = rig.height;
            var sb = new System.Text.StringBuilder();
            Vector3 root = rig.transform.position;
            foreach (var n in rig.nodes)
                sb.AppendLine($"  node {n.id}: pos {(n.Body.position - root) / h} scale {n.transform.lossyScale / h} mass {n.Body.mass:0.000}");
            foreach (var t in rig.textile)
                sb.AppendLine($"  cloth {t.name}: rest {t.restLength / h:0.000} max {t.length / h:0.000} now {Vector3.Distance(t.fromPoint.transform.position, t.toPoint.transform.position) / h:0.000}");
            foreach (var cross in new[] { rig.leftCross, rig.rightCross })
            foreach (var c in cross.channels)
            {
                var s = c.controlString;
                var j = System.Array.Find(s.attachment.node.GetComponents<ConfigurableJoint>(), x => x.connectedBody == s.handle);
                sb.AppendLine($"  string {c.name}: len {s.length / h:0.000} now {Vector3.Distance(s.handle.position, s.attachment.transform.position) / h:0.000} " +
                              $"top {(s.handle.position - root) / h} limit {(j != null ? j.linearLimit.limit / h : -1f):0.000}");
            }
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator InterruptedHoldDoesNotChangeSize()
        {
            Floor();
            var (spawner, input, _) = MakePuppeteer(Vector3.zero);
            yield return Wait(0.5f);
            yield return Hold(input, SizeGesture.Grow, 4f);
            yield return Wait(0.2f); // let go
            yield return Hold(input, SizeGesture.Grow, 4f);
            Assert.AreEqual(k_Mouse, spawner.rig.height, 1e-4f, "two 4 s holds with a gap must not add up to 7 s");
        }

        [UnityTest]
        public IEnumerator HoldingPastAChangeDoesNotChainSizes()
        {
            Floor();
            var (spawner, input, sizes) = MakePuppeteer(Vector3.zero);
            sizes.sizes = new[] { 0.06f, k_Mouse, MarionetteBuilder.ReferenceHeight };
            yield return Wait(0.5f);
            yield return Hold(input, SizeGesture.Grow, 15f);
            Assert.AreEqual(MarionetteBuilder.ReferenceHeight, spawner.rig.height, 1e-3f, "one step up from mouse");
            yield return Hold(input, SizeGesture.Shrink, 15f);
            Assert.AreEqual(k_Mouse, spawner.rig.height, 1e-3f, "15 s shrink hold must only go one step, not two");
        }

        [UnityTest]
        public IEnumerator PuppetIdlesOnStartPadWithoutFalling()
        {
            var (spawner, _, _) = MakePuppeteer(Vector3.zero);
            var course = CourseBuilder.BuildSection1(spawner, null);
            yield return Wait(4f);
            Assert.AreEqual(0, course.Falls, "idle puppet on the start pad registered a fall");
            Assert.AreEqual(0, course.ReachedCheckpoint, "start checkpoint should register on spawn");
            Vector3 c = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Assert.Greater(c.y, CourseBuilder.DeskTop + 0.03f, "puppet should be hanging upright above the desk");
        }

        [UnityTest]
        public IEnumerator FallingIntoCoffeeRespawnsAtLastCheckpoint()
        {
            var (spawner, _, _) = MakePuppeteer(Vector3.zero);
            var course = CourseBuilder.BuildSection1(spawner, null);
            yield return Wait(1f);
            var first = spawner.rig;

            // Drop the puppet straight into the coffee spill.
            var coffee = GameObject.Find("CoffeeSpill").transform.position;
            spawner.Respawn(coffee + Vector3.up * 0.002f);
            yield return Wait(2.5f);

            Assert.GreaterOrEqual(course.Falls, 1, "touching coffee should count as a fall");
            Vector3 start = course.checkpoints[0].transform.position;
            Vector3 c = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Assert.Less(new Vector2(c.x - start.x, c.z - start.z).magnitude, 0.08f, "should respawn on the start pad");
            Assert.AreNotSame(first, spawner.rig);
        }
    }
}

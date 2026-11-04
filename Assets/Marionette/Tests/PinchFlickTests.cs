using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>Pinch & flick: recorded flicks applied to one string, palm tilt, pinch-to-string mapping.</summary>
    public class PinchFlickTests
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

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static (PuppetSpawner, ScriptedControlInput, StringFlick) MakePuppet()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            var go = new GameObject("Puppeteer");
            go.SetActive(false);
            var input = go.AddComponent<ScriptedControlInput>();
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            var flick = go.AddComponent<StringFlick>();
            go.SetActive(true);
            return (spawner, input, flick);
        }

        /// <summary>A step-like arc: forward by `forward` puppet heights, peaking `up` heights high, over `seconds`.</summary>
        static XRHandsControlInput.FlickTake Arc(PuppetNodeId target, Vector3 direction, float distance, float up, float seconds)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(seconds * 90f));
            var take = new XRHandsControlInput.FlickTake { target = target, times = new float[n], path = new Vector3[n] };
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                take.times[i] = k * seconds;
                take.path[i] = (direction * (distance * k) + Vector3.up * (up * Mathf.Sin(k * Mathf.PI))) * k_Mouse;
            }
            return take;
        }

        /// <summary>Peak forward (z) speed while the flick is being applied.</summary>
        static IEnumerator PeakSpeed(Rigidbody body, StringFlick flick, float[] result)
        {
            yield return new WaitForFixedUpdate();
            while (flick.Playing)
            {
                result[0] = Mathf.Max(result[0], body.linearVelocity.z);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator FlickMovesThePartAndFasterHitsHarder()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            var foot = spawner.rig.Node(PuppetNodeId.LeftFoot).Body;
            float h = k_Mouse;

            // Slow flick: forward 0.3 H with a little lift, drawn over 1.2 s.
            Vector3 start = foot.position;
            var slowPeak = new float[1];
            float maxForward = 0f;
            flick.Play(Arc(PuppetNodeId.LeftFoot, Vector3.forward, 0.3f, 0.15f, 1.2f));
            float end = Time.time + 1.4f;
            while (Time.time < end)
            {
                if (flick.Playing) slowPeak[0] = Mathf.Max(slowPeak[0], foot.linearVelocity.z);
                maxForward = Mathf.Max(maxForward, (foot.position - start).z / h);
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(flick.Playing, "flick finished");
            Assert.Greater(maxForward, 0.1f, "the foot follows the flick forward");

            // Let it settle, then the same arc drawn 4x faster.
            spawner.Respawn(Vector3.zero);
            yield return Wait(1.5f);
            foot = spawner.rig.Node(PuppetNodeId.LeftFoot).Body;
            var fastPeak = new float[1];
            flick.Play(Arc(PuppetNodeId.LeftFoot, Vector3.forward, 0.3f, 0.15f, 0.3f));
            yield return PeakSpeed(foot, flick, fastPeak);

            Debug.Log($"[Flick] foot forward {maxForward:0.00} H; peak forward speed slow {slowPeak[0] / h:0.00} H/s, fast {fastPeak[0] / h:0.00} H/s");
            Assert.Greater(fastPeak[0], slowPeak[0] * 1.3f, "a faster flick should move the foot faster");
            Vector3 c = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Assert.IsFalse(float.IsNaN(c.x), "puppet stable after flicks");
        }

        [UnityTest]
        public IEnumerator FlickingOneHandDragsTheBodyAndTheOtherLimbsAlong()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            float h = k_Mouse;
            Vector3 P(PuppetNodeId id) => rig.Node(id).Body.position;
            Vector3 center0 = P(PuppetNodeId.Center), head0 = P(PuppetNodeId.Head), otherHand0 = P(PuppetNodeId.LeftHand);
            var rest = new System.Collections.Generic.Dictionary<PuppetNodeId, Quaternion>();
            foreach (var id in new[] { PuppetNodeId.LeftHand, PuppetNodeId.RightHand, PuppetNodeId.LeftFoot, PuppetNodeId.RightFoot })
                rest[id] = Quaternion.Inverse(rig.Node(PuppetNodeId.Center).Body.rotation) * rig.Node(id).Body.rotation;

            // Right hand flicked out to the right and forward.
            flick.Play(Arc(PuppetNodeId.RightHand, new Vector3(1f, 0f, 1f).normalized, 0.5f, 0.1f, 0.4f));
            float maxCenter = 0f, maxHead = 0f, maxOtherHand = 0f, maxSpin = 0f;
            float end = Time.time + 1f;
            while (Time.time < end)
            {
                maxCenter = Mathf.Max(maxCenter, Vector3.Distance(P(PuppetNodeId.Center), center0) / h);
                maxHead = Mathf.Max(maxHead, Vector3.Distance(P(PuppetNodeId.Head), head0) / h);
                maxOtherHand = Mathf.Max(maxOtherHand, Vector3.Distance(P(PuppetNodeId.LeftHand), otherHand0) / h);
                // How far each hand and foot has turned relative to the body since the start: spinning
                // would show as a large angle; the limits keep it to tens of degrees.
                Quaternion body = rig.Node(PuppetNodeId.Center).Body.rotation;
                foreach (var id in new[] { PuppetNodeId.LeftHand, PuppetNodeId.RightHand, PuppetNodeId.LeftFoot, PuppetNodeId.RightFoot })
                {
                    Quaternion rel = Quaternion.Inverse(body) * rig.Node(id).Body.rotation;
                    maxSpin = Mathf.Max(maxSpin, Quaternion.Angle(rest[id], rel));
                }
                yield return new WaitForFixedUpdate();
            }
            Debug.Log($"[Flick] right hand flick dragged: body {maxCenter:0.00} H, head {maxHead:0.00} H, other hand {maxOtherHand:0.00} H; most a limb turned against the body {maxSpin:0} deg");
            Assert.Less(maxCenter, 0.2f, "a hand drag sways the body but doesn't carry it off: the feet do the walking");
            Assert.Greater(maxOtherHand, 0.01f, "the other hand is dragged along too");
            var tuning = rig.GetComponent<MarionetteTuning>();
            float limit = Mathf.Max(tuning.limbPitchLimit, tuning.limbYawLimit, tuning.limbRollLimit) * 1.75f; // combined axes, plus solver give
            Assert.Less(maxSpin, limit, "hands and feet turn only within their limits, they don't spin");
            float neck0 = Vector3.Distance(head0, center0), neck = Vector3.Distance(P(PuppetNodeId.Head), P(PuppetNodeId.Center));
            Assert.Less(Mathf.Abs(neck - neck0), 0.1f * h, "the head stays on the body");
        }

        [UnityTest]
        public IEnumerator HeadStaysUpWithoutAHeadString()
        {
            var (spawner, _, _) = MakePuppet();
            yield return Wait(3f);
            var rig = spawner.rig;
            Vector3 head = rig.Node(PuppetNodeId.Head).Body.position, center = rig.Node(PuppetNodeId.Center).Body.position;
            Debug.Log($"[Flick] at rest: head {(head.y - center.y) / k_Mouse:0.00} H above the body, body at {center.y / k_Mouse:0.00} H");
            Assert.Greater(head.y - center.y, 0.25f * k_Mouse, "head rides on top of the body");
            Assert.Greater(center.y, 0.3f * k_Mouse, "body hangs from the arms, not collapsed on the floor");
        }

        [UnityTest]
        public IEnumerator HardSidewaysKickOfAFootIsCaughtOrUnbalances()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            var balance = spawner.rig.GetComponent<BalanceMonitor>();
            Assert.IsFalse(balance.ControlsOverridden, "standing before the flick");
            bool off = false;
            balance.StateChanged += s => off |= s is BalanceMonitor.State.Staggering or BalanceMonitor.State.Toppling or BalanceMonitor.State.Falling;

            flick.Play(Arc(PuppetNodeId.RightFoot, Vector3.right, 0.9f, 0.3f, 0.25f));
            yield return Wait(1.5f);
            Assert.IsTrue(off || balance.StepsTaken > 0, "kicking a foot hard sideways should make it step to catch itself, or lose its balance");
        }

        [UnityTest]
        public IEnumerator TiltingAHandTiltsItsCross()
        {
            var (spawner, input, _) = MakePuppet();
            yield return Wait(1f);
            var cross = spawner.rig.rightCross;
            float LeftTop() => cross.channels[0].controlString.handle.position.y;
            float RightTop() => cross.channels[1].controlString.handle.position.y;
            Assert.AreEqual(LeftTop(), RightTop(), 0.01f * k_Mouse, "level hand, level feet strings");

            input.rightTilt = new Vector3(0f, 0f, 15f); // roll
            yield return Wait(0.5f);
            Debug.Log($"[Flick] 15 deg roll: foot string tops differ by {(LeftTop() - RightTop()) / k_Mouse:0.00} H");
            Assert.Greater(Mathf.Abs(LeftTop() - RightTop()), 0.02f * k_Mouse, "rolling the leg hand raises one foot string and lowers the other");
        }

        [Test]
        public void PinchCombinationsPickStrings()
        {
            var hands = new GameObject("Hands").AddComponent<XRHandsControlInput>();
            Assert.AreEqual(PuppetNodeId.LeftHand, hands.TargetFor(1), "thumb + index");
            Assert.AreEqual(PuppetNodeId.RightHand, hands.TargetFor(2), "thumb + middle");
            Assert.AreEqual(PuppetNodeId.LeftFoot, hands.TargetFor(4), "thumb + ring");
            Assert.AreEqual(PuppetNodeId.RightFoot, hands.TargetFor(8), "thumb + little");
            Assert.IsNull(hands.TargetFor(3), "two fingers at once is ambiguous");
            Object.DestroyImmediate(hands.gameObject);
        }
    }
}

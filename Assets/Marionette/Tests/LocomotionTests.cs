using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>Momentum from flicks, automatic steps, tripping, toppling with waving arms, counter-leaning.</summary>
    public class LocomotionTests
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

        /// <summary>What happened over a stretch of time.</summary>
        class Log
        {
            public readonly HashSet<BalanceMonitor.State> states = new HashSet<BalanceMonitor.State>();
            public float maxLean, maxHandSpeedWhileToppling, maxArmSpreadWhileStaggering;
            public float lostAt = float.PositiveInfinity; // seconds until it tripped or toppled
            public bool sawOverride;
            public int steps;
            public Vector3 centerStart, centerEnd;
            public float centerEndHeight;
            public override string ToString() =>
                $"states [{string.Join(", ", states)}], max lean {maxLean:0} deg, steps {steps}, override {sawOverride}, " +
                $"body moved ({(centerEnd - centerStart).x / k_Mouse:0.00}, {(centerEnd - centerStart).z / k_Mouse:0.00}) H, " +
                $"body height {centerEndHeight / k_Mouse:0.00} H, hand speed while toppling {maxHandSpeedWhileToppling / k_Mouse:0.0} H/s, " +
                $"arm spread while staggering {maxArmSpreadWhileStaggering / k_Mouse:0.00} H";
        }

        static IEnumerator Watch(PuppetSpawner spawner, float seconds, Log log)
        {
            var rig = spawner.rig;
            var balance = rig.GetComponent<BalanceMonitor>();
            log.centerStart = rig.Node(PuppetNodeId.Center).Body.position;
            float end = Time.time + seconds, start = Time.time;
            while (Time.time < end && spawner.rig == rig)
            {
                yield return new WaitForFixedUpdate();
                log.states.Add(balance.Current);
                if (balance.ControlsOverridden && float.IsPositiveInfinity(log.lostAt)) log.lostAt = Time.time - start;
                log.maxLean = Mathf.Max(log.maxLean, balance.Lean);
                log.sawOverride |= rig.leftCross.overridden;
                if (balance.Current == BalanceMonitor.State.Toppling)
                    foreach (var id in new[] { PuppetNodeId.LeftHand, PuppetNodeId.RightHand })
                        log.maxHandSpeedWhileToppling = Mathf.Max(log.maxHandSpeedWhileToppling, rig.Node(id).Body.linearVelocity.magnitude);
                if (balance.Current == BalanceMonitor.State.Staggering)
                    log.maxArmSpreadWhileStaggering = Mathf.Max(log.maxArmSpreadWhileStaggering,
                        Vector3.Distance(rig.Node(PuppetNodeId.LeftHand).Body.position, rig.Node(PuppetNodeId.RightHand).Body.position));
                log.steps = balance.StepsTaken;
                log.centerEnd = rig.Node(PuppetNodeId.Center).Body.position;
                log.centerEndHeight = log.centerEnd.y;
            }
        }

        static bool LostIt(Log log) =>
            log.states.Contains(BalanceMonitor.State.Staggering) || log.states.Contains(BalanceMonitor.State.Toppling) ||
            log.states.Contains(BalanceMonitor.State.Falling);

        [UnityTest]
        public IEnumerator AnAutomaticStepPutsTheFootWhereItWasSent()
        {
            var (spawner, _, _) = MakePuppet();
            yield return Wait(1.5f);
            // The stepping mechanism on its own: a step the puppet doesn't need would (rightly) tip it over.
            spawner.rig.GetComponent<BalanceMonitor>().enabled = false;
            var steps = spawner.rig.GetComponent<AutoStep>();
            var foot = steps.Foot(1);
            Vector3 start = foot.position;
            Vector3 target = new Vector3(start.x, 0f, start.z + 0.3f * k_Mouse);
            steps.Begin(1, target, stagger: false);
            // Measured at landing: afterwards the leg settles back under the body, which is intended.
            float moved = 0f;
            for (int i = 0; i < 60 && steps.Stepping; i++) yield return new WaitForFixedUpdate();
            moved = (foot.position - start).z / k_Mouse;
            Debug.Log($"[Locomotion] step sent 0.30 H forward, foot landed {moved:0.00} H");
            Assert.AreEqual(0.3f, moved, 0.08f, "the foot lands where the step sent it");
        }

        [UnityTest]
        public IEnumerator SmallStepForwardStaysUp()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            flick.Play(Arc(PuppetNodeId.LeftFoot, Vector3.forward, 0.25f, 0.1f, 0.5f));
            var log = new Log();
            yield return Watch(spawner, 3f, log);
            Debug.Log($"[Locomotion] small step: {log}");
            Assert.IsFalse(LostIt(log), "a small careful step shouldn't lose balance");
        }

        [UnityTest]
        public IEnumerator SwingingAHandSwaysButDoesntWalk()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            var hand = rig.Node(PuppetNodeId.RightHand).Body;
            var other = rig.Node(PuppetNodeId.LeftHand).Body;
            var center = rig.Node(PuppetNodeId.Center).Body;
            TextileLink arm = null;
            foreach (var link in rig.textile) if (link.to.id == PuppetNodeId.RightHand) arm = link;
            float ArmStretch() => Vector3.Distance(arm.fromPoint.transform.position, arm.toPoint.transform.position) / arm.restLength;
            flick.Play(Arc(PuppetNodeId.RightHand, Vector3.forward, 1.5f, 0.3f, 0.35f)); // a big swing, further than the arm reaches
            var log = new Log();
            float farthest = 0f, otherSpeed = 0f;
            float end = Time.time + 3f;
            var balance = rig.GetComponent<BalanceMonitor>();
            log.centerStart = center.position;
            while (Time.time < end)
            {
                yield return new WaitForFixedUpdate();
                farthest = Mathf.Max(farthest, ArmStretch());
                otherSpeed = Mathf.Max(otherSpeed, (other.linearVelocity - center.linearVelocity).magnitude);
                log.states.Add(balance.Current);
            }
            Vector3 moved = center.position - log.centerStart;
            Debug.Log($"[Locomotion] hand swing: states [{string.Join(", ", log.states)}], arm stretched to {farthest:0.00}x its length, " +
                      $"other hand swung at {otherSpeed / k_Mouse:0.00} H/s, body moved {moved.z / k_Mouse:0.00} H");
            Assert.IsFalse(LostIt(log), "swinging an arm sways the puppet but doesn't knock it over");
            Assert.Less(moved.z, 0.2f * k_Mouse, "and doesn't walk it: the feet do the walking");
            Assert.Less(farthest, 1.35f, "the arm doesn't stretch out past its reach");
            Assert.Greater(otherSpeed, 0.15f * k_Mouse, "the other arm is loose and swings along");
        }

        [UnityTest]
        public IEnumerator ALittleWaveDoesNothingBad()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            int n = 90;
            var take = new XRHandsControlInput.FlickTake { target = PuppetNodeId.LeftHand, times = new float[n], path = new Vector3[n] };
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                take.times[i] = k;
                take.path[i] = new Vector3(Mathf.Sin(k * 4f * Mathf.PI) * 0.1f, 0.03f * Mathf.Sin(k * Mathf.PI), 0f) * k_Mouse; // waving side to side
            }
            flick.Play(take);
            var log = new Log();
            yield return Watch(spawner, 3f, log);
            Debug.Log($"[Locomotion] little wave: {log}");
            Assert.IsFalse(LostIt(log), "a little wave never knocks it over");
        }

        [UnityTest]
        public IEnumerator AHardKickKnocksItOver()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            flick.momentumGain = 2f;
            flick.Play(Arc(PuppetNodeId.RightFoot, Vector3.forward, 1.5f, 0.1f, 0.15f)); // a hard kick
            var log = new Log();
            yield return Watch(spawner, 5f, log);
            Debug.Log($"[Locomotion] huge shove: {log}");
            Assert.IsTrue(log.states.Contains(BalanceMonitor.State.Staggering) || log.states.Contains(BalanceMonitor.State.Toppling),
                "a huge shove should trip or topple the puppet");
            Assert.IsTrue(log.sawOverride, "controls are overridden while it loses its balance");
            Assert.IsTrue(log.states.Contains(BalanceMonitor.State.Falling), "and it ends in a fall");
            // A hard kick forward takes the leg out from under it, so it usually lands on its back: the direction isn't checked.
        }

        [UnityTest]
        public IEnumerator LeaningTooFarTopplesWithWavingArmsAndFallsThatWay(
            [Values(0, 1, 2)] int direction)
        {
            var (spawner, input, _) = MakePuppet();
            yield return Wait(1.5f);
            // Right palm pitch leans forward (fingers down) or back; left palm roll leans to the side.
            Vector3 expected = direction switch { 0 => Vector3.forward, 1 => Vector3.back, _ => Vector3.right };
            if (direction == 0) input.rightTilt = new Vector3(45f, 0f, 0f);
            else if (direction == 1) input.rightTilt = new Vector3(-45f, 0f, 0f);
            else input.leftTilt = new Vector3(0f, 0f, -45f);
            var log = new Log();
            yield return Watch(spawner, 4f, log);
            Debug.Log($"[Locomotion] lean {expected}: {log}");
            Assert.IsTrue(log.states.Contains(BalanceMonitor.State.Toppling), "leaning past the limit topples");
            Assert.Greater(log.maxHandSpeedWhileToppling, 2f * k_Mouse, "arms wave while toppling");
            Assert.IsTrue(log.states.Contains(BalanceMonitor.State.Falling), "then it falls");
            Assert.Less(log.centerEndHeight, 0.35f * k_Mouse, "flat on the ground");
            Assert.Greater(Vector3.Dot(log.centerEnd - log.centerStart, expected), 0.1f * k_Mouse, "it falls the way it leaned");
        }

        [UnityTest]
        public IEnumerator AHardStepStaggersItWithArmsOut()
        {
            var (spawner, _, flick) = MakePuppet();
            yield return Wait(1.5f);
            flick.Play(Arc(PuppetNodeId.RightFoot, Vector3.forward, 1f, 0.1f, 0.25f)); // a hard step forward
            var log = new Log();
            yield return Watch(spawner, 4f, log);
            Debug.Log($"[Locomotion] shove: {log}");
            Assert.IsTrue(log.states.Contains(BalanceMonitor.State.Staggering), "a shove makes it stagger");
            Assert.Greater(log.maxArmSpreadWhileStaggering, 0.55f * k_Mouse, "arms brace out while staggering");
            Assert.Greater(log.steps, 1, "stumbling steps");
        }

        [Test]
        public void EachPalmLeansOneWay()
        {
            var roll = Quaternion.Euler(10f, 0f, -20f); // both pitch and roll
            Vector3 l = XRHandsControlInput.KeepTiltAxis(roll, sideways: true) * Vector3.up;
            Vector3 r = XRHandsControlInput.KeepTiltAxis(roll, sideways: false) * Vector3.up;
            Assert.AreEqual(0f, l.z, 1e-4f, "left palm: no forward/back");
            Assert.Greater(l.x, 0.2f, "left palm: sideways");
            Assert.AreEqual(0f, r.x, 1e-4f, "right palm: no sideways");
            Assert.Greater(r.z, 0.1f, "right palm: fingers down leans forward");
        }

        [UnityTest]
        public IEnumerator LeaningBackAgainstAShoveHelps()
        {
            float lostAlone, lostCountered;
            {
                var (spawner, _, flick) = MakePuppet();
                yield return Wait(1.5f);
                flick.Play(Arc(PuppetNodeId.RightFoot, Vector3.forward, 1f, 0.1f, 0.25f));
                var log = new Log();
                yield return Watch(spawner, 3f, log);
                lostAlone = log.lostAt;
                Debug.Log($"[Locomotion] shove alone: {log}");
            }
            TearDown(); SetUp();
            yield return null;
            {
                var (spawner, input, flick) = MakePuppet();
                yield return Wait(1.5f);
                input.rightTilt = new Vector3(-15f, 0f, 0f); // right palm tipped back: lean back against it
                flick.Play(Arc(PuppetNodeId.RightFoot, Vector3.forward, 1f, 0.1f, 0.25f));
                var log = new Log();
                yield return Watch(spawner, 3f, log);
                lostCountered = log.lostAt;
                Debug.Log($"[Locomotion] shove with counter-lean: {log}");
            }
            Debug.Log($"[Locomotion] lost balance after: alone {lostAlone:0.00} s, leaning back {lostCountered:0.00} s");
            Assert.Greater(lostCountered, lostAlone, "leaning back against the shove should keep it up longer, or save it");
        }
    }
}

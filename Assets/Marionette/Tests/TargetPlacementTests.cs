using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>
    /// Point & place: select a part, aim (marker + ghost stance, balance protected), commit, and the puppet moves
    /// the part there while balance counts again. Lean from the line between the hands.
    /// </summary>
    public class TargetPlacementTests
    {
        const float k_Mouse = 0.12f;

        [SetUp]
        public void SetUp() => Time.captureFramerate = 90;

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>())
                if (go.transform.parent == null) { go.SetActive(false); Object.Destroy(go); }
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static (PuppetSpawner, ScriptedControlInput, StringFlick, PartPlacer) MakePuppet()
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
            var placer = go.AddComponent<PartPlacer>();
            go.SetActive(true);
            return (spawner, input, flick, placer);
        }

        /// <summary>A ray from above and behind, down onto a floor point.</summary>
        static Ray PointAt(Vector3 floorPoint)
        {
            Vector3 from = floorPoint + new Vector3(0f, 0.6f, -0.5f) * k_Mouse;
            return new Ray(from, (floorPoint - from).normalized);
        }

        static bool Lost(BalanceMonitor b) => b.ControlsOverridden;

        [UnityTest]
        public IEnumerator SelectingShowsTheTargetAndCantFall()
        {
            var (spawner, input, _, placer) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            var balance = rig.GetComponent<BalanceMonitor>();
            var foot = rig.Node(PuppetNodeId.LeftFoot).Body;
            Vector3 target = new Vector3(foot.position.x, 0f, foot.position.z + 0.3f * k_Mouse);

            Assert.IsTrue(placer.BeginSelect(PuppetNodeId.LeftFoot), "select the left foot");
            Assert.IsTrue(balance.Protected, "the puppet is protected while selecting");
            placer.Aim(PointAt(target));
            Assert.IsTrue(placer.TargetValid && placer.TargetInReach, "a short step is a valid target within reach");
            Assert.IsTrue(placer.GhostVisible, "the ghost stance shows");
            Vector3 ghostFoot = placer.Ghost.PartPosition(PuppetNodeId.LeftFoot);
            Assert.Less(Vector3.Distance(new Vector3(ghostFoot.x, 0f, ghostFoot.z), target), 0.03f * k_Mouse, "the ghost's foot is at the target");

            // Try hard to knock it over while selecting: a steep lean with the hands and a shove.
            input.useHandLine = true;
            input.handLineLean = new Vector3(0f, 0f, -50f);
            rig.Node(PuppetNodeId.Center).Body.linearVelocity += Vector3.right * (2f * k_Mouse);
            bool lost = false;
            float end = Time.time + 2f;
            while (Time.time < end)
            {
                placer.Aim(PointAt(target));
                lost |= Lost(balance);
                yield return new WaitForFixedUpdate();
            }
            Debug.Log($"[Target] selecting under a shove and a 50 deg lean: lost balance {lost}, lean {balance.Lean:0} deg");
            Assert.IsFalse(lost, "it can't fall while a target is being chosen");
            placer.Cancel();
            Assert.IsFalse(balance.Protected, "cancelling ends the protection");
            Assert.IsFalse(placer.GhostVisible);
        }

        [UnityTest]
        public IEnumerator CommittingStepsTheFootThere()
        {
            var (spawner, _, _, placer) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            var balance = rig.GetComponent<BalanceMonitor>();
            var foot = rig.Node(PuppetNodeId.RightFoot).Body;
            Vector3 target = new Vector3(foot.position.x, 0f, foot.position.z + 0.3f * k_Mouse);
            placer.BeginSelect(PuppetNodeId.RightFoot);
            placer.Aim(PointAt(target));
            Assert.IsTrue(placer.Commit(), "commit the target");
            Assert.IsFalse(balance.Protected, "balance counts again once the move is chosen");
            Assert.IsTrue(placer.GhostVisible, "the ghost stays as a guide while it moves");
            bool lost = false;
            float end = Time.time + 2.5f;
            while (Time.time < end) { lost |= Lost(balance); yield return new WaitForFixedUpdate(); }
            float off = Vector3.Distance(new Vector3(foot.position.x, 0f, foot.position.z), target) / k_Mouse;
            Debug.Log($"[Target] committed step: foot {off:0.00} H from the target, lost balance {lost}");
            Assert.AreEqual(PartPlacer.Phase.Idle, placer.Current, "the move is done");
            Assert.IsFalse(placer.GhostVisible, "and the ghost is gone");
            Assert.Less(off, 0.1f, "the foot stepped to the target");
            Assert.IsFalse(lost, "a short step doesn't lose balance");
        }

        [UnityTest]
        public IEnumerator OutOfRangeCantBeChosen()
        {
            var (spawner, _, _, placer) = MakePuppet();
            yield return Wait(1.5f);
            var foot = spawner.rig.Node(PuppetNodeId.LeftFoot).Body;
            placer.BeginSelect(PuppetNodeId.LeftFoot);
            placer.Aim(PointAt(foot.position + Vector3.forward * (3f * k_Mouse)));
            Assert.IsFalse(placer.TargetValid, "3 H away is out of range");
            Assert.IsFalse(placer.GhostVisible, "no ghost for it");
            Assert.IsFalse(placer.Commit(), "and it can't be committed");
            placer.Cancel();
        }

        [UnityTest]
        public IEnumerator ALongStepDragsTheBodyAlong()
        {
            var (spawner, _, _, placer) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            var foot = rig.Node(PuppetNodeId.LeftFoot).Body;
            var center = rig.Node(PuppetNodeId.Center).Body;
            Vector3 start = center.position;
            Vector3 target = new Vector3(foot.position.x, 0f, foot.position.z + 0.95f * k_Mouse);
            placer.BeginSelect(PuppetNodeId.LeftFoot);
            placer.Aim(PointAt(target));
            Assert.IsTrue(placer.TargetValid && !placer.TargetInReach, "a long step: allowed, but beyond a comfortable step");
            placer.Commit();
            yield return Wait(2f);
            float moved = (center.position - start).z / k_Mouse;
            Debug.Log($"[Target] long step: body moved {moved:0.00} H forward, state {rig.GetComponent<BalanceMonitor>().Current}");
            Assert.Greater(moved, 0.2f, "the body is dragged toward the far foot");
        }

        [UnityTest]
        public IEnumerator CommittingAHandReachesAndStays()
        {
            var (spawner, _, flick, placer) = MakePuppet();
            yield return Wait(1.5f);
            var rig = spawner.rig;
            var hand = rig.Node(PuppetNodeId.RightHand).Body;
            // A box just in front of the hand to rest it on.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = Vector3.one * (0.3f * k_Mouse);
            box.transform.position = hand.position + new Vector3(0f, -0.2f, 0.12f) * k_Mouse;
            Vector3 top = box.transform.position + Vector3.up * (0.15f * k_Mouse);
            placer.BeginSelect(PuppetNodeId.RightHand);
            placer.Aim(new Ray(top + Vector3.up * (0.5f * k_Mouse), Vector3.down));
            Assert.IsTrue(placer.TargetValid && placer.TargetInReach, "the box top is within the hand's reach");
            Assert.IsTrue(placer.Commit());
            yield return Wait(2f);
            float off = Vector3.Distance(hand.worldCenterOfMass, placer.PartGoal) / k_Mouse;
            Debug.Log($"[Target] hand: {off:0.00} H from its target, held {flick.Holding}");
            Assert.Less(off, 0.1f, "the hand reached its target");
            Assert.IsTrue(flick.Holding, "and stays there");
        }

        [UnityTest]
        public IEnumerator TheLineBetweenTheHandsLeansTheBody()
        {
            var (spawner, input, _, _) = MakePuppet();
            yield return Wait(1f);
            var balance = spawner.rig.GetComponent<BalanceMonitor>();
            input.useHandLine = true;
            input.handLineLean = new Vector3(0f, 0f, -30f); // the line slopes down to the right
            yield return Wait(0.3f);
            Vector3 up = balance.LeanTarget * Vector3.up;
            float lean = Vector3.Angle(Vector3.up, up);
            Debug.Log($"[Target] 30 deg hand line: lean target {lean:0.0} deg toward ({up.x:0.00}, {up.z:0.00})");
            Assert.Greater(up.x, 0.05f, "leans toward where the line slopes down to");
            Assert.AreEqual((30f - balance.handLineDeadZone) * balance.handLineControl, lean, 0.5f, "by the slope past the dead zone");
        }

        [Test]
        public void HandLineSlopeGivesDirectionAndAngle()
        {
            Vector3 Up(Quaternion q) => q * Vector3.up;
            // Hands side by side, right one 10 cm lower.
            var r = XRHandsControlInput.HandLineTilt(new Vector3(-0.2f, 1f, 0.3f), new Vector3(0.2f, 0.9f, 0.3f), Quaternion.identity);
            Assert.Greater(Up(r).x, 0.1f, "right hand lower: lean right");
            Assert.AreEqual(Mathf.Atan2(0.1f, 0.4f) * Mathf.Rad2Deg, Vector3.Angle(Vector3.up, Up(r)), 0.1f, "as steep as the line");
            // One hand in front of the other, the front one lower.
            var f = XRHandsControlInput.HandLineTilt(new Vector3(0f, 1f, 0.2f), new Vector3(0f, 0.92f, 0.45f), Quaternion.identity);
            Assert.Greater(Up(f).z, 0.1f, "front hand lower: lean forward");
            // Level hands: upright.
            var level = XRHandsControlInput.HandLineTilt(new Vector3(-0.2f, 1f, 0.3f), new Vector3(0.2f, 1f, 0.3f), Quaternion.identity);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.up, Up(level)), 0.01f);
        }
    }
}

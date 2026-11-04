using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>Right-hand yaw turns the puppet on the spot, one step at a time, and the strings (and so the view) come round.</summary>
    public class StepTurnTests
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

        static float Heading(Quaternion r)
        {
            Vector3 f = Vector3.ProjectOnPlane(r * Vector3.forward, Vector3.up);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        [UnityTest]
        public IEnumerator RotatingTheRightHandTurnsOneStepAtATime([Values(1f, -1f)] float sign)
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
            go.SetActive(true);
            yield return Wait(1.5f);

            var rig = spawner.rig;
            var turn = rig.GetComponent<StepTurn>();
            var balance = rig.GetComponent<BalanceMonitor>();
            var center = rig.Node(PuppetNodeId.Center).Body;
            float startBody = Heading(center.rotation), startStrings = Heading(rig.leftCross.neutral.rotation);
            Vector3 startPos = center.position;

            input.rightYaw = 30f * sign; // clockwise (+) = turn right
            bool fell = false;
            float end = Time.time + 1.2f;
            while (Time.time < end) { yield return new WaitForFixedUpdate(); fell |= balance.ControlsOverridden; }
            int afterFirst = turn.Turns;
            end = Time.time + 2.8f;
            while (Time.time < end) { yield return new WaitForFixedUpdate(); fell |= balance.ControlsOverridden; }
            input.rightYaw = 0f;
            end = Time.time + 1.5f;
            while (Time.time < end) { yield return new WaitForFixedUpdate(); fell |= balance.ControlsOverridden; }

            float body = Mathf.DeltaAngle(startBody, Heading(center.rotation));
            float strings = Mathf.DeltaAngle(startStrings, Heading(rig.leftCross.neutral.rotation));
            float drift = Vector3.ProjectOnPlane(center.position - startPos, Vector3.up).magnitude;
            Debug.Log($"[Turn] yaw {30f * sign}: {turn.Turns} turns ({afterFirst} after 1.2 s), body turned {body:0} deg, strings/view {strings:0} deg, " +
                      $"drifted {drift / k_Mouse:0.00} H, fell {fell}, steps {balance.StepsTaken}");
            Assert.IsFalse(fell, "turning on the spot shouldn't knock it over");
            Assert.AreEqual(1, afterFirst, "one turn, then a pause before the next");
            Assert.GreaterOrEqual(turn.Turns, 2, "the hand is still rotated, so it keeps turning");
            Assert.Greater(body * sign, 40f, "the body turns the way the hand rotated");
            Assert.Greater(strings * sign, 40f, "the strings (and the view) come round with it");
            Assert.Less(drift, 0.4f * k_Mouse, "on the spot");
        }
    }
}

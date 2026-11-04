using System.Collections;
using System.Collections.Generic;
using Marionette.Course;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>Episode 1: the walkway/ramp/stairs stage, balance, foot placement and the follow view.</summary>
    public class Episode1Tests
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

        static (PuppetSpawner, ScriptedControlInput, CourseManager, Episode1Builder.Layout) MakeStage()
        {
            var go = new GameObject("Puppeteer");
            go.SetActive(false);
            var input = go.AddComponent<ScriptedControlInput>();
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            go.AddComponent<SizeChanger>();
            var manager = Episode1Builder.Build(spawner, k_Mouse, null);
            go.SetActive(true);
            return (spawner, input, manager, new Episode1Builder.Layout(k_Mouse));
        }

        static BalanceMonitor Balance(PuppetSpawner s) => s.rig.GetComponent<BalanceMonitor>();

        [UnityTest]
        public IEnumerator StandsStillOnEveryLevelOfTheStage()
        {
            var (spawner, _, manager, L) = MakeStage();
            yield return Wait(1f);

            var spots = new List<(string, Vector3)> { ("start", L.Start), ("block A", L.BlockA), ("walkway", L.WalkwayMiddle), ("block B", L.BlockB) };
            for (int i = 0; i < L.TreadCount; i++) spots.Add(($"step {i + 1}", L.Step(i)));
            spots.Add(("finish", L.Finish));

            foreach (var (name, spot) in spots)
            {
                spawner.Respawn(spot);
                var watched = Balance(spawner);
                watched.StateChanged += s => Debug.Log($"[Episode1] {name} t={Time.time:0.00}: {s}, margin {watched.Margin:0.00}, feet {watched.GroundedFeet}");
                yield return Wait(2.5f);
                var b = Balance(spawner);
                Debug.Log($"[Episode1] {name}: {b.Current}, margin {b.Margin:0.00} H, feet down {b.GroundedFeet}");
                Assert.AreEqual(0, manager.Falls, $"fell while standing still on {name}");
                Assert.AreNotEqual(BalanceMonitor.State.Falling, b.Current, $"lost balance standing on {name}");
                Assert.AreEqual(2, b.GroundedFeet, $"both feet should rest on {name}");
            }
        }

        [UnityTest]
        public IEnumerator LiftingBothFeetMakesItFall()
        {
            var (spawner, input, manager, L) = MakeStage();
            spawner.Respawn(L.BlockA);
            yield return Wait(2f);
            bool fell = false;
            Balance(spawner).StateChanged += s => fell |= s == BalanceMonitor.State.Falling;

            input.rightChannels[0] = input.rightChannels[1] = 1f; // both feet up
            yield return Wait(3f);
            Assert.IsTrue(fell, "holding both feet in the air should make it fall");
            Assert.GreaterOrEqual(manager.Falls, 1, "the fall should count on the course");
        }

        [UnityTest]
        public IEnumerator CarefulOnTheWalkwayStaysUpCarelessFalls()
        {
            var (spawner, input, manager, L) = MakeStage();
            spawner.Respawn(L.WalkwayMiddle);
            yield return Wait(2f);
            Assert.AreEqual(0, manager.Falls, "standing on the walkway");

            // Careful: a small sideways adjustment.
            input.leftTranslation = input.rightTranslation = Vector3.right * (0.05f * k_Mouse);
            yield return Wait(2f);
            Debug.Log($"[Episode1] walkway after small lean: {Balance(spawner).Current}, margin {Balance(spawner).Margin:0.00} H");
            Assert.AreEqual(0, manager.Falls, "a small lean shouldn't fall");

            // Careless: swing the whole puppet a long way sideways.
            input.leftTranslation = input.rightTranslation = Vector3.right * (0.8f * k_Mouse);
            for (int i = 0; i < 8; i++)
            {
                yield return Wait(0.5f);
                var b = Balance(spawner);
                Vector3 c = spawner.rig.Node(PuppetNodeId.Center).Body.position;
                Vector3 f = spawner.rig.Node(PuppetNodeId.LeftFoot).Body.position;
                Debug.Log($"[Episode1] swing t={i * 0.5f + 0.5f}: {b.Current}, margin {b.Margin:0.00} H, feet {b.GroundedFeet}, " +
                          $"center x {c.x / k_Mouse:0.00} H, left foot x {f.x / k_Mouse:0.00} H y {f.y / k_Mouse:0.00} H");
            }
            Assert.GreaterOrEqual(manager.Falls, 1, "a big sideways swing over the pit should fall");
        }

        [UnityTest]
        public IEnumerator FeetLieFlatOnTheRamp()
        {
            var (spawner, _, manager, L) = MakeStage();
            spawner.Respawn(L.RampMiddle + Vector3.up * (0.03f * k_Mouse));
            yield return Wait(2.5f);
            float on = Tilt(spawner);

            spawner.Respawn(L.RampMiddle + Vector3.up * (0.03f * k_Mouse));
            foreach (var p in spawner.rig.GetComponentsInChildren<SurfacePlacement>()) p.enabled = false;
            yield return Wait(2.5f);
            float off = Tilt(spawner);

            Debug.Log($"[Episode1] feet on the ramp: {on:0.0} deg off flat with placement, {off:0.0} deg without");
            Assert.Less(on, 5f, "with placement the feet should lie flat on the 15 degree ramp");
            Assert.AreEqual(0, manager.Falls, "standing on the ramp");
        }

        static float Tilt(PuppetSpawner spawner)
        {
            float worst = 0f;
            foreach (var node in spawner.rig.nodes)
            {
                if (node.id != PuppetNodeId.LeftFoot && node.id != PuppetNodeId.RightFoot) continue;
                // Angle to the ramp's normal, measured directly so it works with placement switched off too.
                Vector3 normal = Quaternion.Euler(-Episode1Builder.Layout.RampAngle, 0f, 0f) * Vector3.up;
                worst = Mathf.Max(worst, Vector3.Angle(node.transform.up, normal));
            }
            return worst;
        }

        [UnityTest]
        public IEnumerator FollowViewStaysBehindAndTurnsWithThePuppet()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);

            var originGo = new GameObject("XR Origin");
            originGo.SetActive(false);
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(offset.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            originGo.SetActive(true);

            var go = new GameObject("Puppeteer");
            go.SetActive(false);
            var input = go.AddComponent<ScriptedControlInput>();
            input.supportsTravel = true;
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            go.AddComponent<SizeChanger>();
            go.AddComponent<ViewScaler>().origin = origin;
            go.AddComponent<PuppetTravel>().origin = origin;
            var follow = go.AddComponent<PuppetFollowView>();
            follow.origin = origin;
            go.SetActive(true);
            yield return Wait(1.5f);

            AssertBehindAndAbove(spawner, cam, follow, "at start");

            // Rotate the right hand 40 degrees for 2 s: it turns on the spot, 30 degrees a turn, a second between turns: two turns.
            input.rightYaw = 40f;
            yield return Wait(2f);
            input.rightYaw = 0f;
            yield return Wait(1.5f); // view smoothing settles
            float heading = follow.Heading;
            yield return Wait(2f);
            Debug.Log($"[Episode1] follow view turned to {follow.Heading:0} deg, puppet faces {PuppetYaw(spawner):0} deg");
            Assert.AreEqual(60f, heading, 15f, "view comes round with the puppet's turn steps");
            Assert.AreEqual(heading, follow.Heading, 2f, "turning stops when the hand comes back");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(PuppetYaw(spawner), follow.Heading)), 35f, "puppet turned with the view");
            AssertBehindAndAbove(spawner, cam, follow, "after turning");
        }

        static void AssertBehindAndAbove(PuppetSpawner spawner, Camera cam, PuppetFollowView follow, string when)
        {
            Vector3 center = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Vector3 forward = Quaternion.Euler(0f, follow.Heading, 0f) * Vector3.forward;
            Vector3 toCam = cam.transform.position - center;
            Assert.Less(Vector3.Dot(toCam, forward), 0f, $"camera behind the puppet {when}");
            Assert.Greater(cam.transform.position.y, center.y + spawner.rig.height * 0.5f, $"camera above the puppet {when}");
        }

        static float PuppetYaw(PuppetSpawner spawner)
        {
            Vector3 f = spawner.rig.Node(PuppetNodeId.Center).transform.forward;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}

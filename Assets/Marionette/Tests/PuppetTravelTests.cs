using System.Collections;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    public class PuppetTravelTests
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

        static XROrigin MakeOrigin(Vector3 at)
        {
            var root = new GameObject("XR Origin");
            root.SetActive(false);
            root.transform.position = at;
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(root.transform, false);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(offset.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var origin = root.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            root.SetActive(true);
            return origin;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator ReachingPastTheEdgeCarriesPlayerAndPuppetAlong()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            var origin = MakeOrigin(new Vector3(0.3f, 0f, -1f));

            var go = new GameObject("Puppeteer");
            go.SetActive(false);
            var input = go.AddComponent<ScriptedControlInput>();
            input.supportsTravel = true;
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            go.AddComponent<SizeChanger>();
            go.AddComponent<ViewScaler>().origin = origin;
            var travel = go.AddComponent<PuppetTravel>();
            travel.origin = origin;
            go.SetActive(true);
            yield return Wait(1.5f);
            // This test is about travel carrying player and puppet. Being dragged along by the strings
            // at full travel speed would (rightly) count as losing balance, so balance is off here.
            spawner.rig.GetComponent<BalanceMonitor>().enabled = false;

            float h = spawner.rig.height;
            Vector3 startPuppet = spawner.rig.Node(PuppetNodeId.Center).Body.position;
            Vector3 startGround = spawner.rig.GroundPoint;
            Vector3 startOffset = Flat(origin.transform.position - startGround);

            // Inside the reach radius: puppeteering only, no travel.
            input.leftTranslation = input.rightTranslation = Vector3.forward * (0.2f * h);
            yield return Wait(1f);
            Assert.AreEqual(Vector3.zero, travel.Velocity, "no travel inside the reach radius");

            // Reach past the edge for 4 s: everything glides forward.
            Vector3 originBefore = origin.transform.position;
            input.leftTranslation = input.rightTranslation = Vector3.forward * (0.75f * h);
            yield return Wait(4f);
            Assert.Greater(travel.Velocity.z, 0f, "travelling forward while reaching out");

            // Pull back in and let it settle.
            input.leftTranslation = input.rightTranslation = Vector3.zero;
            yield return Wait(2f);
            Assert.AreEqual(Vector3.zero, travel.Velocity, "stops when the hands come back");

            var rig = spawner.rig;
            Vector3 puppet = rig.Node(PuppetNodeId.Center).Body.position;
            float puppetMoved = (puppet - startPuppet).z / h;
            float originMoved = (origin.transform.position - originBefore).z / h;
            Debug.Log($"[Travel] puppet moved {puppetMoved:0.0} heights, player moved {originMoved:0.0} heights");
            Assert.Greater(puppetMoved, 2.5f, "puppet should travel several of its own heights");
            Assert.AreEqual(originMoved, puppetMoved, 1f, "player keeps up with the puppet");
            Assert.Greater(rig.Node(PuppetNodeId.Head).Body.position.y, puppet.y, "puppet still upright after travelling");

            // Respawn back at the start: player returns to the same offset from the puppet.
            spawner.Respawn(startGround);
            yield return Wait(0.5f);
            Vector3 offset = Flat(origin.transform.position - spawner.rig.GroundPoint);
            Assert.AreEqual(startOffset.x, offset.x, 0.3f * h, "respawn offset x");
            Assert.AreEqual(startOffset.z, offset.z, 0.3f * h, "respawn offset z");
        }
    }
}

using System.Collections;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    public class ViewScalerTests
    {
        const float k_Mouse = 0.12f;
        const float k_Apparent = 0.8f;

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
            cam.nearClipPlane = 0.01f;
            var origin = root.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            root.SetActive(true);
            return origin;
        }

        static (PuppetSpawner, ScriptedControlInput, ViewScaler) MakePuppeteer(XROrigin origin)
        {
            var go = new GameObject("Puppeteer");
            go.SetActive(false);
            var input = go.AddComponent<ScriptedControlInput>();
            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = input;
            spawner.startHeight = k_Mouse;
            var sizes = go.AddComponent<SizeChanger>();
            sizes.sizes = new[] { k_Mouse, MarionetteBuilder.ReferenceHeight };
            var view = go.AddComponent<ViewScaler>();
            view.origin = origin;
            view.apparentHeight = k_Apparent;
            go.SetActive(true);
            return (spawner, input, view);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator PlayerScalesWithPuppetAndKeepsTheSameView()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            var origin = MakeOrigin(new Vector3(0.3f, 0f, -1f));
            var (spawner, input, view) = MakePuppeteer(origin);
            yield return Wait(1f);

            // Mouse: player shrunk so the puppet looks apparentHeight tall.
            Assert.AreEqual(k_Mouse / k_Apparent, view.Scale, 1e-3f, "player scale at mouse size");
            Vector3 ground = spawner.rig.GroundPoint;
            float mouseDistance = Flat(origin.transform.position - ground).magnitude / spawner.rig.height;
            float mouseEye = (origin.Camera.transform.position.y - ground.y) / spawner.rig.height;
            Assert.AreEqual(0.01f * view.Scale, origin.Camera.nearClipPlane, 1e-5f, "near clip follows scale");

            // Grow: the view grows with the puppet. Distance and eye height in puppet heights stay put.
            input.sizeGesture = SizeGesture.Grow;
            yield return Wait(7.5f);
            input.sizeGesture = SizeGesture.None;
            yield return Wait(2.5f);
            Assert.AreEqual(MarionetteBuilder.ReferenceHeight / k_Apparent, view.Scale, 1e-3f, "player scale at gnome size");
            ground = spawner.rig.GroundPoint;
            float gnomeDistance = Flat(origin.transform.position - ground).magnitude / spawner.rig.height;
            float gnomeEye = (origin.Camera.transform.position.y - ground.y) / spawner.rig.height;
            Debug.Log($"[ViewScaler] distance {mouseDistance:0.00} -> {gnomeDistance:0.00} heights, eye {mouseEye:0.00} -> {gnomeEye:0.00} heights");
            Assert.AreEqual(mouseDistance, gnomeDistance, mouseDistance * 0.25f, "view distance in puppet heights (the puppet shifts a little while it grows)");
            Assert.AreEqual(mouseEye, gnomeEye, mouseEye * 0.15f, "eye height in puppet heights");

            // Respawn 2 m away: the view comes along.
            Vector3 before = origin.transform.position;
            spawner.Respawn(ground + new Vector3(2f, 0f, 0f));
            yield return Wait(0.5f);
            Assert.AreEqual(2f, origin.transform.position.x - before.x, 0.05f, "view follows respawn");
        }
    }
}

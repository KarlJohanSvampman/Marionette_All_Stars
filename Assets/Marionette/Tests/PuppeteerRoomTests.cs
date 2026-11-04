using System.Collections;
using Marionette.Course;
using Marionette.Room;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Marionette.Tests
{
    /// <summary>
    /// The puppeteer's room: chair menu, teleport, taking control (view jumps behind the puppet),
    /// contact breaking the link, the globe and beaming to a stage and back.
    /// </summary>
    public class PuppeteerRoomTests
    {
        const float k_Gnome = MarionetteBuilder.ReferenceHeight;

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

        class Setup
        {
            public PuppeteerRoom room;
            public PuppetSpawner spawner;
            public StringFlick flick;
            public XROrigin origin;
            public BeamDestination stage;
            public ArmrestPanel panel;
            public HoloGlobe globe;
            public Beamer beamer;
            public Camera cam;
        }

        static Setup Build()
        {
            var originGo = new GameObject("XR Origin");
            originGo.SetActive(false);
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
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
            spawner.startHeight = k_Gnome;
            var sizes = go.AddComponent<SizeChanger>();
            sizes.sizes = new[] { 0.12f, k_Gnome };
            go.AddComponent<ViewScaler>().origin = origin;
            go.AddComponent<PuppetTravel>().origin = origin;
            go.AddComponent<PuppetFollowView>().origin = origin;
            var flick = go.AddComponent<StringFlick>();

            var room = PuppeteerRoomBuilder.Build(origin, spawner, null);
            var stage = Episode1Builder.Build(spawner, k_Gnome, null);
            stage.transform.position = new Vector3(300f, 0f, 0f);
            var dest = PuppeteerRoomBuilder.AddDestination(room, "Episode 1", 35f, -40f, stage.gameObject,
                stage.checkpoints[0].respawnPoint.position, stage, k_Gnome, null);
            spawner.transform.position = PuppeteerRoomBuilder.PlatformTop;
            go.SetActive(true);

            var panel = room.chair.GetComponentInChildren<ArmrestPanel>(true);
            return new Setup
            {
                room = room, spawner = spawner, flick = flick, origin = origin, stage = dest, panel = panel,
                globe = panel.globe, beamer = panel.beamer, cam = cam
            };
        }

        static IEnumerator SitDown(Setup s)
        {
            s.room.chair.OpenMenu();
            s.room.chair.menu.action.Press();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChairMenuEntersAndExits()
        {
            var s = Build();
            yield return Wait(1f);
            Assert.AreEqual(k_Gnome, s.spawner.rig.height, 1e-4f, "the puppet is gnome-sized by default");

            s.room.chair.OpenMenu();
            Assert.IsTrue(s.room.chair.menu.IsOpen, "pinching the chair opens its menu");
            Assert.AreEqual("Enter Chair", s.room.chair.menu.ActionLabel);
            s.room.chair.menu.action.Press();
            yield return null;
            Assert.IsTrue(s.room.Seated, "Enter Chair sits you down");
            Assert.Less(Vector3.Distance(s.cam.transform.position, s.room.chair.eyePoint.position), 0.02f, "head at the seated eye point");

            s.room.chair.OpenMenu();
            Assert.AreEqual("Exit Chair", s.room.chair.menu.ActionLabel, "sitting: the menu offers Exit Chair");
            s.room.chair.menu.cancel.Press();
            Assert.IsFalse(s.room.chair.menu.IsOpen, "Cancel closes the menu");
            Assert.IsTrue(s.room.Seated, "and changes nothing");

            s.room.chair.OpenMenu();
            s.room.chair.menu.action.Press();
            yield return null;
            Assert.IsFalse(s.room.Seated, "Exit Chair stands you up");
            float fromChair = Vector3.Distance(Flat(s.cam.transform.position), Flat(s.room.chair.transform.position));
            Assert.Greater(fromChair, 0.4f, "standing beside the chair, not in it");
        }

        [UnityTest]
        public IEnumerator TeleportOnlyWhileStanding()
        {
            var s = Build();
            yield return Wait(0.5f);
            Vector3 target = new Vector3(-1.3f, 0f, 1.1f);
            s.room.floor.OnPointerRelease(target);
            Vector3 cell = s.room.floor.Snap(target);
            Assert.Less(Vector3.Distance(Flat(s.cam.transform.position), Flat(cell)), 0.01f, "teleported onto the grid cell");

            yield return SitDown(s);
            Vector3 seated = s.cam.transform.position;
            s.room.floor.OnPointerRelease(new Vector3(1.5f, 0f, -1.5f));
            Assert.Less(Vector3.Distance(seated, s.cam.transform.position), 1e-3f, "no teleporting from the chair");
        }

        [UnityTest]
        public IEnumerator TakingControlJumpsTheViewBehindThePuppet()
        {
            var s = Build();
            yield return Wait(1f);
            var rig = s.spawner.rig;
            Assert.IsNull(rig.leftCross.input, "nobody on the strings until you take control");

            s.panel.TakeControl();
            Assert.IsFalse(s.room.link.IsConnected, "Take Control needs you in the chair");

            yield return SitDown(s);
            s.panel.TakeControl();
            yield return Wait(0.5f);
            Assert.IsTrue(s.room.link.IsConnected, "connected from the chair");
            Assert.IsNotNull(rig.leftCross.input, "your hands work the strings");
            Assert.IsFalse(s.room.pointer.enabled, "the room's pointer is off while connected");
            Assert.AreEqual(k_Gnome / 0.8f, s.origin.transform.localScale.x, 0.02f, "the view is scaled to the puppet");
            Vector3 toCam = s.cam.transform.position - rig.CenterOfMass;
            Debug.Log($"[Room] connected: camera {toCam.magnitude:0.00} m from the puppet, {Vector3.Dot(toCam, rig.Node(PuppetNodeId.Center).transform.forward):0.00} m in front");
            Assert.Less(toCam.magnitude, 4f * k_Gnome, "the view is right by the puppet");
            Assert.Less(Vector3.Dot(toCam, rig.leftCross.neutral.forward), 0f, "behind it");

            s.room.link.Disconnect("test");
            yield return null;
            Assert.AreEqual(1f, s.origin.transform.localScale.x, 1e-4f, "back to your own size");
            Assert.Less(Vector3.Distance(s.cam.transform.position, s.room.chair.eyePoint.position), 0.02f, "back in the chair");
            Assert.IsNull(rig.leftCross.input, "the strings hold where they are");
            Assert.IsTrue(s.room.pointer.enabled);
        }

        [UnityTest]
        public IEnumerator TouchingThePuppeteerBreaksTheLink()
        {
            var s = Build();
            yield return Wait(1f);
            yield return SitDown(s);
            var avatar = s.room.GetComponentInChildren<PuppeteerAvatar>();
            Vector3 head = s.room.chair.eyePoint.position;

            // Something already touching the head when connecting (the finger on the button) doesn't count.
            var already = GameObject.CreatePrimitive(PrimitiveType.Cube);
            already.transform.position = head + Vector3.left * 0.1f;
            already.transform.localScale = Vector3.one * 0.1f;
            s.panel.TakeControl();
            yield return Wait(1.2f);
            Assert.IsTrue(s.room.link.IsConnected, "a touch that was already there at connect is ignored");

            // Something new bumping into the head breaks the connection.
            var bump = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bump.transform.position = avatar.head.transform.position + Vector3.right * 0.05f;
            bump.transform.localScale = Vector3.one * 0.1f;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Debug.Log($"[Room] after the bump: connected {s.room.link.IsConnected}, reason '{s.room.link.LastReason}'");
            Assert.IsFalse(s.room.link.IsConnected, "a bump breaks the link");
            StringAssert.Contains("interrupted", s.room.link.LastReason);

            Object.Destroy(bump);
            yield return Wait(1f);
            Assert.IsFalse(s.room.link.IsConnected, "and it stays broken until Take Control is pressed again");
            Object.Destroy(already);
            yield return null;
            s.panel.TakeControl();
            yield return null;
            Assert.IsTrue(s.room.link.IsConnected, "pressing Take Control again reconnects");
        }

        [UnityTest]
        public IEnumerator TheGlobeBeamsThePuppetToAStageAndHome()
        {
            var s = Build();
            yield return Wait(1.5f);
            yield return SitDown(s);
            Assert.IsFalse(s.globe.gameObject.activeSelf, "the globe is hidden at first");
            s.panel.ToggleGlobe();
            Assert.IsTrue(s.globe.gameObject.activeSelf, "the Globe button shows it");
            Assert.AreEqual(2, s.globe.Pins.Count, "a pin for home and one for the stage");

            s.panel.Beam();
            Assert.AreEqual(s.beamer.home, s.beamer.Current, "nothing picked: no beam");

            GlobePin stagePin = null;
            foreach (var p in s.globe.Pins) if (p.destination == s.stage) stagePin = p;
            stagePin.OnPointerPress(stagePin.transform.position);
            Assert.AreEqual(s.stage, s.globe.Selected, "pinching a pin picks it");
            Assert.IsTrue(s.beamer.PuppetOnPlatform(), "the puppet starts on the platform");
            s.panel.Beam();
            yield return Wait(2.5f);
            Vector3 c = s.spawner.rig.CenterOfMass, target = s.stage.spawnPoint.position;
            Debug.Log($"[Room] beamed: puppet {Vector3.Distance(Flat(c), Flat(target)):0.00} m from the stage's start, status '{s.beamer.Status}'");
            Assert.AreEqual(s.stage, s.beamer.Current, "the puppet is at the stage");
            Assert.Less(Vector3.Distance(Flat(c), Flat(target)), 0.5f, "standing on the stage's start pad");
            Assert.IsTrue(s.stage.stage.activeSelf, "the stage is switched on");
            Assert.IsTrue(s.stage.course.isActiveAndEnabled, "and its course runs");

            s.globe.Select(s.beamer.home);
            s.panel.Beam();
            yield return Wait(2.5f);
            Assert.AreEqual(s.beamer.home, s.beamer.Current, "home again");
            Assert.IsTrue(s.beamer.PuppetOnPlatform(), "back on the platform");
            Assert.IsFalse(s.stage.stage.activeSelf, "the stage is switched off when the puppet leaves");
        }

        [UnityTest]
        public IEnumerator ThePuppetIgnoresFlicksWhileNobodyIsConnected()
        {
            var s = Build();
            yield return Wait(1f);
            var take = new XRHandsControlInput.FlickTake
            {
                target = PuppetNodeId.RightFoot,
                times = new[] { 0f, 0.1f, 0.2f, 0.3f },
                path = new[] { Vector3.zero, Vector3.forward * 0.05f, Vector3.forward * 0.1f, Vector3.forward * 0.15f }
            };
            s.flick.Play(take);
            Assert.IsFalse(s.flick.Playing, "not connected: a flick does nothing");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}

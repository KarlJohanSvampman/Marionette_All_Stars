using Marionette.Course;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marionette.Room
{
    /// <summary>
    /// The puppeteer's body is a stand-in figure in a dark robe, built from primitives; a rigged humanoid
    /// model can replace the Standing / Sitting objects under PuppeteerBody later.
    ///
    /// Builds the puppeteer's room around the world origin: a 7 x 7 m room with a teleport grid floor,
    /// the chair in the middle facing the puppet's platform in a corner, the armrest panel, the hologram
    /// globe, the puppeteer's body, and the menus. Used by the editor menu and by the tests.
    /// </summary>
    public static class PuppeteerRoomBuilder
    {
        [System.Serializable]
        public class Materials
        {
            public Material floor, wall, wood, darkWood, metal, cloth, robe, panel, button, holo, pin, beam, platform;
        }

        public const float RoomSize = 7f, WallHeight = 3f;
        public static readonly Vector3 PlatformCentre = new Vector3(2.4f, 0f, 2.4f);
        public const float PlatformHeight = 0.35f, PlatformRadius = 0.55f;

        public static Vector3 PlatformTop => PlatformCentre + Vector3.up * PlatformHeight;

        public static PuppeteerRoom Build(XROrigin origin, PuppetSpawner spawner, Materials m)
        {
            m ??= new Materials();
            m.floor ??= Lit(new Color(0.35f, 0.37f, 0.4f));
            m.wall ??= Lit(new Color(0.62f, 0.58f, 0.52f));
            m.wood ??= Lit(new Color(0.5f, 0.33f, 0.2f));
            m.darkWood ??= Lit(new Color(0.28f, 0.18f, 0.11f));
            m.metal ??= Lit(new Color(0.6f, 0.6f, 0.65f));
            m.cloth ??= Lit(new Color(0.25f, 0.2f, 0.32f));
            m.robe ??= Lit(new Color(0.07f, 0.06f, 0.08f));
            m.panel ??= Lit(new Color(0.12f, 0.13f, 0.16f));
            m.button ??= Lit(new Color(0.2f, 0.25f, 0.32f));
            m.platform ??= Lit(new Color(0.3f, 0.32f, 0.38f));
            m.holo ??= Holo(new Color(0.4f, 0.85f, 1f, 0.6f));
            m.pin ??= Lit(new Color(1f, 0.45f, 0.3f));
            m.beam ??= Holo(new Color(0.5f, 0.9f, 1f, 0.5f));

            var root = new GameObject("PuppeteerRoom").transform;
            var room = root.gameObject.AddComponent<PuppeteerRoom>();
            room.origin = origin;

            // ---- Floor (teleport grid), walls, light ----
            var floorGo = Box(root, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(RoomSize, 0.1f, RoomSize), m.floor);
            floorGo.AddComponent<RoomNoInterrupt>();
            room.floor = floorGo.AddComponent<TeleportFloor>();
            room.floor.room = room;
            float half = RoomSize * 0.5f;
            Box(root, "WallNorth", new Vector3(0f, 0f, half + 0.05f), new Vector3(RoomSize + 0.2f, WallHeight, 0.1f), m.wall);
            Box(root, "WallSouth", new Vector3(0f, 0f, -half - 0.05f), new Vector3(RoomSize + 0.2f, WallHeight, 0.1f), m.wall);
            Box(root, "WallEast", new Vector3(half + 0.05f, 0f, 0f), new Vector3(0.1f, WallHeight, RoomSize), m.wall);
            Box(root, "WallWest", new Vector3(-half - 0.05f, 0f, 0f), new Vector3(0.1f, WallHeight, RoomSize), m.wall);
            Box(root, "Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(RoomSize, 0.1f, RoomSize), m.wall);
            var lamp = new GameObject("CeilingLight").AddComponent<Light>();
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(0f, WallHeight - 0.3f, 0f);
            lamp.type = LightType.Point;
            lamp.range = 9f;
            lamp.intensity = 6f;
            lamp.color = new Color(1f, 0.93f, 0.82f);
            lamp.shadows = LightShadows.Soft;

            // ---- The puppet's platform, in a corner ----
            var platform = Cylinder(root, "PuppetPlatform", PlatformCentre, PlatformRadius, PlatformHeight, m.platform);
            var ring = Cylinder(platform.transform.parent, "PlatformGlow", PlatformCentre + Vector3.up * PlatformHeight, PlatformRadius * 0.9f, 0.004f, m.holo);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            var homeGo = new GameObject("Home_Platform");
            homeGo.transform.SetParent(root, false);
            homeGo.transform.position = PlatformTop;
            var home = homeGo.AddComponent<BeamDestination>();
            home.displayName = "The workshop";
            home.latitude = 59f; home.longitude = 18f;
            home.spawnPoint = homeGo.transform;
            home.isHome = true;
            var beamer = homeGo.AddComponent<Beamer>();
            beamer.spawner = spawner;
            beamer.home = home;
            beamer.platformRadius = PlatformRadius;
            beamer.beamMaterial = m.beam;
            spawner.transform.position = PlatformTop;

            // ---- The chair, in the middle, facing the platform ----
            var chairRoot = new GameObject("PuppeteerChair").transform;
            chairRoot.SetParent(root, false);
            chairRoot.rotation = Quaternion.LookRotation(new Vector3(PlatformCentre.x, 0f, PlatformCentre.z).normalized, Vector3.up);
            chairRoot.gameObject.AddComponent<RoomNoInterrupt>();
            var chair = chairRoot.gameObject.AddComponent<RoomChair>();
            chair.room = room;
            room.chair = chair;
            Box(chairRoot, "Seat", new Vector3(0f, 0.42f, 0f), new Vector3(0.52f, 0.08f, 0.5f), m.cloth);
            Box(chairRoot, "Back", new Vector3(0f, 0.5f, -0.27f), new Vector3(0.52f, 0.7f, 0.07f), m.cloth);
            Box(chairRoot, "ArmLeft", new Vector3(-0.3f, 0.62f, 0f), new Vector3(0.08f, 0.05f, 0.5f), m.wood);
            Box(chairRoot, "ArmRight", new Vector3(0.3f, 0.62f, 0f), new Vector3(0.08f, 0.05f, 0.5f), m.wood);
            foreach (var (x, z) in new[] { (-0.22f, -0.22f), (0.22f, -0.22f), (-0.22f, 0.22f), (0.22f, 0.22f) })
                Box(chairRoot, "Leg", new Vector3(x, 0f, z), new Vector3(0.05f, 0.42f, 0.05f), m.darkWood);
            foreach (var x in new[] { -0.3f, 0.3f })
                Box(chairRoot, "ArmPost", new Vector3(x, 0.42f, 0.2f), new Vector3(0.04f, 0.2f, 0.04f), m.darkWood);
            chair.eyePoint = Point(chairRoot, "SeatedEye", new Vector3(0f, 1.15f, -0.05f));
            chair.standPoint = Point(chairRoot, "StandHere", new Vector3(0.75f, 0f, 0.3f));

            // ---- Armrest panel: Take Control, Globe, Beam ----
            var panelRoot = new GameObject("ArmrestPanel").transform;
            panelRoot.SetParent(chairRoot, false);
            panelRoot.localPosition = new Vector3(0.33f, 0.69f, 0.16f);
            Vector3 eye = chair.eyePoint.position;
            panelRoot.rotation = Quaternion.LookRotation(panelRoot.position - eye, chairRoot.up); // +Z away from the viewer
            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "PanelBacking"; // physical: touching it while connected breaks the link
            backing.transform.SetParent(panelRoot, false);
            backing.transform.localPosition = new Vector3(0f, -0.005f, 0.012f);
            backing.transform.localScale = new Vector3(0.27f, 0.12f, 0.012f);
            backing.GetComponent<Renderer>().sharedMaterial = m.panel;
            var panel = panelRoot.gameObject.AddComponent<ArmrestPanel>();
            panel.room = room;
            panel.beamer = beamer;
            var size = new Vector2(0.075f, 0.04f);
            panel.takeControl = RoomButton.Create(panelRoot, "TakeControl", "Take\nControl", new Vector3(-0.085f, 0.02f, 0f), size, m.button, null);
            panel.globeButton = RoomButton.Create(panelRoot, "Globe", "Globe", new Vector3(0f, 0.02f, 0f), size, m.button, null);
            panel.beam = RoomButton.Create(panelRoot, "Beam", "Beam", new Vector3(0.085f, 0.02f, 0f), size, m.button, null);
            panel.status = RoomText.Create(panelRoot, "Status", "", new Vector3(0f, -0.035f, -0.002f), 0.012f, new Color(0.6f, 0.9f, 1f));
            panel.Wire();

            // ---- Hologram globe, in front of the chair (hidden until the Globe button) ----
            var globeRoot = new GameObject("HoloGlobe").transform;
            globeRoot.SetParent(chairRoot, false);
            globeRoot.localPosition = new Vector3(0f, 1.0f, 0.55f);
            globeRoot.gameObject.AddComponent<RoomNoInterrupt>(); // a hologram: nothing physical to bump
            var spinner = new GameObject("Spinner").transform;
            spinner.SetParent(globeRoot, false);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Earth";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(spinner, false);
            sphere.GetComponent<Renderer>().sharedMaterial = m.holo;
            sphere.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var globe = globeRoot.gameObject.AddComponent<HoloGlobe>();
            globe.spinner = spinner;
            sphere.transform.localScale = Vector3.one * (globe.radius * 2f);
            var labelRoot = new GameObject("LabelFacing").transform;
            labelRoot.SetParent(globeRoot, false);
            labelRoot.localPosition = new Vector3(0f, globe.radius + 0.05f, 0f);
            globe.label = RoomText.Create(labelRoot, "Destination", "Pick a place", Vector3.zero, 0.025f, new Color(0.6f, 0.95f, 1f));
            panel.globe = globe;
            AddDestinationPin(globe, home, m);
            globeRoot.gameObject.SetActive(false);

            // ---- Enter / Exit chair menu ----
            var menuGo = new GameObject("ChairMenu");
            menuGo.transform.SetParent(root, false);
            var menu = menuGo.AddComponent<ChoiceMenu>();
            menu.action = RoomButton.Create(menuGo.transform, "MenuAction", "Enter Chair", new Vector3(-0.07f, 0f, 0f), new Vector2(0.12f, 0.05f), m.button, null);
            menu.cancel = RoomButton.Create(menuGo.transform, "MenuCancel", "Cancel", new Vector3(0.07f, 0f, 0f), new Vector2(0.12f, 0.05f), m.button, null);
            menu.Wire();
            menuGo.SetActive(false);
            chair.menu = menu;

            // ---- The puppeteer's hands and pointer, on the XR rig ----
            var hands = GetOrAdd<RoomHands>(origin.gameObject);
            hands.origin = origin;
            room.pointer = GetOrAdd<RoomPointer>(origin.gameObject);

            // ---- The connection to the puppet ----
            var link = GetOrAdd<PuppetLink>(spawner.gameObject);
            link.room = room;
            link.origin = origin;
            link.hands = hands;
            room.link = link;

            // ---- The puppeteer's body ----
            BuildAvatar(root, room, hands, m);

            // Start standing a little behind the chair.
            origin.transform.SetPositionAndRotation(chairRoot.TransformPoint(new Vector3(0f, 0f, -1.2f)), chairRoot.rotation);
            return room;
        }

        /// <summary>Add a place to beam to: a stage (switched off until the puppet arrives) and its pin on the globe.</summary>
        public static BeamDestination AddDestination(PuppeteerRoom room, string name, float lat, float lon, GameObject stage, Vector3 spawn, CourseManager course, float puppetHeight, Materials m)
        {
            var go = new GameObject("Destination_" + name);
            go.transform.SetParent(stage.transform, false);
            go.transform.position = spawn;
            var d = go.AddComponent<BeamDestination>();
            d.displayName = name;
            d.latitude = lat; d.longitude = lon;
            d.spawnPoint = go.transform;
            d.course = course;
            d.puppetHeight = puppetHeight;
            d.stage = stage;
            if (course != null) course.enabled = false;
            var globe = room.chair.GetComponentInChildren<HoloGlobe>(true);
            AddDestinationPin(globe, d, m);
            stage.SetActive(false);
            return d;
        }

        static void AddDestinationPin(HoloGlobe globe, BeamDestination d, Materials m)
        {
            if (globe != null) globe.AddPin(d, m != null ? m.pin : null);
        }

        static void BuildAvatar(Transform root, PuppeteerRoom room, RoomHands hands, Materials m)
        {
            var body = new GameObject("PuppeteerBody").transform;
            body.SetParent(root, false);
            var avatar = body.gameObject.AddComponent<PuppeteerAvatar>();
            avatar.room = room;
            avatar.hands = hands;

            var standing = new GameObject("Standing").transform;
            standing.SetParent(body, false);
            Capsule(standing, "Torso", new Vector3(0f, 1.15f, -0.05f), new Vector3(0.36f, 0.32f, 0.24f), m.robe);
            Capsule(standing, "LegLeft", new Vector3(-0.1f, 0.45f, -0.05f), new Vector3(0.13f, 0.45f, 0.13f), m.robe);
            Capsule(standing, "LegRight", new Vector3(0.1f, 0.45f, -0.05f), new Vector3(0.13f, 0.45f, 0.13f), m.robe);
            avatar.standing = standing;

            var sitting = new GameObject("Sitting").transform;
            sitting.SetParent(body, false);
            Capsule(sitting, "Torso", new Vector3(0f, 0.78f, -0.08f), new Vector3(0.36f, 0.3f, 0.24f), m.robe);
            var thighL = Capsule(sitting, "ThighLeft", new Vector3(-0.1f, 0.52f, 0.12f), new Vector3(0.13f, 0.22f, 0.13f), m.robe);
            var thighR = Capsule(sitting, "ThighRight", new Vector3(0.1f, 0.52f, 0.12f), new Vector3(0.13f, 0.22f, 0.13f), m.robe);
            thighL.transform.localRotation = thighR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Capsule(sitting, "ShinLeft", new Vector3(-0.1f, 0.25f, 0.32f), new Vector3(0.12f, 0.22f, 0.12f), m.robe);
            Capsule(sitting, "ShinRight", new Vector3(0.1f, 0.25f, 0.32f), new Vector3(0.12f, 0.22f, 0.12f), m.robe);
            avatar.sitting = sitting;
            foreach (var c in body.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Head (seen only as a shadow by its owner) and hand sensors.
            avatar.head = Sensor(body, "HeadSensor", 0.12f, m.robe, shadowOnly: true);
            avatar.leftHand = Sensor(body, "LeftHandSensor", 0.06f, null, false);
            avatar.rightHand = Sensor(body, "RightHandSensor", 0.06f, null, false);
        }

        static ContactSensor Sensor(Transform parent, string name, float radius, Material mat, bool shadowOnly)
        {
            GameObject go;
            if (mat != null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = mat;
                if (shadowOnly) go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            else go = new GameObject();
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * (radius * 2f);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f;
            col.isTrigger = true;
            col.enabled = false;
            return go.AddComponent<ContactSensor>();
        }

        // ---------- helpers ----------

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static GameObject Box(Transform parent, string name, Vector3 bottom, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = bottom + Vector3.up * (size.y * 0.5f);
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Cylinder(Transform parent, string name, Vector3 bottom, float radius, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = bottom + Vector3.up * (height * 0.5f);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            // A box collider sits flat and steady under the puppet's feet (the cylinder primitive's collider is a capsule).
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.7f, 2f, 0.7f);
            return go;
        }

        static GameObject Capsule(Transform parent, string name, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = new Vector3(size.x, size.y, size.z);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Transform Point(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        public static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>Glowing see-through material (URP Unlit, transparent, additive) for holograms and beams.</summary>
        public static Material Holo(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            MakeHolo(m, c);
            return m;
        }

        public static void MakeHolo(Material m, Color c)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}

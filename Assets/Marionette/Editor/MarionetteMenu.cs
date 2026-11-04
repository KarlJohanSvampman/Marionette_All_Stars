using System.IO;
using Marionette.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marionette.Editor
{
    public static class MarionetteMenu
    {
        const string k_Root = "Assets/Marionette";
        const string k_MaterialDir = k_Root + "/Materials";
        const string k_SceneDir = k_Root + "/Scenes";

        /// <summary>Default mouse-sized puppet height. See Docs/02_ScaleStudy.md for how it was chosen.</summary>
        const float k_MouseHeight = 0.12f;

        const string k_Keys = "Keys: WASD/QE move+yaw, IJKL/UO legs cross, Z X = left hand / right hand, " +
                              "B N = left foot / right foot, R = reset, hold G = grow, hold H = shrink.";

        [MenuItem("Marionette/Create Test Scene (gnome size)")]
        static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            var grid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/BasicScene/Grid.mat");
            if (grid != null) floor.GetComponent<Renderer>().sharedMaterial = grid;

            HandsXRRig.Create(new Vector3(0f, 0f, -1.2f), Quaternion.identity);
            var spawner = CreatePuppeteer(Vector3.zero, MarionetteBuilder.ReferenceHeight);
            Save(scene, "MarionetteTest.unity", spawner.rig.gameObject);
        }

        public const string CourseScenePath = k_SceneDir + "/ObstacleCourse_S1.unity";

        [MenuItem("Marionette/Create Obstacle Course - Section 1 (mouse size)")]
        public static void CreateCourseScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var origin = HandsXRRig.Create(Vector3.zero, Quaternion.identity);
            var spawner = CreatePuppeteer(Vector3.zero, k_MouseHeight);
            var course = CourseBuilder.BuildSection1(spawner, LoadOrCreateCourseMaterials());
            // Player stands on the floor in front of the desk, a little behind and right of the start pad.
            origin.transform.position = new Vector3(spawner.transform.position.x + 0.15f, 0f, spawner.transform.position.z - 0.55f);
            // Rebuild the puppet on the start pad now the course has placed the spawner there.
            Object.DestroyImmediate(spawner.rig.gameObject);
            spawner.rig = BuildRig(spawner.transform.position, k_MouseHeight);
            Save(scene, "ObstacleCourse_S1.unity", course.gameObject);
        }

        public const string Episode1ScenePath = k_SceneDir + "/Episode1_Balance.unity";

        [MenuItem("Marionette/Create Episode 1 Stage - Walkway, Ramp, Stairs (mouse size)")]
        public static void CreateEpisode1Scene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var origin = HandsXRRig.Create(Vector3.zero, Quaternion.identity);
            var spawner = CreatePuppeteer(Vector3.zero, k_MouseHeight);
            var stage = Episode1Builder.Build(spawner, k_MouseHeight, LoadOrCreateEpisode1Materials());
            // Player behind the start pad; the follow view takes over in Play mode.
            origin.transform.position = spawner.transform.position + new Vector3(0f, 0f, -0.6f);
            Object.DestroyImmediate(spawner.rig.gameObject);
            spawner.rig = BuildRig(spawner.transform.position, k_MouseHeight);
            Save(scene, "Episode1_Balance.unity", stage.gameObject);
        }

        [MenuItem("Marionette/Hand Tracking/Controls - Pinch and Flick")]
        static void UsePinchFlick() => SetFingerAction(XRHandsControlInput.FingerAction.PinchFlick);

        [MenuItem("Marionette/Hand Tracking/Controls - Lift Finger")]
        static void UseLift() => SetFingerAction(XRHandsControlInput.FingerAction.Lift);

        static void SetFingerAction(XRHandsControlInput.FingerAction action)
        {
            foreach (var hands in Object.FindObjectsByType<XRHandsControlInput>())
            {
                Undo.RecordObject(hands, "Change hand controls");
                hands.fingerAction = action;
                if (hands.GetComponent<StringFlick>() == null && hands.GetComponent<PuppetSpawner>() != null)
                    Undo.AddComponent<StringFlick>(hands.gameObject);
                EditorSceneManager.MarkSceneDirty(hands.gameObject.scene);
            }
            Debug.Log($"[Marionette] Hand controls: {action}. Save the scene to keep it.");
        }

        [MenuItem("Marionette/Add Puppet To Current Scene")]
        static void AddPuppet()
        {
            Vector3 pos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var spawner = CreatePuppeteer(pos, k_MouseHeight);
            Selection.activeGameObject = spawner.rig.gameObject;
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        }

        /// <summary>
        /// The Puppeteer object holds everything that outlives a single puppet: inputs, spawner,
        /// size changer. The puppet itself is built now so it can be inspected and tuned in the editor.
        /// </summary>
        static PuppetSpawner CreatePuppeteer(Vector3 position, float height)
        {
            var go = new GameObject("Puppeteer");
            go.transform.position = position;
            Undo.RegisterCreatedObjectUndo(go, "Create Puppeteer");

            // Finger tracking whenever a headset is running, keyboard at the desk. Never controllers.
            var keyboard = go.AddComponent<KeyboardControlInput>();
            var hands = go.AddComponent<XRHandsControlInput>();
            var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null && origin.CameraFloorOffsetObject != null)
                hands.trackingSpace = origin.CameraFloorOffsetObject.transform;
            var auto = go.AddComponent<AutoControlInput>();
            auto.hands = hands;
            auto.keyboard = keyboard;

            var spawner = go.AddComponent<PuppetSpawner>();
            spawner.input = auto;
            spawner.startHeight = height;
            spawner.materials = LoadOrCreateMaterials();
            spawner.rig = BuildRig(position, height);

            var sizes = go.AddComponent<SizeChanger>();
            sizes.sizes = new[] { k_MouseHeight, MarionetteBuilder.ReferenceHeight };

            // The player shrinks and grows with the puppet so it always looks the same size in VR.
            if (origin != null) go.AddComponent<ViewScaler>().origin = origin;
            // Reach past arm's length to glide along the course with the puppet.
            go.AddComponent<PuppetTravel>().origin = origin;
            // Third-person view: behind and above the puppet, turning with it.
            if (origin != null) go.AddComponent<PuppetFollowView>().origin = origin;
            // Pinch & flick: applies recorded right-index flicks to the pinched string.
            go.AddComponent<StringFlick>();

            var cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.gameObject.AddComponent<PuppetCamera>();
                follow.spawner = spawner;
                Vector3 target = position + Vector3.up * (height * 0.5f);
                cam.transform.position = target + follow.offset * height;
                cam.transform.LookAt(target);
                cam.nearClipPlane = Mathf.Max(0.002f, height * 0.05f);
            }
            return spawner;
        }

        static MarionetteRig BuildRig(Vector3 position, float height)
        {
            var rig = MarionetteBuilder.Build(position, LoadOrCreateMaterials(), new MarionetteBuilder.Settings { height = height });
            Undo.RegisterCreatedObjectUndo(rig.gameObject, "Create Marionette");
            return rig;
        }

        static void Save(UnityEngine.SceneManagement.Scene scene, string file, GameObject select)
        {
            Directory.CreateDirectory(k_SceneDir);
            string path = $"{k_SceneDir}/{file}";
            EditorSceneManager.SaveScene(scene, path);
            AddToBuildSettingsFirst(path);
            Selection.activeGameObject = select;
            Debug.Log($"[Marionette] Created {file}. Press Play. {k_Keys}");
        }

        /// <summary>Puts the scene first in Build Settings so builds and Quest Link start in it.</summary>
        static void AddToBuildSettingsFirst(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static MarionetteBuilder.Materials LoadOrCreateMaterials() => new MarionetteBuilder.Materials
        {
            wood = Lit("Wood", new Color(0.72f, 0.52f, 0.32f), 0.35f, 0f),
            crossWood = Lit("CrossWood", new Color(0.45f, 0.30f, 0.18f), 0.3f, 0f),
            metal = Lit("Brass", new Color(0.78f, 0.62f, 0.28f), 0.7f, 1f),
            rubber = Lit("SuctionRubber", new Color(0.75f, 0.2f, 0.15f), 0.6f, 0f),
            cloth = Unlit("Textile", new Color(0.25f, 0.35f, 0.55f)),
            line = Unlit("String", Color.white),
            footGrip = FootGrip()
        };

        static CourseBuilder.Materials LoadOrCreateCourseMaterials() => new CourseBuilder.Materials
        {
            desk = Lit("Course/DeskOak", new Color(0.55f, 0.38f, 0.22f), 0.45f, 0f),
            deskLeg = Lit("Course/DeskLeg", new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.8f),
            floor = Lit("Course/OfficeFloor", new Color(0.35f, 0.36f, 0.4f), 0.1f, 0f),
            paper = Lit("Course/Paper", new Color(0.95f, 0.95f, 0.92f), 0.1f, 0f),
            cover = Lit("Course/NotebookCover", new Color(0.15f, 0.3f, 0.55f), 0.3f, 0f),
            ruler = Lit("Course/Ruler", new Color(0.85f, 0.7f, 0.45f), 0.5f, 0f),
            coffee = Lit("Course/Coffee", new Color(0.25f, 0.13f, 0.05f), 0.95f, 0f),
            water = Lit("Course/Water", new Color(0.55f, 0.75f, 0.9f), 0.95f, 0f),
            mug = Lit("Course/Mug", new Color(0.9f, 0.9f, 0.88f), 0.8f, 0f),
            coaster = Lit("Course/Coaster", new Color(0.6f, 0.45f, 0.3f), 0.2f, 0f),
            sticky = Lit("Course/StickyNote", new Color(1f, 0.92f, 0.35f), 0.1f, 0f)
        };

        static Episode1Builder.Materials LoadOrCreateEpisode1Materials() => new Episode1Builder.Materials
        {
            ground = Lit("Episode1/Ground", new Color(0.42f, 0.44f, 0.4f), 0.1f, 0f),
            block = Lit("Episode1/Block", new Color(0.78f, 0.72f, 0.62f), 0.2f, 0f),
            walkway = Lit("Episode1/Walkway", new Color(0.62f, 0.42f, 0.25f), 0.35f, 0f),
            ramp = Lit("Episode1/Ramp", new Color(0.55f, 0.6f, 0.68f), 0.2f, 0f),
            stairs = Lit("Episode1/Stairs", new Color(0.68f, 0.62f, 0.5f), 0.2f, 0f),
            pit = Lit("Episode1/Pit", new Color(0.12f, 0.1f, 0.12f), 0.05f, 0f),
            pad = Lit("Episode1/Pad", new Color(0.3f, 0.6f, 0.4f), 0.2f, 0f)
        };

        static Material Lit(string name, Color color, float smoothness, float metallic)
        {
            string path = $"{k_MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Particle unlit multiplies by vertex colour, which the line renderers use for slack/taut tinting.</summary>
        static Material Unlit(string name, Color color)
        {
            string path = $"{k_MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static PhysicsMaterial FootGrip()
        {
            string path = $"{k_MaterialDir}/FootGrip.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm != null) return pm;
            Directory.CreateDirectory(k_MaterialDir);
            pm = new PhysicsMaterial("FootGrip")
            {
                staticFriction = 0.9f,
                dynamicFriction = 0.7f,
                frictionCombine = PhysicsMaterialCombine.Maximum
            };
            AssetDatabase.CreateAsset(pm, path);
            return pm;
        }
    }

    /// <summary>
    /// First time the project compiles without the course scene, build it and open it, so there is
    /// always a ready-made scene to press Play in. Runs once; delete the scene to have it rebuilt.
    /// </summary>
    [InitializeOnLoad]
    static class CreateCourseSceneOnFirstLoad
    {
        const string k_Done = "Marionette.CourseSceneAutoCreated.v3";

        static CreateCourseSceneOnFirstLoad()
        {
            if (Application.isBatchMode || SessionState.GetBool(k_Done, false)) return;
            EditorApplication.delayCall += TryCreate;
        }

        static void TryCreate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryCreate;
                return;
            }
            SessionState.SetBool(k_Done, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(MarionetteMenu.CourseScenePath))
            {
                Debug.Log($"[Marionette] Creating the obstacle course scene at {MarionetteMenu.CourseScenePath}");
                MarionetteMenu.CreateCourseScene();
            }
            if (!File.Exists(MarionetteMenu.Episode1ScenePath))
            {
                Debug.Log($"[Marionette] Creating the Episode 1 stage at {MarionetteMenu.Episode1ScenePath}");
                MarionetteMenu.CreateEpisode1Scene();
            }
        }
    }
}

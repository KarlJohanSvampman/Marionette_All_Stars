using System.Collections.Generic;
using UnityEngine;

namespace Marionette.Course
{
    /// <summary>
    /// Obstacle course, section 1: Movement and Balance, laid out on an office desk.
    /// Everything is real-world size (a ruler is 30 cm), the puppet is the small thing.
    ///
    ///   0 Start      coaster
    ///   1 Walk+Stop  lane between two rulers, stop and hold still on a sticky note
    ///   2 Turn       turn right around a coffee mug
    ///   3 Step up    onto a 1.5 cm notebook
    ///   4 Beam       3 cm wide ruler bridging two notebooks over a coffee spill
    ///   5 Uneven     sticky-note pads at different heights and tilts over a water spill
    ///   6 Finish     coaster
    /// </summary>
    public static class CourseBuilder
    {
        [System.Serializable]
        public class Materials
        {
            public Material desk, deskLeg, floor, paper, cover, ruler, coffee, water, mug, coaster, sticky;
        }

        public const float DeskTop = 0.75f;
        const float k_ContactOffset = 0.001f;

        public static CourseManager BuildSection1(PuppetSpawner spawner, Materials m)
        {
            m ??= new Materials();
            var root = new GameObject("ObstacleCourse_S1_MovementBalance").transform;
            var checkpoints = new List<Checkpoint>();
            var fails = new List<FailSurface>();

            // ---- Room ----
            var floor = Box(root, "Floor", new Vector3(0f, -DeskTop - 0.01f, 0f), new Vector3(6f, 0.02f, 6f), m.floor);
            fails.Add(floor.AddComponent<FailSurface>());
            Box(root, "DeskTop", new Vector3(0f, -0.03f, 0f), new Vector3(1.6f, 0.03f, 0.8f), m.desk);
            foreach (var (x, z) in new[] { (-0.75f, -0.35f), (0.75f, -0.35f), (-0.75f, 0.35f), (0.75f, 0.35f) })
                Box(root, "DeskLeg", new Vector3(x, -DeskTop, z), new Vector3(0.05f, DeskTop - 0.03f, 0.05f), m.deskLeg);

            // ---- 0 Start ----
            Cylinder(root, "StartCoaster", new Vector3(-0.6f, 0f, -0.32f), 0.10f, 0.005f, m.coaster);
            checkpoints.Add(MakeCheckpoint(root, "Start", new Vector3(-0.6f, 0.005f, -0.32f), new Vector3(0.1f, 0.7f, 0.1f)));

            // ---- 1 Walk lane + stop note ----
            Box(root, "LaneRuler_L", new Vector3(-0.665f, 0f, -0.06f), new Vector3(0.03f, 0.003f, 0.30f), m.ruler);
            Box(root, "LaneRuler_R", new Vector3(-0.535f, 0f, -0.06f), new Vector3(0.03f, 0.003f, 0.30f), m.ruler);
            Box(root, "StopNote", new Vector3(-0.6f, 0f, 0.14f), new Vector3(0.076f, 0.001f, 0.076f), m.sticky);
            var stop = MakeCheckpoint(root, "Walk and stop on the note", new Vector3(-0.6f, 0.001f, 0.14f), new Vector3(0.076f, 0.7f, 0.076f));
            stop.dwellSeconds = 1f;
            checkpoints.Add(stop);

            // ---- 2 Turn around the mug ----
            Cylinder(root, "Mug", new Vector3(-0.6f, 0f, 0.29f), 0.085f, 0.10f, m.mug);
            checkpoints.Add(MakeCheckpoint(root, "Turn at the mug", new Vector3(-0.45f, 0f, 0.27f), new Vector3(0.06f, 0.7f, 0.14f)));

            // ---- 3 Step up onto a notebook ----
            Box(root, "Notebook_A", new Vector3(-0.25f, 0f, 0.27f), new Vector3(0.21f, 0.015f, 0.15f), m.cover);
            checkpoints.Add(MakeCheckpoint(root, "Step up", new Vector3(-0.28f, 0.015f, 0.27f), new Vector3(0.08f, 0.7f, 0.15f)));

            // ---- 4 Beam over coffee ----
            Box(root, "Notebook_B", new Vector3(0.26f, 0f, 0.27f), new Vector3(0.21f, 0.015f, 0.15f), m.cover);
            Box(root, "BeamRuler", new Vector3(0.005f, 0.015f, 0.27f), new Vector3(0.32f, 0.003f, 0.03f), m.ruler);
            var coffee = Box(root, "CoffeeSpill", new Vector3(0.005f, 0f, 0.27f), new Vector3(0.29f, 0.002f, 0.22f), m.coffee);
            fails.Add(coffee.AddComponent<FailSurface>());
            checkpoints.Add(MakeCheckpoint(root, "Cross the beam", new Vector3(0.27f, 0.015f, 0.27f), new Vector3(0.08f, 0.7f, 0.15f)));

            // ---- 5 Uneven sticky-note pads over water ----
            var water = Box(root, "WaterSpill", new Vector3(0.50f, 0f, 0.10f), new Vector3(0.22f, 0.002f, 0.40f), m.water);
            fails.Add(water.AddComponent<FailSurface>());
            Box(root, "StickyPad_1", new Vector3(0.45f, 0f, 0.24f), new Vector3(0.076f, 0.012f, 0.076f), m.sticky, Quaternion.Euler(0f, 10f, 0f));
            Box(root, "StickyPad_2", new Vector3(0.53f, 0f, 0.14f), new Vector3(0.076f, 0.020f, 0.076f), m.sticky, Quaternion.Euler(6f, -8f, 0f));
            Box(root, "StickyPad_3", new Vector3(0.47f, 0f, 0.03f), new Vector3(0.076f, 0.008f, 0.076f), m.sticky, Quaternion.Euler(0f, 20f, -5f));
            Box(root, "StickyPad_4", new Vector3(0.54f, 0f, -0.07f), new Vector3(0.076f, 0.016f, 0.076f), m.sticky, Quaternion.Euler(-4f, 0f, 4f));
            checkpoints.Add(MakeCheckpoint(root, "Cross the uneven pads", new Vector3(0.54f, 0.016f, -0.07f), new Vector3(0.076f, 0.7f, 0.076f)));

            // ---- 6 Finish ----
            Cylinder(root, "FinishCoaster", new Vector3(0.62f, 0f, -0.25f), 0.10f, 0.005f, m.coaster);
            checkpoints.Add(MakeCheckpoint(root, "Finish", new Vector3(0.62f, 0.005f, -0.25f), new Vector3(0.1f, 0.7f, 0.1f)));

            for (int i = 0; i < checkpoints.Count; i++) checkpoints[i].index = i;

            var manager = root.gameObject.AddComponent<CourseManager>();
            manager.spawner = spawner;
            manager.checkpoints = checkpoints.ToArray();
            manager.failSurfaces = fails.ToArray();
            foreach (var c in checkpoints) c.manager = manager;
            foreach (var f in fails) f.manager = manager;
            spawner.transform.position = checkpoints[0].respawnPoint.position;
            return manager;
        }

        /// <summary>Position is the bottom-centre on the desk surface (desk top = y 0 in these coordinates).</summary>
        static GameObject Box(Transform parent, string name, Vector3 bottom, Vector3 size, Material mat, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localPosition = new Vector3(bottom.x, DeskTop + bottom.y + size.y * 0.5f, bottom.z);
            go.transform.localScale = size;
            Finish(go, mat);
            return go;
        }

        static GameObject Cylinder(Transform parent, string name, Vector3 bottom, float diameter, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(bottom.x, DeskTop + bottom.y + height * 0.5f, bottom.z);
            // Unity's cylinder is 2 units tall, and its default capsule collider has a rounded top,
            // which would turn a flat coaster into a dome. Use the cylinder mesh itself instead.
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            Finish(go, mat);
            return go;
        }

        static void Finish(GameObject go, Material mat)
        {
            go.isStatic = true;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().contactOffset = k_ContactOffset;
        }

        static Checkpoint MakeCheckpoint(Transform parent, string label, Vector3 surface, Vector3 size)
        {
            var go = new GameObject($"Checkpoint_{label}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(surface.x, DeskTop + surface.y, surface.z);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);

            var cp = go.AddComponent<Checkpoint>();
            cp.label = label;
            cp.respawnPoint = go.transform; // bottom-centre on the surface
            return cp;
        }
    }
}

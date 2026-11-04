using System.Collections.Generic;
using UnityEngine;

namespace Marionette.Course
{
    /// <summary>
    /// Episode 1 test stage: "can puppeteering be fun and still challenging?"
    ///
    ///   start pad -> ramp up -> Block A -> narrow walkway over a pit -> Block B -> stairs down -> finish
    ///
    /// Every size is in puppet heights (H), so the stage fits any puppet size. The route runs along +Z.
    /// The floor is safe; the pit under the walkway is a fail surface.
    /// </summary>
    public static class Episode1Builder
    {
        [System.Serializable]
        public class Materials
        {
            public Material ground, block, walkway, ramp, stairs, pit, pad;
        }

        /// <summary>Stage dimensions, in puppet heights, and the points derived from them.</summary>
        public class Layout
        {
            public const float BlockSize = 2f;       // block top is 2 H x 2 H
            public const float BlockHeight = 0.6f;
            public const float RampAngle = 15f;      // degrees
            public const float WalkwayLength = 3f;
            public const float WalkwayWidth = 0.4f;  // the feet sit 0.35 H apart: barely room for both
            public const float WalkwayThickness = 0.05f;
            public const int StepCount = 4;          // risers, down to the floor
            public const float StepRiser = 0.15f;
            public const float StepTread = 0.6f;     // whole puppet fits on one tread
            public const float PadSize = 1.2f;

            public readonly float h;
            public readonly float rampStart, rampEnd, blockAStart, walkwayStart, blockBStart, stairsStart, stairsEnd;

            public Layout(float puppetHeight)
            {
                h = puppetHeight;
                float rampLength = BlockHeight / Mathf.Tan(RampAngle * Mathf.Deg2Rad);
                rampStart = 0.8f;
                rampEnd = rampStart + rampLength;
                blockAStart = rampEnd;
                walkwayStart = blockAStart + BlockSize;
                blockBStart = walkwayStart + WalkwayLength;
                stairsStart = blockBStart + BlockSize;
                stairsEnd = stairsStart + (StepCount - 1) * StepTread;
            }

            /// <summary>World point from (x, y, z) in puppet heights.</summary>
            public Vector3 P(float x, float y, float z) => new Vector3(x, y, z) * h;

            public Vector3 Start => P(0f, 0f, 0f);
            public Vector3 RampMiddle => P(0f, BlockHeight * 0.5f, (rampStart + rampEnd) * 0.5f);
            public Vector3 BlockA => P(0f, BlockHeight, blockAStart + BlockSize * 0.5f);
            public Vector3 WalkwayMiddle => P(0f, BlockHeight, walkwayStart + WalkwayLength * 0.5f);
            public Vector3 BlockB => P(0f, BlockHeight, blockBStart + BlockSize * 0.5f);
            public Vector3 Finish => P(0f, 0f, stairsEnd + 1.2f);

            /// <summary>Centre of each tread, top to bottom (the last riser steps onto the floor).</summary>
            public Vector3 Step(int i) => P(0f, BlockHeight - StepRiser * (i + 1), stairsStart + StepTread * (i + 0.5f));
            public int TreadCount => StepCount - 1;
        }

        public static CourseManager Build(PuppetSpawner spawner, float puppetHeight, Materials m)
        {
            m ??= new Materials();
            var L = new Layout(puppetHeight);
            var root = new GameObject("Episode1_Stage").transform;
            var checkpoints = new List<Checkpoint>();
            var fails = new List<FailSurface>();
            float H = Layout.BlockHeight, B = Layout.BlockSize;

            // ---- Ground and pads ----
            Box(root, "Ground", L.P(0f, -0.1f, 6f), L.P(40f, 0.1f, 40f), m.ground);
            Box(root, "StartPad", L.P(0f, 0f, 0f), L.P(Layout.PadSize, 0.01f, Layout.PadSize), m.pad);
            Box(root, "FinishPad", L.Finish, L.P(Layout.PadSize, 0.01f, Layout.PadSize), m.pad);
            checkpoints.Add(MakeCheckpoint(root, "Start", L.Start + L.P(0f, 0.01f, 0f), L.P(0.8f, 3f, 0.8f)));

            // ---- Ramp up to Block A: a plank tilted 15 degrees, top surface from the floor to the block edge ----
            float t = Layout.WalkwayThickness;
            float run = L.rampEnd - L.rampStart;
            float slope = run / Mathf.Cos(Layout.RampAngle * Mathf.Deg2Rad);
            var tilt = Quaternion.Euler(-Layout.RampAngle, 0f, 0f);
            Vector3 topMiddle = L.P(0f, H * 0.5f, (L.rampStart + L.rampEnd) * 0.5f);
            var ramp = Box(root, "Ramp", Vector3.zero, L.P(B, t, slope + 0.1f), m.ramp, tilt);
            ramp.transform.position = topMiddle - tilt * Vector3.up * (t * 0.5f * L.h);

            // ---- Block A ----
            Box(root, "BlockA", L.P(0f, 0f, L.blockAStart + B * 0.5f), L.P(B, H, B), m.block);
            checkpoints.Add(MakeCheckpoint(root, "Up the ramp", L.BlockA, L.P(1.6f, 3f, 1.2f)));

            // ---- Narrow walkway over the pit ----
            var pit = Box(root, "Pit", L.P(0f, 0f, L.walkwayStart + Layout.WalkwayLength * 0.5f), L.P(6f, 0.01f, Layout.WalkwayLength), m.pit);
            fails.Add(pit.AddComponent<FailSurface>());
            Box(root, "Walkway", L.P(0f, H - t, L.walkwayStart + Layout.WalkwayLength * 0.5f),
                L.P(Layout.WalkwayWidth, t, Layout.WalkwayLength + 0.2f), m.walkway);

            // ---- Block B ----
            Box(root, "BlockB", L.P(0f, 0f, L.blockBStart + B * 0.5f), L.P(B, H, B), m.block);
            checkpoints.Add(MakeCheckpoint(root, "Across the walkway", L.BlockB, L.P(1.6f, 3f, 1.2f)));

            // ---- Stairs down: each tread holds the whole puppet ----
            for (int i = 0; i < L.TreadCount; i++)
            {
                Vector3 c = L.Step(i);
                Box(root, $"Step{i + 1}", new Vector3(c.x, 0f, c.z), new Vector3(B * L.h, c.y, Layout.StepTread * L.h), m.stairs);
            }
            checkpoints.Add(MakeCheckpoint(root, "Down the stairs", L.Finish + L.P(0f, 0.01f, 0f), L.P(1f, 3f, 1f)));

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

        /// <summary>Box standing on bottom (bottom-centre), world units.</summary>
        static GameObject Box(Transform parent, string name, Vector3 bottom, Vector3 size, Material mat, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localPosition = bottom + Vector3.up * (size.y * 0.5f);
            go.transform.localScale = size;
            go.isStatic = true;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().contactOffset = 0.001f;
            return go;
        }

        static Checkpoint MakeCheckpoint(Transform parent, string label, Vector3 surface, Vector3 size)
        {
            var go = new GameObject($"Checkpoint_{label}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = surface;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            var cp = go.AddComponent<Checkpoint>();
            cp.label = label;
            cp.respawnPoint = go.transform;
            return cp;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Builds the default sparse puppet (Prototype A): six rigid nodes, textile links between them,
    /// six external strings on outside attachment points, two control crosses.
    /// All numbers here are first guesses meant to be tuned in the inspector afterwards.
    /// </summary>
    public static class MarionetteBuilder
    {
        [System.Serializable]
        public class Materials
        {
            public Material wood;
            public Material metal;
            public Material rubber;   // suction cups
            public Material cloth;    // textile lines
            public Material line;     // control strings
            public Material crossWood;
            public PhysicsMaterial footGrip;
        }

        /// <summary>All lengths below are at the reference height and get scaled to <see cref="height"/>.</summary>
        public class Settings
        {
            public float height = ReferenceHeight; // puppet height in metres, feet to crown
            public float clothSlack = 0.05f;       // textile max length = rest distance * (1 + slack)
            public float clothWidth = 0.035f;
            public float stringWidth = 0.002f;
            public float headStringHeight = 1.0f;  // left cross height above the puppet's feet
            public float legStringHeight = 0.9f;   // right cross height above the puppet's feet
            public float footStringSlack = 0.06f;  // extra foot string length: resting feet press on the ground, and steps aren't held back
        }

        /// <summary>Height the numbers in this file were designed at (gnome size).</summary>
        public const float ReferenceHeight = 0.575f;

        struct NodeSpec
        {
            public PuppetNodeId id;
            public PrimitiveType shape;
            public Vector3 position;
            public Vector3 scale;
            public float mass;
        }

        // Puppet is ~0.6 m tall, slightly oversized head, hands and feet.
        static readonly NodeSpec[] k_Nodes =
        {
            new NodeSpec { id = PuppetNodeId.Head,      shape = PrimitiveType.Sphere,  position = new Vector3(0f, 0.50f, 0f),      scale = new Vector3(0.14f, 0.15f, 0.14f), mass = 0.6f },
            new NodeSpec { id = PuppetNodeId.Center,    shape = PrimitiveType.Capsule, position = new Vector3(0f, 0.30f, 0f),      scale = new Vector3(0.14f, 0.10f, 0.11f), mass = 2.0f },
            new NodeSpec { id = PuppetNodeId.LeftHand,  shape = PrimitiveType.Sphere,  position = new Vector3(-0.20f, 0.30f, 0f),  scale = new Vector3(0.07f, 0.05f, 0.08f), mass = 0.3f },
            new NodeSpec { id = PuppetNodeId.RightHand, shape = PrimitiveType.Sphere,  position = new Vector3(0.20f, 0.30f, 0f),   scale = new Vector3(0.07f, 0.05f, 0.08f), mass = 0.3f },
            new NodeSpec { id = PuppetNodeId.LeftFoot,  shape = PrimitiveType.Cube,    position = new Vector3(-0.07f, 0.02f, 0.02f), scale = new Vector3(0.06f, 0.04f, 0.12f), mass = 0.5f },
            new NodeSpec { id = PuppetNodeId.RightFoot, shape = PrimitiveType.Cube,    position = new Vector3(0.07f, 0.02f, 0.02f),  scale = new Vector3(0.06f, 0.04f, 0.12f), mass = 0.5f },
        };

        public static MarionetteRig Build(Vector3 position, Materials mats, Settings settings = null)
        {
            settings ??= new Settings();
            mats ??= new Materials();
            float s = settings.height / ReferenceHeight;
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;

            var root = new GameObject("Marionette").transform;
            root.position = position;
            var rig = root.gameObject.AddComponent<MarionetteRig>();
            rig.height = settings.height;
            rig.scale = s;

            var nodesRoot = Child(root, "PhysicsNodes");
            var textileRoot = Child(root, "TextileBody");
            var controlRoot = Child(root, "ControlRig");

            var nodes = new Dictionary<PuppetNodeId, PuppetNode>();
            var attachments = new List<PuppetAttachment>();

            foreach (var spec in k_Nodes)
                nodes[spec.id] = CreateNode(nodesRoot, spec, mats, s);

            PuppetAttachment Attach(PuppetNodeId id, string name, AttachmentType type, Vector3 localInWorldUnits)
            {
                var a = CreateAttachment(nodes[id], name, type, localInWorldUnits);
                attachments.Add(a);
                return a;
            }

            // ---- External control points: hands and feet only, on the OUTSIDE surface of their node.
            // The body and head have no strings: they hang from the arms and get dragged along. ----
            var lHandBack = Attach(PuppetNodeId.LeftHand, "Ctrl_LeftHandBack", AttachmentType.ExternalControl, V(0f, 0.025f, 0f));
            var rHandBack = Attach(PuppetNodeId.RightHand, "Ctrl_RightHandBack", AttachmentType.ExternalControl, V(0f, 0.025f, 0f));
            var lFootTop = Attach(PuppetNodeId.LeftFoot, "Ctrl_LeftFootInstep", AttachmentType.ExternalControl, V(0f, 0.02f, 0.01f));
            var rFootTop = Attach(PuppetNodeId.RightFoot, "Ctrl_RightFootInstep", AttachmentType.ExternalControl, V(0f, 0.02f, 0.01f));

            // ---- Suction cups (palms + soles) and internal grapple line exits (palms). ----
            Attach(PuppetNodeId.LeftHand, "Suction_LeftPalm", AttachmentType.Suction, V(0f, -0.025f, 0f));
            Attach(PuppetNodeId.RightHand, "Suction_RightPalm", AttachmentType.Suction, V(0f, -0.025f, 0f));
            Attach(PuppetNodeId.LeftFoot, "Suction_LeftSole", AttachmentType.Suction, V(0f, -0.02f, 0f));
            Attach(PuppetNodeId.RightFoot, "Suction_RightSole", AttachmentType.Suction, V(0f, -0.02f, 0f));
            Attach(PuppetNodeId.LeftHand, "Grapple_LeftPalm", AttachmentType.Grapple, V(0f, -0.025f, 0f));
            Attach(PuppetNodeId.RightHand, "Grapple_RightPalm", AttachmentType.Grapple, V(0f, -0.025f, 0f));
            AddSuctionCupVisual(nodes[PuppetNodeId.LeftHand], -0.025f * s, s, mats.rubber);
            AddSuctionCupVisual(nodes[PuppetNodeId.RightHand], -0.025f * s, s, mats.rubber);
            AddSuctionCupVisual(nodes[PuppetNodeId.LeftFoot], -0.02f * s, s, mats.rubber);
            AddSuctionCupVisual(nodes[PuppetNodeId.RightFoot], -0.02f * s, s, mats.rubber);

            // ---- Textile: everything between head, hands, feet and center is cloth. ----
            var textile = new List<TextileLink>
            {
                Link(textileRoot, settings, mats, s, Attach(PuppetNodeId.Center, "Tex_Neck", AttachmentType.Textile, V(0f, 0.11f, 0f)),
                                                  Attach(PuppetNodeId.Head, "Tex_Chin", AttachmentType.Textile, V(0f, -0.07f, 0f))),
                Link(textileRoot, settings, mats, s, Attach(PuppetNodeId.Center, "Tex_LeftShoulder", AttachmentType.Textile, V(-0.06f, 0.07f, 0f)),
                                                  Attach(PuppetNodeId.LeftHand, "Tex_LeftWrist", AttachmentType.Textile, V(0.03f, 0f, 0f))),
                Link(textileRoot, settings, mats, s, Attach(PuppetNodeId.Center, "Tex_RightShoulder", AttachmentType.Textile, V(0.06f, 0.07f, 0f)),
                                                  Attach(PuppetNodeId.RightHand, "Tex_RightWrist", AttachmentType.Textile, V(-0.03f, 0f, 0f))),
                Link(textileRoot, settings, mats, s, Attach(PuppetNodeId.Center, "Tex_LeftHip", AttachmentType.Textile, V(-0.045f, -0.1f, 0f)),
                                                  Attach(PuppetNodeId.LeftFoot, "Tex_LeftAnkle", AttachmentType.Textile, V(0f, 0.02f, -0.035f))),
                Link(textileRoot, settings, mats, s, Attach(PuppetNodeId.Center, "Tex_RightHip", AttachmentType.Textile, V(0.045f, -0.1f, 0f)),
                                                  Attach(PuppetNodeId.RightFoot, "Tex_RightAnkle", AttachmentType.Textile, V(0f, 0.02f, -0.035f))),
            };

            // ---- Control rig: left hand = the puppet's hands, right hand = its feet. ----
            var tops = Child(controlRoot, "StringTops");
            var leftCross = CreateCross(controlRoot, tops, ControlSide.Left, position + Vector3.up * (settings.headStringHeight * s), settings, mats, s,
                ("Left hand", lHandBack, V(-0.20f, 0f, 0f)),
                ("Right hand", rHandBack, V(0.20f, 0f, 0f)));
            var rightCross = CreateCross(controlRoot, tops, ControlSide.Right, position + Vector3.up * (settings.legStringHeight * s), settings, mats, s,
                ("Left foot", lFootTop, V(-0.07f, 0f, 0.03f)),
                ("Right foot", rFootTop, V(0.07f, 0f, 0.03f)));

            // ---- Body joints: head held on the body; hands and feet can't spin freely. ----
            var center = nodes[PuppetNodeId.Center];
            rig.neckJoint = BodyJoint(nodes[PuppetNodeId.Head], center, lockPosition: true);
            rig.limbJoints = new[]
            {
                BodyJoint(nodes[PuppetNodeId.LeftHand], center, lockPosition: false),
                BodyJoint(nodes[PuppetNodeId.RightHand], center, lockPosition: false),
                BodyJoint(nodes[PuppetNodeId.LeftFoot], center, lockPosition: false),
                BodyJoint(nodes[PuppetNodeId.RightFoot], center, lockPosition: false),
            };

            rig.nodes = new List<PuppetNode>(nodes.Values).ToArray();
            rig.textile = textile.ToArray();
            rig.attachments = attachments.ToArray();
            rig.leftCross = leftCross;
            rig.rightCross = rightCross;
            rig.materials = mats;
            root.gameObject.AddComponent<MarionetteTuning>();
            root.gameObject.AddComponent<AutoStep>();
            root.gameObject.AddComponent<StepTurn>();
            root.gameObject.AddComponent<BalanceMonitor>(); // last: reads the finished nodes
            return rig;
        }

        /// <summary>
        /// Joint between a part and the body, relative to how they sit as built. Limits come from
        /// MarionetteTuning. lockPosition pins the part on the body (the neck); otherwise the part is sprung
        /// to its usual place (limbSpring, ankle/wrist stiffness), so moving it drags the body along.
        /// </summary>
        static ConfigurableJoint BodyJoint(PuppetNode part, PuppetNode body, bool lockPosition)
        {
            var j = part.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = body.Body;
            // Fixed anchors (not auto-configured), so resizing and re-attaching keep the built relationship.
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = body.transform.InverseTransformPoint(part.transform.position);
            var linear = lockPosition ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            j.xMotion = j.yMotion = j.zMotion = linear;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.enableCollision = false;
            j.enablePreprocessing = false;
            return j;
        }

        static PuppetNode CreateNode(Transform parent, NodeSpec spec, Materials mats, float s)
        {
            var go = GameObject.CreatePrimitive(spec.shape);
            go.name = spec.id.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spec.position * s;
            // Unity's capsule is 2 units tall, so its Y scale is half the real height.
            go.transform.localScale = spec.scale * s;
            go.GetComponent<Renderer>().sharedMaterial = mats.wood;

            if (spec.id == PuppetNodeId.LeftFoot || spec.id == PuppetNodeId.RightFoot)
                go.GetComponent<Collider>().sharedMaterial = mats.footGrip;

            var collider = go.GetComponent<Collider>();
            // Default contact offset (1 cm) is bigger than a mouse-sized foot; keep it proportional.
            collider.contactOffset = Mathf.Max(0.0005f, 0.006f * s);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = spec.mass * s * s * s;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 25;
            rb.solverVelocityIterations = 10;

            var node = go.AddComponent<PuppetNode>();
            node.id = spec.id;

            // Feet and hands know when they touch the ground (balance) and lie flat on it (placement).
            if (spec.id != PuppetNodeId.Head && spec.id != PuppetNodeId.Center)
            {
                go.AddComponent<GroundContact>();
                go.AddComponent<SurfacePlacement>();
            }
            return node;
        }

        /// <summary>Offsets are in world units relative to the node centre, so scaled primitives don't distort them.</summary>
        static PuppetAttachment CreateAttachment(PuppetNode node, string name, AttachmentType type, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(node.transform, false);
            go.transform.position = node.transform.position + offset;
            var a = go.AddComponent<PuppetAttachment>();
            a.attachmentId = name;
            a.type = type;
            a.node = node;
            return a;
        }

        static void AddSuctionCupVisual(PuppetNode node, float yOffset, float s, Material mat)
        {
            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "SuctionCup";
            Object.DestroyImmediate(cup.GetComponent<Collider>());
            cup.transform.SetParent(node.transform, false);
            cup.transform.position = node.transform.position + new Vector3(0f, yOffset, 0f);
            cup.transform.localScale = DivideByParentScale(node.transform, new Vector3(0.04f, 0.004f, 0.04f) * s);
            cup.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static TextileLink Link(Transform parent, Settings settings, Materials mats, float s, PuppetAttachment fromPoint, PuppetAttachment toPoint)
        {
            var go = new GameObject($"Textile_{fromPoint.node.id}_{toPoint.node.id}");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = mats.cloth;
            line.widthMultiplier = settings.clothWidth * s;
            line.numCapVertices = 4;
            line.positionCount = 2;
            line.SetPosition(0, fromPoint.transform.position);
            line.SetPosition(1, toPoint.transform.position);

            var link = go.AddComponent<TextileLink>();
            link.from = fromPoint.node;
            link.to = toPoint.node;
            link.fromPoint = fromPoint;
            link.toPoint = toPoint;
            float rest = Vector3.Distance(fromPoint.transform.position, toPoint.transform.position);
            link.Configure(rest, settings.clothSlack);
            return link;
        }

        static ControlCross CreateCross(Transform parent, Transform topsParent, ControlSide side, Vector3 neutralPos, Settings settings, Materials mats, float s,
            params (string name, PuppetAttachment attachment, Vector3 offset)[] channels)
        {
            var neutral = Child(parent, $"{side}CrossNeutral");
            neutral.position = neutralPos;

            var crossGo = new GameObject($"{side}ControlCross");
            crossGo.transform.SetParent(parent, false);
            crossGo.transform.position = neutralPos;
            var cross = crossGo.AddComponent<ControlCross>();
            cross.side = side;
            cross.neutral = neutral;
            cross.channels = new ControlCross.Channel[channels.Length];

            for (int i = 0; i < channels.Length; i++)
            {
                var (name, attachment, offset) = channels[i];
                Vector3 top = neutralPos + offset;

                var handleGo = new GameObject($"Top_{name.Replace(" ", "")}");
                handleGo.transform.SetParent(topsParent, false);
                handleGo.transform.position = top;
                var handle = handleGo.AddComponent<Rigidbody>();
                handle.isKinematic = true;
                handle.interpolation = RigidbodyInterpolation.Interpolate;

                var stringGo = new GameObject($"String_{name.Replace(" ", "")}");
                stringGo.transform.SetParent(crossGo.transform, false);
                var line = stringGo.AddComponent<LineRenderer>();
                line.sharedMaterial = mats.line;
                line.widthMultiplier = settings.stringWidth * Mathf.Max(s, 0.5f);
                line.positionCount = 2;
                line.SetPosition(0, top);
                line.SetPosition(1, attachment.transform.position);

                var str = stringGo.AddComponent<ControlString>();
                str.attachment = attachment;
                str.handle = handle;
                // Feet hang a little slack at rest: their own weight keeps them on the ground, which is
                // what balance counts as standing. A taut foot string would leave them hovering.
                bool foot = attachment.node.id == PuppetNodeId.LeftFoot || attachment.node.id == PuppetNodeId.RightFoot;
                str.Configure(Vector3.Distance(top, attachment.transform.position) + (foot ? settings.footStringSlack * s : 0f));

                cross.channels[i] = new ControlCross.Channel { name = name, controlString = str, topOffset = offset };
                AddCrossBar(crossGo.transform, Vector3.zero, offset, 0.012f * s, mats.crossWood);
            }
            return cross;
        }

        static void AddCrossBar(Transform cross, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            if ((b - a).sqrMagnitude < 1e-6f) return;
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(cross, false);
            bar.transform.localPosition = (a + b) * 0.5f;
            bar.transform.localRotation = Quaternion.LookRotation(b - a);
            bar.transform.localScale = new Vector3(thickness, thickness, (b - a).magnitude);
            bar.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static Vector3 DivideByParentScale(Transform parent, Vector3 worldScale)
        {
            Vector3 s = parent.lossyScale;
            return new Vector3(worldScale.x / s.x, worldScale.y / s.y, worldScale.z / s.z);
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}

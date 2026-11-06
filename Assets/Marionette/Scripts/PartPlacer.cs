using System.Collections.Generic;
using Marionette.Room;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Point & place: choose where a hand or foot goes, then watch the puppet try to get there.
    ///
    ///   1. Select: pinch a string with the left hand (index / middle / ring / little = left hand / right hand /
    ///      left foot / right foot) and keep pinching. A pointer comes from the right hand. Where it hits a
    ///      surface within range a marker appears, and a see-through copy of the puppet shows roughly the stance
    ///      it would end up in. While selecting, the puppet can't fall (BalanceMonitor.Protect).
    ///   2. Commit: pinch with the right hand. The target is set and the ghost stays as a guide.
    ///   3. Execute: a foot steps there (AutoStep), a hand reaches there and stays (StringFlick hold). Further than
    ///      the limb reaches pulls the body toward it, and the automatic steps try to keep it up. Now balance
    ///      counts: lean against the momentum with the line between your hands.
    ///
    /// Marker and ghost colours: green within reach, amber beyond reach (it will pull the body over), no marker
    /// and a red pointer out of range.
    /// </summary>
    [RequireComponent(typeof(StringFlick))]
    public class PartPlacer : MonoBehaviour
    {
        public enum Phase { Idle, Selecting, Executing }

        [Tooltip("Hands: a target counts as within reach up to this many times the arm's length from the shoulder.")]
        public float reach = 1.15f;
        [Tooltip("Hands: furthest target that can be chosen, times the arm's length. Beyond reach, the body is pulled over toward it.")]
        public float range = 2f;
        [Tooltip("Feet: a comfortable step, puppet heights from where the foot is now.")]
        public float stepReach = 0.55f;
        [Tooltip("Feet: furthest step that can be chosen, puppet heights. Beyond stepReach, the body is pulled over toward it.")]
        public float stepRange = 1.1f;
        [Tooltip("While a target beyond reach is being reached for, the body is pulled toward it: puppet heights per second squared per puppet height of overreach.")]
        public float overreachPull = 20f;
        [Tooltip("Longest pointer, puppet heights.")]
        public float maxDistance = 12f;
        [Tooltip("Seconds a hand may take to reach its target.")]
        public float handSeconds = 3f;
        public Color inReachColor = new Color(0.4f, 1f, 0.55f, 0.45f);
        public Color overreachColor = new Color(1f, 0.7f, 0.2f, 0.45f);
        public Color outOfRangeColor = new Color(1f, 0.25f, 0.2f, 0.8f);

        PuppetSpawner m_Spawner;
        StringFlick m_Flick;
        XRHandsControlInput m_Hands;
        LineRenderer m_Pointer;
        Transform m_Marker;
        Renderer m_MarkerRenderer;
        StanceGhost m_Ghost;
        MaterialPropertyBlock m_Block;
        bool m_WasRightPinching, m_NeedRelease;
        float m_Lift, m_ExecTime, m_PullOver;
        Vector3 m_GroundPoint, m_PartGoal;

        public Phase Current { get; private set; }
        public PuppetNodeId Part { get; private set; }
        public bool TargetValid { get; private set; }
        public bool TargetInReach { get; private set; }
        /// <summary>Where the chosen part's centre will go.</summary>
        public Vector3 PartGoal => m_PartGoal;
        /// <summary>The point on the surface the pointer is on.</summary>
        public Vector3 TargetPoint => m_GroundPoint;
        public bool GhostVisible => m_Ghost != null && m_Ghost.Visible;
        public StanceGhost Ghost => m_Ghost;

        /// <summary>Tests and scripted demos: point here instead of the right hand.</summary>
        [System.NonSerialized] public Ray? aimOverride;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            m_Flick = GetComponent<StringFlick>();
            m_Hands = GetComponent<XRHandsControlInput>();
            m_Block = new MaterialPropertyBlock();
            var holo = PuppeteerRoomBuilder.Holo(Color.white);

            var go = new GameObject("TargetPointer");
            go.transform.SetParent(transform, false);
            m_Pointer = go.AddComponent<LineRenderer>();
            m_Pointer.useWorldSpace = true;
            m_Pointer.positionCount = 2;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) m_Pointer.sharedMaterial = new Material(shader);
            m_Pointer.enabled = false;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "TargetMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            m_MarkerRenderer = marker.GetComponent<Renderer>();
            m_MarkerRenderer.sharedMaterial = holo;
            marker.SetActive(false);
            m_Marker = marker.transform;
            m_Ghost = new StanceGhost(holo);
        }

        void OnDestroy() => m_Ghost?.Destroy();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            foreach (var flick in FindObjectsByType<StringFlick>(FindObjectsSortMode.None))
                if (flick.GetComponent<PartPlacer>() == null) flick.gameObject.AddComponent<PartPlacer>();
        }

        BalanceMonitor Balance => m_Spawner.rig != null ? m_Spawner.rig.GetComponent<BalanceMonitor>() : null;
        AutoStep Steps => m_Spawner.rig != null ? m_Spawner.rig.GetComponent<AutoStep>() : null;
        static bool IsFoot(PuppetNodeId id) => id is PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot;

        // ---------- Control flow ----------

        void FixedUpdate()
        {
            var rig = m_Spawner.rig;
            if (rig == null) return;
            if (!m_Flick.isActiveAndEnabled) { if (Current != Phase.Idle) Cancel(); return; } // nobody connected
            bool pointPlace = m_Hands != null && m_Hands.fingerAction == XRHandsControlInput.FingerAction.PointPlace;
            bool rightPinch = pointPlace && m_Hands.RightPinching;
            bool rightStarted = rightPinch && !m_WasRightPinching;
            m_WasRightPinching = rightPinch;
            PuppetNodeId? held = pointPlace ? m_Hands.HeldTarget : null;

            switch (Current)
            {
                case Phase.Idle:
                    if (!held.HasValue) m_NeedRelease = false;
                    else if (!m_NeedRelease) BeginSelect(held.Value);
                    break;
                case Phase.Selecting:
                    if (pointPlace && held != Part) { Cancel(); break; }
                    Ray? aim = aimOverride ?? (pointPlace && m_Hands.RightAimValid ? m_Hands.RightAim : (Ray?)null);
                    if (aim.HasValue) Aim(aim.Value);
                    if (rightStarted && Commit()) m_NeedRelease = true;
                    break;
                case Phase.Executing:
                    Execute(Time.fixedDeltaTime);
                    break;
            }
        }

        /// <summary>Start choosing a target for a hand or foot. The puppet is held steady meanwhile.</summary>
        public bool BeginSelect(PuppetNodeId part)
        {
            var rig = m_Spawner.rig;
            var balance = Balance;
            if (rig == null || Current == Phase.Executing || (balance != null && balance.ControlsOverridden)) return false;
            if (part is not (PuppetNodeId.LeftHand or PuppetNodeId.RightHand or PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot)) return false;
            Part = part;
            var body = rig.Node(part).Body;
            var col = body.GetComponent<Collider>();
            m_Lift = col != null ? body.worldCenterOfMass.y - col.bounds.min.y : 0.05f * rig.height;
            TargetValid = TargetInReach = false;
            Current = Phase.Selecting;
            if (balance != null) balance.Protect(true);
            return true;
        }

        /// <summary>Point at a surface: updates the target, the marker and the ghost.</summary>
        public void Aim(Ray ray)
        {
            var rig = m_Spawner.rig;
            if (rig == null || Current != Phase.Selecting) return;
            float h = rig.height;
            TargetValid = TargetInReach = false;
            Vector3 end = ray.origin + ray.direction * (maxDistance * h);
            bool hitSomething = false;
            float best = float.MaxValue;
            RaycastHit chosen = default;
            foreach (var hit in Physics.RaycastAll(ray, maxDistance * h, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<MarionetteRig>() != null || hit.distance >= best) continue;
                best = hit.distance;
                chosen = hit;
                hitSomething = true;
            }
            if (hitSomething)
            {
                end = chosen.point;
                bool standable = !IsFoot(Part) || chosen.normal.y > 0.6f; // feet go on floors and steps, hands anywhere
                Vector3 goal = chosen.point + (IsFoot(Part) ? Vector3.up : chosen.normal) * m_Lift;
                if (standable && Overreach(rig, goal, out float over, out float limit))
                {
                    TargetValid = over <= limit;
                    TargetInReach = over <= 0f;
                    m_GroundPoint = chosen.point;
                    m_PartGoal = goal;
                }
            }
            Color c = !TargetValid ? outOfRangeColor : TargetInReach ? inReachColor : overreachColor;
            m_Pointer.enabled = true;
            m_Pointer.widthMultiplier = 0.012f * h;
            m_Pointer.startColor = new Color(c.r, c.g, c.b, 0.9f);
            m_Pointer.endColor = new Color(c.r, c.g, c.b, 0.3f);
            m_Pointer.SetPosition(0, ray.origin);
            m_Pointer.SetPosition(1, end);
            ShowTarget(rig, c);
        }

        void ShowTarget(MarionetteRig rig, Color c)
        {
            m_Marker.gameObject.SetActive(TargetValid);
            if (!TargetValid) { m_Ghost.Hide(); return; }
            float h = rig.height;
            m_Marker.position = m_GroundPoint + Vector3.up * (0.004f * h);
            m_Marker.localScale = new Vector3(0.18f * h, 0.002f * h, 0.18f * h);
            m_Block.SetColor("_BaseColor", c);
            m_MarkerRenderer.SetPropertyBlock(m_Block);
            m_Ghost.Show(rig, Part, m_PartGoal, StanceShift(rig), c);
        }

        /// <summary>
        /// How far (metres) a goal is beyond comfortable reach (negative: within it), and how much beyond reach is
        /// still allowed. Feet: a step from where the foot is. Hands: the arm's length from the shoulder.
        /// </summary>
        bool Overreach(MarionetteRig rig, Vector3 goal, out float over, out float allowed)
        {
            float h = rig.height;
            if (IsFoot(Part))
            {
                float d = Flat(goal - rig.Node(Part).Body.worldCenterOfMass).magnitude;
                over = d - stepReach * h;
                allowed = (stepRange - stepReach) * h;
                return true;
            }
            over = allowed = 0f;
            if (!Limb(rig, Part, out Vector3 root, out float length)) return false;
            over = Vector3.Distance(root, goal) - length * reach;
            allowed = length * (range - reach);
            return true;
        }

        /// <summary>Roughly how the body moves to make the stance: half a foot's step, or as far as a hand overreaches.</summary>
        Vector3 StanceShift(MarionetteRig rig)
        {
            Vector3 now = rig.Node(Part).Body.worldCenterOfMass;
            if (IsFoot(Part)) return Flat(m_PartGoal - now) * 0.5f;
            if (!Limb(rig, Part, out Vector3 root, out _)) return Vector3.zero;
            Overreach(rig, m_PartGoal, out float over, out _);
            return over > 0f ? Flat((m_PartGoal - root).normalized) * over : Vector3.zero;
        }

        /// <summary>Set the target: the puppet starts moving and balance counts again.</summary>
        public bool Commit()
        {
            var rig = m_Spawner.rig;
            if (Current != Phase.Selecting || !TargetValid || rig == null) return false;
            var balance = Balance;
            if (balance != null) balance.Protect(false);
            m_Pointer.enabled = false;
            Current = Phase.Executing;
            m_ExecTime = 0f;
            if (IsFoot(Part))
            {
                var steps = Steps;
                if (steps == null) { Finish(); return false; }
                Overreach(rig, m_PartGoal, out m_PullOver, out _); // how far past a comfortable step, fixed at commit
                steps.Begin(Part == PuppetNodeId.LeftFoot ? 0 : 1, m_GroundPoint, stagger: false, plant: true);
            }
            else
            {
                if (m_Flick.Holding && m_Flick.HeldPart != Part) m_Flick.EndHold(); // the other hand goes back
                if (!m_Flick.Holding && !m_Flick.BeginHold(Part)) { Finish(); return false; }
                m_Flick.SetHoldGoal(m_PartGoal);
            }
            return true;
        }

        /// <summary>Stop selecting without choosing anything.</summary>
        public void Cancel()
        {
            var balance = Balance;
            if (balance != null) balance.Protect(false);
            Finish();
        }

        void Execute(float dt)
        {
            var rig = m_Spawner.rig;
            var balance = Balance;
            m_ExecTime += dt;
            if (balance != null && balance.ControlsOverridden) { Finish(); return; } // lost it: the move is over
            if (IsFoot(Part))
            {
                PullTowardTarget(rig);
                var steps = Steps;
                if (steps == null || (!steps.Stepping && m_ExecTime > 0.05f)) Finish();
            }
            else
            {
                m_Flick.SetHoldGoal(m_PartGoal); // the hold pulls the body itself when it overreaches
                float d = Vector3.Distance(rig.Node(Part).Body.worldCenterOfMass, m_PartGoal);
                if (d < 0.06f * rig.height || m_ExecTime > handSeconds) Finish(); // the hand stays held there
            }
        }

        /// <summary>A foot sent further than a comfortable step drags the body after it.</summary>
        void PullTowardTarget(MarionetteRig rig)
        {
            float over = m_PullOver;
            if (over <= 0f) return;
            var torso = rig.Node(PuppetNodeId.Center).Body;
            Vector3 dir = Flat(m_PartGoal - torso.worldCenterOfMass);
            if (dir.sqrMagnitude < 1e-8f) return;
            torso.AddForce(dir.normalized * (Mathf.Min(over, rig.height) * overreachPull), ForceMode.Acceleration);
        }

        void Finish()
        {
            Current = Phase.Idle;
            TargetValid = false;
            m_Pointer.enabled = false;
            m_Marker.gameObject.SetActive(false);
            m_Ghost.Hide();
        }

        /// <summary>The limb's shoulder or hip and its length (from its cloth sleeve).</summary>
        static bool Limb(MarionetteRig rig, PuppetNodeId part, out Vector3 root, out float length)
        {
            root = Vector3.zero; length = 0f;
            foreach (var link in rig.textile)
            {
                if (link.to.id != part && link.from.id != part) continue;
                bool fromBody = link.to.id == part;
                root = (fromBody ? link.fromPoint : link.toPoint).transform.position;
                length = link.restLength + Vector3.Distance((fromBody ? link.toPoint : link.fromPoint).transform.position, rig.Node(part).Body.worldCenterOfMass);
                return true;
            }
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    /// <summary>
    /// A see-through copy of the puppet: its parts where they are now, the chosen part at its target, and the
    /// body shifted roughly as the stance needs. Limbs are drawn as lines.
    /// </summary>
    public class StanceGhost
    {
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        GameObject m_Root;
        MarionetteRig m_For;
        readonly Dictionary<PuppetNodeId, Transform> m_Parts = new Dictionary<PuppetNodeId, Transform>();
        readonly List<LineRenderer> m_Lines = new List<LineRenderer>();

        public bool Visible => m_Root != null && m_Root.activeSelf;

        public StanceGhost(Material material) => m_Material = material;

        /// <summary>Where the ghost shows a part (for tests).</summary>
        public Vector3 PartPosition(PuppetNodeId id) => m_Parts.TryGetValue(id, out var t) ? t.position : Vector3.zero;

        void Build(MarionetteRig rig)
        {
            Destroy();
            m_For = rig;
            m_Root = new GameObject("StanceGhost");
            foreach (var node in rig.nodes)
            {
                var mf = node.GetComponent<MeshFilter>();
                var mr = node.GetComponent<MeshRenderer>();
                var go = new GameObject("Ghost_" + node.id);
                go.transform.SetParent(m_Root.transform, false);
                if (mf != null && mr != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = m_Material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                m_Parts[node.id] = go.transform;
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            for (int i = 0; i < 5; i++)
            {
                var line = new GameObject("GhostLimb").AddComponent<LineRenderer>();
                line.transform.SetParent(m_Root.transform, false);
                line.useWorldSpace = true;
                line.positionCount = 2;
                if (shader != null) line.sharedMaterial = new Material(shader);
                m_Lines.Add(line);
            }
        }

        public void Show(MarionetteRig rig, PuppetNodeId moved, Vector3 movedGoal, Vector3 bodyShift, Color color)
        {
            if (rig != m_For || m_Root == null) Build(rig);
            m_Root.SetActive(true);
            m_Block.SetColor("_BaseColor", color);
            foreach (var node in rig.nodes)
            {
                var t = m_Parts[node.id];
                Vector3 p = node.transform.position;
                if (node.id == moved) p = movedGoal + (node.transform.position - node.Body.worldCenterOfMass);
                else if (Shifts(node.id, moved)) p += bodyShift;
                t.SetPositionAndRotation(p, node.transform.rotation);
                t.localScale = node.transform.lossyScale;
                var r = t.GetComponent<Renderer>();
                if (r != null) r.SetPropertyBlock(m_Block);
            }
            Vector3 c = m_Parts[PuppetNodeId.Center].position;
            var ends = new[] { PuppetNodeId.Head, PuppetNodeId.LeftHand, PuppetNodeId.RightHand, PuppetNodeId.LeftFoot, PuppetNodeId.RightFoot };
            for (int i = 0; i < ends.Length; i++)
            {
                m_Lines[i].widthMultiplier = 0.02f * rig.height;
                m_Lines[i].startColor = m_Lines[i].endColor = color;
                m_Lines[i].SetPosition(0, c);
                m_Lines[i].SetPosition(1, m_Parts[ends[i]].position);
            }
        }

        /// <summary>Which parts move with the body: everything but the moved part and the feet still standing.</summary>
        static bool Shifts(PuppetNodeId id, PuppetNodeId moved) =>
            id is PuppetNodeId.Center or PuppetNodeId.Head || (id is PuppetNodeId.LeftHand or PuppetNodeId.RightHand && id != moved);

        public void Hide()
        {
            if (m_Root != null) m_Root.SetActive(false);
        }

        public void Destroy()
        {
            if (m_Root != null) Object.Destroy(m_Root);
            m_Root = null;
            m_Parts.Clear();
            m_Lines.Clear();
        }
    }
}

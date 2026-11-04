using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Point & place: move the puppet's hands and feet live, by pointing.
    ///
    /// Take hold of a part either way:
    ///   - pinch its string with the left hand (thumb to index / middle / ring / little = left hand / right hand /
    ///     left foot / right foot), or
    ///   - point the right hand's ray straight at the part and pinch.
    /// While it's held, it follows where the right hand points: onto the surface the ray hits (a foot lands
    /// there, a hand rests there), or, pointing at nothing, along the ray at the distance it was picked up.
    /// Let go of the pinch to set it down. It moves at most StringFlick.placeSpeed and never further than
    /// the limb reaches; pointing beyond that pulls the body over toward it.
    ///
    /// Both control crosses hold still while a part is held, so the lean doesn't change.
    /// </summary>
    [RequireComponent(typeof(StringFlick))]
    public class PartPlacer : MonoBehaviour
    {
        [Tooltip("Longest pointing ray, in puppet heights.")]
        public float maxDistance = 12f;
        public Color rayColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        PuppetSpawner m_Spawner;
        StringFlick m_Flick;
        XRHandsControlInput m_Hands;
        LineRenderer m_Ray;
        bool m_ByRight, m_WasRightPinching;
        float m_PickDistance;

        /// <summary>Tests and scripted demos: point here instead of the right hand (null = use the hand).</summary>
        [System.NonSerialized] public Ray? aimOverride;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            m_Flick = GetComponent<StringFlick>();
            m_Hands = GetComponent<XRHandsControlInput>();
            var go = new GameObject("PlacementRay");
            go.transform.SetParent(transform, false);
            m_Ray = go.AddComponent<LineRenderer>();
            m_Ray.useWorldSpace = true;
            m_Ray.positionCount = 2;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) m_Ray.sharedMaterial = new Material(shader);
            m_Ray.startColor = rayColor;
            m_Ray.endColor = new Color(rayColor.r, rayColor.g, rayColor.b, 0.2f);
            m_Ray.enabled = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            foreach (var flick in FindObjectsByType<StringFlick>(FindObjectsSortMode.None))
                if (flick.GetComponent<PartPlacer>() == null) flick.gameObject.AddComponent<PartPlacer>();
        }

        bool PointPlaceMode => m_Hands != null && m_Hands.fingerAction == XRHandsControlInput.FingerAction.PointPlace;

        Ray? Aim => aimOverride ?? (m_Hands != null && m_Hands.RightAimValid ? m_Hands.RightAim : (Ray?)null);

        void FixedUpdate()
        {
            if (m_Hands == null) return; // keyboard or scripted input: holds are driven from code
            var rig = m_Spawner.rig;
            if (rig == null || !m_Flick.isActiveAndEnabled || !PointPlaceMode) { Release(); return; }
            bool rightPinch = m_Hands.RightPinching;
            bool rightStarted = rightPinch && !m_WasRightPinching;
            m_WasRightPinching = rightPinch;
            PuppetNodeId? left = m_Hands.HeldTarget;

            if (!m_Flick.Holding)
            {
                if (left.HasValue) Begin(rig, left.Value, byRight: false);
                else if (rightStarted && Aim.HasValue && PartUnder(rig, Aim.Value, out PuppetNodeId part)) Begin(rig, part, byRight: true);
                return;
            }

            bool still = m_ByRight ? rightPinch : left.HasValue && left.Value == m_Flick.HeldPart;
            if (!still) { Release(); return; }
            if (Aim.HasValue) m_Flick.SetHoldGoal(GoalFor(rig, Aim.Value));
        }

        void Begin(MarionetteRig rig, PuppetNodeId part, bool byRight)
        {
            if (!m_Flick.BeginHold(part)) return;
            m_ByRight = byRight;
            m_Hands.externalFreeze = byRight;
            Vector3 at = rig.Node(part).Body.worldCenterOfMass;
            m_PickDistance = Aim.HasValue ? Vector3.Distance(Aim.Value.origin, at) : 0f;
        }

        void Release()
        {
            if (m_Flick != null && m_Flick.Holding) m_Flick.EndHold();
            if (m_Hands != null) m_Hands.externalFreeze = false;
            m_ByRight = false;
            if (m_Ray != null) m_Ray.enabled = false;
        }

        /// <summary>A hand or foot of this puppet under the ray (the closest thing it hits).</summary>
        public bool PartUnder(MarionetteRig rig, Ray ray, out PuppetNodeId part)
        {
            part = default;
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance * rig.height, ~0, QueryTriggerInteraction.Ignore)) return false;
            var node = hit.collider.GetComponentInParent<PuppetNode>();
            if (node == null || hit.collider.GetComponentInParent<MarionetteRig>() != rig) return false;
            if (node.id is not (PuppetNodeId.LeftHand or PuppetNodeId.RightHand or PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot)) return false;
            part = node.id;
            return true;
        }

        /// <summary>
        /// Where the held part should go for this ray: resting on the first surface it hits (not the puppet), or
        /// along the ray at the distance it was picked up from.
        /// </summary>
        public Vector3 GoalFor(MarionetteRig rig, Ray ray)
        {
            var body = rig.Node(m_Flick.HeldPart).Body;
            var col = body.GetComponent<Collider>();
            float lift = col != null ? col.bounds.extents.y : 0.05f * rig.height;
            Vector3 goal = ray.origin + ray.direction * Mathf.Max(m_PickDistance, 0.3f * rig.height);
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, maxDistance * rig.height, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<MarionetteRig>() != null || hit.distance >= best) continue;
                best = hit.distance;
                goal = hit.point + hit.normal * lift; // resting on the surface
            }
            if (m_Ray != null)
            {
                m_Ray.enabled = true;
                m_Ray.widthMultiplier = 0.02f * rig.height;
                m_Ray.SetPosition(0, ray.origin);
                m_Ray.SetPosition(1, goal);
            }
            return goal;
        }
    }
}

using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Every physics feel parameter of the puppet in one inspector. Changes apply live in Play mode,
    /// so tune while puppeteering. Pick a preset to compare the three planned prototypes:
    ///   A Textile  - default sparse cloth body, sleeves fold softly
    ///   B Stiff    - near-rigid links, reads like an articulated puppet
    ///   C Loose    - very slack cloth, floppy ragdoll-like limbs
    /// All values are authored at gnome size (MarionetteBuilder.ReferenceHeight) and scaled to the
    /// puppet's real size: lengths x s, masses x s^3, springs x s^2, dampers x s^2.5. That keeps the
    /// same proportional stretch and damping ratio at any size, so one tuning works mouse to gnome.
    /// Remember: values changed in Play mode are lost on exit. Right-click the component
    /// > Copy Component, then paste values after stopping, to keep a result.
    /// </summary>
    [RequireComponent(typeof(MarionetteRig))]
    public class MarionetteTuning : MonoBehaviour
    {
        public enum Preset { A_Textile, B_Stiff, C_Loose, Custom }

        [Tooltip("Choosing A/B/C overwrites the values below. Edit any value to get your own Custom setup.")]
        public Preset preset = Preset.A_Textile;

        [Header("Simulation")]
        [Tooltip("Physics step. 1/90 matches Quest refresh and keeps joints stable. Project default is 1/50.")]
        public float fixedTimestep = 1f / 90f;
        [Range(4, 50)] public int solverIterations = 25;
        [Range(1, 20)] public int solverVelocityIterations = 10;
        [Tooltip("Small things falling at full gravity look too fast. Below 1 gives the floaty marionette feel.")]
        [Range(0.3f, 1.5f)] public float gravityScale = 0.85f;
        [Tooltip("Off = puppet parts pass through each other (stable). On = hands bump into the body.")]
        public bool selfCollision = false;

        [Header("Hands")]
        [Tooltip("Puppet metres moved per metre of real hand motion. 1 = true 1:1 puppeteering.")]
        public float handMotionGain = 1f;

        [Header("Mass (kg at gnome size)")]
        public float headMass = 0.6f;
        public float centerMass = 2.0f;
        public float handMass = 0.3f;
        public float footMass = 0.5f;

        [Header("Damping (air drag, swing decay)")]
        [Range(0f, 2f)] public float linearDamping = 0.1f;
        [Range(0f, 5f)] public float angularDamping = 0.8f;
        [Tooltip("Feet get extra angular damping so they don't flip over when lifted.")]
        [Range(0f, 10f)] public float footAngularDamping = 3f;

        [Header("Textile")]
        [Tooltip("How far the cloth stretches past its built length before it stops.")]
        [Range(0f, 0.5f)] public float clothSlack = 0.05f;
        public float clothStretchSpring = 3000f;
        public float clothStretchDamper = 40f;
        [Tooltip("Cloth starts resisting compression below this fraction of its built length.")]
        [Range(0f, 1f)] public float clothMinLength = 0.6f;
        public float clothCompressStiffness = 40f;
        public float clothCompressDamper = 2f;

        [Header("Strings")]
        public float stringSpring = 4000f;
        public float stringDamper = 60f;

        [Header("Finger lift range (m at full finger press)")]
        public float handLift = 0.22f;
        public float footLift = 0.14f;

        [Header("Limbs: arm and leg cloth that pushes back")]
        [Tooltip("With no body string, the legs hold the body up and the arms keep the hands spread. " +
                 "Arm and leg cloth resists being squashed below this fraction of its built length.")]
        [Range(0f, 1f)] public float limbMinLength = 0.92f;
        [Tooltip("How hard squashed arm and leg cloth pushes back (N/m at gnome size).")]
        public float limbCompressStiffness = 2000f;
        public float limbCompressDamper = 30f;

        [Header("Limbs: hands and feet are sprung to the body")]
        [Tooltip("How hard a hand or foot moved away from its usual place pulls the body along (N/m at gnome size). " +
                 "The legs' spring is also what holds the body up on its feet.")]
        public float limbSpring = 1500f;
        public float limbDamper = 25f;
        [Tooltip("Furthest a hand or foot can get from its usual place, in puppet heights.")]
        [Range(0.05f, 1f)] public float limbMaxStretch = 0.35f;
        [Tooltip("Ankle stiffness: how strongly the feet hold the body upright over them (N·m/rad at gnome size). " +
                 "A few times what gravity needs, like a real stance: a push leans the body, and steps catch it.")]
        public float ankleStiffness = 15f;
        public float ankleDamper = 1f;
        [Tooltip("Most torque each ankle can use to hold the body at its lean (N·m at gnome size). Momentum beyond " +
                 "what this can hold tips the whole puppet over its feet: a step or a counter-lean is needed.")]
        public float ankleMaxTorque = 2f;
        [Tooltip("Extra stretch allowed in arm and leg cloth, so it doesn't stop steps and reaches short. The limb springs do the holding.")]
        [Range(0f, 1f)] public float limbClothSlack = 0.5f;
        [Tooltip("Wrist stiffness: how strongly the hands keep their angle to the body (N·m/rad at gnome size).")]
        public float wristStiffness = 2f;
        public float wristDamper = 0.1f;

        [Header("Limbs: rotation limits so hands and feet don't spin")]
        [Tooltip("How far a hand or foot can tip forward/back relative to the body, degrees.")]
        [Range(5f, 90f)] public float limbPitchLimit = 40f;
        [Tooltip("How far a hand or foot can turn left/right relative to the body, degrees.")]
        [Range(5f, 90f)] public float limbYawLimit = 35f;
        [Tooltip("How far a hand or foot can roll sideways relative to the body, degrees.")]
        [Range(5f, 90f)] public float limbRollLimit = 35f;
        [Tooltip("Fastest a hand or foot may spin, turns per second.")]
        [Range(0.5f, 10f)] public float limbMaxSpin = 2f;
        [Tooltip("How far the head can nod and turn on the body, degrees.")]
        [Range(0f, 45f)] public float neckLimit = 15f;

        MarionetteRig m_Rig;
        // Serialized so reopening the scene doesn't reload the preset over tuned values.
        [SerializeField, HideInInspector] Preset m_AppliedPreset = (Preset)(-1);

        // Bumped when defaults change in a way puppets already saved in scenes should pick up.
        const int k_Version = 2;
        [SerializeField, HideInInspector] int m_Version;

        void Upgrade()
        {
            if (m_Version >= k_Version) return;
            // 2: looser, quicker to fall (softer ankles, less limb damping)
            ankleStiffness = 15f;
            ankleMaxTorque = 2f;
            limbDamper = 25f;
            m_Version = k_Version;
        }

        void Awake()
        {
            m_Rig = GetComponent<MarionetteRig>();
            Upgrade();
            Apply();
        }

        void OnValidate()
        {
            if (preset != m_AppliedPreset && preset != Preset.Custom)
                LoadPreset(preset);
            m_AppliedPreset = preset;
            if (Application.isPlaying && m_Rig != null) Apply();
        }

        void LoadPreset(Preset p)
        {
            switch (p)
            {
                case Preset.A_Textile:
                    clothSlack = 0.05f; clothStretchSpring = 3000f; clothStretchDamper = 40f;
                    clothMinLength = 0.6f; clothCompressStiffness = 40f; clothCompressDamper = 2f;
                    angularDamping = 0.8f; linearDamping = 0.1f;
                    break;
                case Preset.B_Stiff:
                    clothSlack = 0f; clothStretchSpring = 8000f; clothStretchDamper = 120f;
                    clothMinLength = 0.95f; clothCompressStiffness = 600f; clothCompressDamper = 25f;
                    angularDamping = 1.5f; linearDamping = 0.15f;
                    break;
                case Preset.C_Loose:
                    clothSlack = 0.3f; clothStretchSpring = 1500f; clothStretchDamper = 20f;
                    clothMinLength = 0f; clothCompressStiffness = 0f; clothCompressDamper = 0f;
                    angularDamping = 0.4f; linearDamping = 0.05f;
                    break;
            }
        }

        public void Apply()
        {
            if (m_Rig == null || m_Rig.nodes == null) return;
            Time.fixedDeltaTime = fixedTimestep;
            float s = m_Rig.scale;
            float massScale = s * s * s;
            float springScale = s * s;
            float damperScale = s * s * Mathf.Sqrt(s);

            foreach (var node in m_Rig.nodes)
            {
                var rb = node.Body;
                bool foot = node.id == PuppetNodeId.LeftFoot || node.id == PuppetNodeId.RightFoot;
                rb.mass = massScale * node.id switch
                {
                    PuppetNodeId.Head => headMass,
                    PuppetNodeId.Center => centerMass,
                    PuppetNodeId.LeftHand or PuppetNodeId.RightHand => handMass,
                    _ => footMass
                };
                rb.linearDamping = linearDamping;
                rb.angularDamping = foot ? footAngularDamping : angularDamping;
                rb.solverIterations = solverIterations;
                rb.solverVelocityIterations = solverVelocityIterations;
                // Joints to a moving kinematic handle don't reliably wake a sleeping body.
                rb.sleepThreshold = 0f;
                // Overlapping parts push apart gently instead of exploding.
                rb.maxDepenetrationVelocity = 1f * s;
            }

            foreach (var link in m_Rig.textile)
            {
                bool limb = IsLimb(link.from.id) || IsLimb(link.to.id);
                link.SetStretch(limb ? limbClothSlack : clothSlack, clothStretchSpring * springScale, clothStretchDamper * damperScale);
                link.minLengthRatio = limb ? limbMinLength : clothMinLength;
                link.compressStiffness = (limb ? limbCompressStiffness : clothCompressStiffness) * springScale;
                link.compressDamper = (limb ? limbCompressDamper : clothCompressDamper) * damperScale;
            }

            foreach (var cross in new[] { m_Rig.leftCross, m_Rig.rightCross })
            {
                if (cross == null) continue;
                cross.translationGain = handMotionGain;
                foreach (var c in cross.channels)
                {
                    c.controlString.SetElasticity(stringSpring * springScale, stringDamper * damperScale);
                    c.liftRange = s * c.controlString.attachment.node.id switch
                    {
                        PuppetNodeId.LeftHand or PuppetNodeId.RightHand => handLift,
                        _ => footLift
                    };
                }
            }

            if (m_Rig.limbJoints != null)
                foreach (var j in m_Rig.limbJoints)
                {
                    if (j == null) continue;
                    SetAngularLimits(j, limbPitchLimit, limbYawLimit, limbRollLimit);
                    j.linearLimit = new SoftJointLimit { limit = limbMaxStretch * m_Rig.height };
                    var drive = new JointDrive { positionSpring = limbSpring * springScale, positionDamper = limbDamper * damperScale, maximumForce = float.MaxValue };
                    j.xDrive = j.yDrive = j.zDrive = drive;

                    // Torque springs scale with s^4 (gravity torque = mass s^3 x lever s), dampers with s^4.5.
                    bool foot = j.GetComponent<PuppetNode>().id is PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot;
                    float s4 = springScale * springScale, s45 = s4 * Mathf.Sqrt(s);
                    j.rotationDriveMode = RotationDriveMode.Slerp;
                    j.slerpDrive = new JointDrive
                    {
                        positionSpring = (foot ? ankleStiffness : wristStiffness) * s4,
                        positionDamper = (foot ? ankleDamper : wristDamper) * s45,
                        maximumForce = foot ? ankleMaxTorque * s4 : float.MaxValue
                    };
                    j.GetComponent<Rigidbody>().maxAngularVelocity = limbMaxSpin * 2f * Mathf.PI;
                }
            if (m_Rig.neckJoint != null) SetAngularLimits(m_Rig.neckJoint, neckLimit, neckLimit, neckLimit);

            ApplySelfCollision();
        }

        static bool IsLimb(PuppetNodeId id) => id != PuppetNodeId.Head && id != PuppetNodeId.Center;

        static void SetAngularLimits(ConfigurableJoint j, float pitch, float yaw, float roll)
        {
            // Joint axes are the part's own: X = right (pitch), Y = up (yaw), Z = forward (roll).
            j.lowAngularXLimit = new SoftJointLimit { limit = -pitch };
            j.highAngularXLimit = new SoftJointLimit { limit = pitch };
            j.angularYLimit = new SoftJointLimit { limit = yaw };
            j.angularZLimit = new SoftJointLimit { limit = roll };
        }

        void ApplySelfCollision()
        {
            var nodes = m_Rig.nodes;
            for (int i = 0; i < nodes.Length; i++)
            for (int j = i + 1; j < nodes.Length; j++)
            {
                var a = nodes[i].GetComponent<Collider>();
                var b = nodes[j].GetComponent<Collider>();
                if (a != null && b != null) Physics.IgnoreCollision(a, b, !selfCollision);
            }
        }

        void FixedUpdate()
        {
            // Hands and feet can't spin: maxAngularVelocity alone doesn't hold against joint impulses.
            if (m_Rig.limbJoints != null)
            {
                float maxSpin = limbMaxSpin * 2f * Mathf.PI;
                foreach (var j in m_Rig.limbJoints)
                {
                    if (j == null) continue;
                    var rb = j.GetComponent<Rigidbody>();
                    if (rb.angularVelocity.sqrMagnitude > maxSpin * maxSpin)
                        rb.angularVelocity = rb.angularVelocity.normalized * maxSpin;
                }
            }

            if (Mathf.Approximately(gravityScale, 1f)) return;
            Vector3 extra = Physics.gravity * (gravityScale - 1f);
            foreach (var node in m_Rig.nodes)
                node.Body.AddForce(extra, ForceMode.Acceleration);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Pinch & flick, the applying half. When the left pinch is released, the selected body part is
    /// driven along the recorded right index path, at the speed it was drawn: a delayed effect.
    ///
    /// The part is dragged along the path as if someone pulled it from the other end: it goes loose from
    /// the body and follows the drawing, position and speed. The rest of the body is yanked after it by a
    /// pulse: each other part gets a short push the way the arc went as the drag starts, the body first
    /// and the head and other limbs a moment later. Faster and longer arcs give a
    /// harder pulse. A careless drag sends the puppet stumbling (BalanceMonitor).
    ///
    /// Also draws the feedback: the selected string turns yellow, and the right index path is drawn
    /// as a trail while recording (green, then grey once the record window is full).
    /// </summary>
    [RequireComponent(typeof(PuppetSpawner))]
    public class StringFlick : MonoBehaviour
    {
        [Tooltip("Puppet movement per unit of hand movement, after the player's scale. 1 = the part follows your drawing 1:1.")]
        [Range(0.2f, 5f)] public float forceGain = 0.7f;
        [Tooltip("Furthest a dragged hand or foot can get from its shoulder or hip, as a multiple of the arm's or leg's length. " +
                 "The drawing is clamped to this, so a big wave doesn't stretch the limb out.")]
        [Range(1f, 2f)] public float reach = 1.15f;
        [Tooltip("How tightly the part follows the drawn path, Hz. Higher = snappier, harsher.")]
        [Range(1f, 20f)] public float response = 6f;
        [Tooltip("Strongest push allowed, in puppet heights per second squared. Stops tracking glitches launching the puppet.")]
        public float maxAcceleration = 300f;
        [Tooltip("While a hand is dragged, its arm still carries its share of the body's weight, but can't pull the body along: " +
                 "only the drag pulse does that. Off: the arm lets go of the body completely. (A dragged foot always lets go.)")]
        public bool keepSupport = true;
        [Tooltip("Seconds for the limb to be drawn back to its usual place after a flick. The spring's rest point starts " +
                 "where the limb was let go and eases home, so the light limb comes back to the body instead of yanking the body over.")]
        public float reattachSeconds = 0.6f;

        [Header("Drag pulse (the rest of the body is yanked after the dragged limb)")]
        [Tooltip("Body speed given per unit of drawn speed, at full range. The pulse goes the way the arc went.")]
        [Range(0f, 3f)] public float momentumGain = 0.6f;
        [Tooltip("Dragging a hand pushes the body this much compared with dragging a foot. Low: waving the arms sways the " +
                 "puppet but doesn't walk it; the feet do the walking.")]
        [Range(0f, 1f)] public float handPulse = 0.2f;
        [Tooltip("How long each body part's pulse lasts, seconds (gnome size; smaller puppets scale it down).")]
        [Range(0.02f, 0.5f)] public float pulseSeconds = 0.12f;
        [Tooltip("Delay per link between the dragged limb and a body part, seconds: the yank travels through the body.")]
        [Range(0f, 0.2f)] public float pulseHopDelay = 0.04f;
        [Tooltip("Share of the pulse each part gets. The head sits high, so its share tips the puppet over more than it moves it.")]
        [Range(0f, 2f)] public float bodyShare = 1f, headShare = 0.5f, limbShare = 1f;
        [Tooltip("Each link further along the body from the dragged limb gets this fraction of the previous one's pulse " +
                 "(dragged limb -> body -> head and the other limbs).")]
        [Range(0f, 1f)] public float chainFalloff = 0.6f;
        [Tooltip("Seconds after letting go that the other arms stay loose, so they swing along with the drag instead of holding stiff.")]
        [Range(0f, 5f)] public float trailSeconds = 2f;
        [Tooltip("How stiff the other arms are while loose, as a fraction of normal.")]
        [Range(0.02f, 1f)] public float trailLooseness = 0.2f;
        [Tooltip("How hard the loose arms are swung along by a drag, compared with the body's pulse before the chain falloff.")]
        [Range(0f, 5f)] public float trailSwing = 5f;
        [Tooltip("Arc length, in puppet heights, that counts as full range. Shorter arcs push proportionally less.")]
        [Range(0.05f, 2f)] public float fullRange = 0.5f;
        [Tooltip("Seconds between releasing the pinch and the flick starting.")]
        [Range(0f, 1f)] public float delay = 0f;
        [Tooltip("The string carries the part's weight during the flick. Without it, ground friction pins a small puppet's feet.")]
        public bool carryWeight = true;

        [Header("Feedback")]
        public Color recordingColor = new Color(0.4f, 1f, 0.5f);
        public Color fullColor = new Color(0.6f, 0.6f, 0.6f);
        public Color expiredColor = new Color(1f, 0.3f, 0.2f);

        PuppetSpawner m_Spawner;
        XRHandsControlInput m_Hands;
        LineRenderer m_Trail;

        // Active replay
        PuppetNodeId m_Target;
        float[] m_Times;
        Vector3[] m_Path;
        Vector3[] m_Velocities;
        Vector3 m_Start;
        bool m_Active, m_Started;
        float m_Clock;

        // The flicked limb's joint spring, loosened during the flick and eased back afterwards.
        ConfigurableJoint m_LimbJoint;
        JointDrive m_LimbDrive;
        Vector3 m_RestAnchor, m_ReleaseAnchor, m_SupportOffset;
        TextileLink m_LooseCloth;
        float m_Reattach = -1f;

        // Body speed this flick gives, and the pulses carrying it to each other body part.
        Vector3 m_Momentum;   // for the body, head and feet (small for a hand drag)
        Vector3 m_Swing;      // for the other, loose arms: the full drag, so they visibly swing along
        readonly List<(ConfigurableJoint joint, JointDrive drive, JointDrive slerp, Vector3 support)> m_Trailing = new List<(ConfigurableJoint, JointDrive, JointDrive, Vector3)>();
        float m_TrailFraction = 1f;
        float m_TrailClock = -1f, m_TrailLength;
        struct Pulse { public Rigidbody body; public float start, length; public Vector3 dv; }
        readonly List<Pulse> m_Pulses = new List<Pulse>();
        float m_PulseClock;

        /// <summary>Upper-body speed change the last flick gave (world m/s), for tests and UI.</summary>
        public Vector3 LastMomentum { get; private set; }

        /// <summary>True from release until the flick has been applied (including the delay).</summary>
        public bool Playing => m_Active;

        void Awake()
        {
            m_Spawner = GetComponent<PuppetSpawner>();
            m_Hands = GetComponent<XRHandsControlInput>();
            if (m_Hands != null) m_Hands.FlickReleased += Play;

            var go = new GameObject("FlickTrail");
            go.transform.SetParent(transform, false);
            m_Trail = go.AddComponent<LineRenderer>();
            m_Trail.useWorldSpace = true;
            m_Trail.enabled = false;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) m_Trail.sharedMaterial = new Material(shader);
        }

        void OnDestroy()
        {
            if (m_Hands != null) m_Hands.FlickReleased -= Play;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var hands = FindAnyObjectByType<XRHandsControlInput>();
            if (hands == null || hands.GetComponent<StringFlick>() != null || hands.GetComponent<PuppetSpawner>() == null) return;
            hands.gameObject.AddComponent<StringFlick>();
        }

        /// <summary>Apply a recorded flick. Also callable from code (tests, AI puppeteers).</summary>
        public void Play(XRHandsControlInput.FlickTake take)
        {
            int n = take.path.Length;
            if (n < 3) return;
            var rig = m_Spawner.rig;
            var balance = rig != null ? rig.GetComponent<BalanceMonitor>() : null;
            if (balance != null && balance.ControlsOverridden) return; // tripping or toppling: no input
            m_Target = take.target;

            // Momentum: the way the arc went (horizontally), as strong as it was fast and long.
            float duration = Mathf.Max(1e-3f, take.times[n - 1] - take.times[0]);
            float length = 0f;
            for (int i = 1; i < n; i++) length += (take.path[i] - take.path[i - 1]).magnitude;
            Vector3 net = take.path[n - 1] - take.path[0];
            net.y = 0f;
            float h = rig != null ? rig.height : 0.12f;
            float speed = length / duration * forceGain;
            float range = Mathf.Clamp01(length * forceGain / (fullRange * h));
            bool hand = take.target is PuppetNodeId.LeftHand or PuppetNodeId.RightHand;
            // Scaled by how far the drag actually went, not the path length: waving back and forth goes nowhere.
            float directness = length > 1e-6f ? Mathf.Clamp01(net.magnitude / length) : 0f;
            m_Swing = net.sqrMagnitude > 1e-10f ? net.normalized * (momentumGain * speed * range * directness) : Vector3.zero;
            m_Momentum = m_Swing * (hand ? handPulse : 1f);
            LastMomentum = m_Momentum;
            m_Times = new float[n];
            m_Path = new Vector3[n];
            m_Velocities = new Vector3[n];
            float t0 = take.times[0];
            for (int i = 0; i < n; i++)
            {
                m_Times[i] = take.times[i] - t0;
                m_Path[i] = take.path[i] * forceGain;
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                float dt = take.times[b] - take.times[a];
                m_Velocities[i] = dt > 1e-4f ? (take.path[b] - take.path[a]) * forceGain / dt : Vector3.zero;
            }
            m_Clock = -delay;
            m_Active = true;
            m_Started = false;
            RestoreNow(); // finish any previous drag first, so its limb's drives are back to normal
            SchedulePulses(rig);
            LoosenOtherArms(rig, delay + duration + trailSeconds);
        }

        /// <summary>
        /// Like someone yanking the limb from the other end: every other body part gets a short pulse the way
        /// the arc went, as the drag starts, a little later the further it is from the limb.
        /// </summary>
        void SchedulePulses(MarionetteRig rig)
        {
            m_Pulses.Clear();
            m_PulseClock = 0f;
            if (rig == null || m_Swing.sqrMagnitude < 1e-12f) return;
            float slow = Mathf.Sqrt(rig.scale);
            float length = pulseSeconds * slow;
            float at = delay; // the yank starts as the drag does
            foreach (var node in rig.nodes)
            {
                if (node.id == m_Target) continue;
                int hops = node.id == PuppetNodeId.Center ? 1 : 2; // every part hangs off the body
                float share = node.id switch { PuppetNodeId.Center => bodyShare, PuppetNodeId.Head => headShare, _ => limbShare };
                share *= Mathf.Pow(chainFalloff, hops - 1); // weaker the further along the chain
                if (share <= 0f) continue;
                bool looseArm = node.id is PuppetNodeId.LeftHand or PuppetNodeId.RightHand;
                Vector3 dv = (looseArm ? m_Swing * trailSwing : m_Momentum) * share;
                m_Pulses.Add(new Pulse { body = node.Body, start = at + hops * pulseHopDelay * slow, length = length, dv = dv });
            }
        }

        void ApplyPulses()
        {
            if (m_Pulses.Count == 0) return;
            float t = m_PulseClock;
            m_PulseClock += Time.fixedDeltaTime;
            for (int i = m_Pulses.Count - 1; i >= 0; i--)
            {
                var p = m_Pulses[i];
                if (p.body == null || t >= p.start + p.length) { m_Pulses.RemoveAt(i); continue; }
                if (t >= p.start) p.body.AddForce(p.dv / p.length, ForceMode.Acceleration);
            }
        }

        /// <summary>The arms not being dragged go loose for a while, so the drag swings them along.</summary>
        void LoosenOtherArms(MarionetteRig rig, float seconds)
        {
            RestoreTrailing();
            if (rig == null || rig.limbJoints == null || trailLooseness >= 1f || seconds <= 0f) return;
            foreach (var j in rig.limbJoints)
            {
                if (j == null) continue;
                var id = j.GetComponent<PuppetNode>().id;
                if (id == m_Target || id is not (PuppetNodeId.LeftHand or PuppetNodeId.RightHand)) continue; // legs hold it up
                // The arm carries part of the body from its hand string; the support it gave at rest is kept while it's loose.
                m_Trailing.Add((j, j.xDrive, j.slerpDrive, j.connectedAnchor - CurrentAnchor(j)));
            }
            m_TrailClock = 0f;
            m_TrailLength = seconds;
            SetTrailing(trailLooseness);
        }

        void SetTrailing(float fraction)
        {
            m_TrailFraction = fraction;
            foreach (var (j, d, a, _) in m_Trailing)
            {
                if (j == null) continue;
                var x = d; x.positionSpring *= fraction; x.positionDamper *= fraction;
                j.xDrive = j.yDrive = j.zDrive = x;
                var r = a; r.positionSpring *= fraction; r.positionDamper *= fraction;
                j.slerpDrive = r;
            }
        }

        void RestoreTrailing()
        {
            SetTrailing(1f);
            m_Trailing.Clear();
            m_TrailClock = -1f;
        }

        void UpdateTrailing()
        {
            if (m_TrailClock < 0f) return;
            var balance = m_Spawner.rig != null ? m_Spawner.rig.GetComponent<BalanceMonitor>() : null;
            if (balance != null && balance.Current == BalanceMonitor.State.Falling)
            {
                m_Trailing.Clear(); m_TrailClock = -1f; // limp anyway; recovery puts the tuning back
                return;
            }
            m_TrailClock += Time.fixedDeltaTime;
            foreach (var (j, d, _, support) in m_Trailing)
            {
                if (j == null || j.connectedBody == null) continue;
                var torso = j.connectedBody;
                Vector3 f = torso.transform.TransformVector(support) * (d.positionSpring * (1f - m_TrailFraction));
                j.GetComponent<Rigidbody>().AddForce(f, ForceMode.Force);
                torso.AddForceAtPosition(-f, torso.transform.TransformPoint(j.connectedAnchor), ForceMode.Force);
            }
            float ease = Mathf.Clamp01((m_TrailClock - (m_TrailLength - 0.5f)) / 0.5f); // stiffen back over the last half second
            if (m_TrailClock >= m_TrailLength) RestoreTrailing();
            else if (ease > 0f) SetTrailing(Mathf.Lerp(trailLooseness, 1f, ease));
        }

        void FixedUpdate()
        {
            UpdateTrailing();
            if (m_Reattach >= 0f) Reattach();
            ApplyPulses();
            if (!m_Active) return;
            if (m_Clock < 0f)
            {
                m_Clock += Time.fixedDeltaTime; // waiting out the delay
                return;
            }

            var rig = m_Spawner.rig;
            float duration = m_Times[m_Times.Length - 1];
            var balance = rig != null ? rig.GetComponent<BalanceMonitor>() : null;
            if (rig == null || m_Clock > duration || balance != null && balance.ControlsOverridden)
            {
                Stop();
                return;
            }

            // Pushed through its centre of mass, so the flick moves the part without spinning it.
            Rigidbody body = rig.Node(m_Target).Body;
            Vector3 point = body.worldCenterOfMass;
            if (!m_Started)
            {
                m_Start = point;
                m_Started = true;
                Loosen(rig);
            }
            // The limb keeps carrying the share of the body it held at rest, wherever it's dragged: the spring's
            // force at rest, applied directly (moving the spring's anchor along would lag a step and yank the body).
            ApplySupport();

            // Follow the drawn path, position and speed. Drawing faster means harder acceleration, so a
            // harder push and more momentum. The cloth between this limb and the body drags the body along.
            float omega = 2f * Mathf.PI * response;
            Vector3 goal = m_Start + Sample(m_Path, m_Clock);
            Vector3 goalVelocity = Sample(m_Velocities, m_Clock);
            ClampToReach(point, ref goal, ref goalVelocity);
            Vector3 error = goal - point;
            Vector3 speedError = goalVelocity - body.linearVelocity;
            Vector3 accel = error * (omega * omega) + speedError * (2f * omega);
            accel = Vector3.ClampMagnitude(accel, maxAcceleration * rig.height);
            Vector3 force = accel * body.mass;
            if (carryWeight)
            {
                // Cancel gravity on this part (including MarionetteTuning's gravity scale), like the
                // puppeteer taking its weight on the string while moving it.
                var tuning = rig.GetComponent<MarionetteTuning>();
                force -= Physics.gravity * ((tuning != null ? tuning.gravityScale : 1f) * body.mass);
            }
            body.AddForceAtPosition(force, point, ForceMode.Force);
            // The rest of the body is yanked along by the drag pulses (ApplyPulses).

            m_Clock += Time.fixedDeltaTime;
        }

        // The dragged limb's cloth: its body end (shoulder or hip), its limb end, and the limb's length.
        PuppetAttachment m_ReachRoot, m_ReachTip;
        float m_ReachLength;

        /// <summary>Keep the dragged limb within reach of its shoulder or hip: the arm or leg can't stretch out.</summary>
        void ClampToReach(Vector3 point, ref Vector3 goal, ref Vector3 goalVelocity)
        {
            if (m_ReachRoot == null || m_ReachTip == null) return;
            float max = m_ReachLength * reach;
            Vector3 tipOffset = m_ReachTip.transform.position - point;
            Vector3 root = m_ReachRoot.transform.position;
            Vector3 d = goal + tipOffset - root;
            if (d.magnitude <= max) return;
            Vector3 dir = d.normalized;
            goal = root + dir * max - tipOffset;
            float outward = Vector3.Dot(goalVelocity, dir);
            if (outward > 0f) goalVelocity -= dir * outward; // slide along the edge of reach, don't push past it
        }

        void Loosen(MarionetteRig rig)
        {
            RestoreNow();
            // The limb's cloth goes loose too, so the body isn't yanked at the limb's speed: it gets the
            // flick's calculated momentum, then follows the limb as the spring returns.
            foreach (var link in rig.textile)
                if (link.from.id == m_Target || link.to.id == m_Target)
                {
                    link.SetLoose(true);
                    m_LooseCloth = link;
                    bool fromBody = link.to.id == m_Target;
                    m_ReachRoot = fromBody ? link.fromPoint : link.toPoint;
                    m_ReachTip = fromBody ? link.toPoint : link.fromPoint;
                    m_ReachLength = link.restLength;
                }
            var steps = rig.GetComponent<AutoStep>();
            if (steps != null) steps.SetHeld(FootIndex(m_Target), true);
            if (rig.limbJoints == null) return;
            foreach (var j in rig.limbJoints)
            {
                if (j == null || j.GetComponent<PuppetNode>().id != m_Target) continue;
                m_LimbJoint = j;
                m_LimbDrive = j.xDrive;
                m_RestAnchor = j.connectedAnchor;
                m_SupportOffset = m_RestAnchor - CurrentAnchor(j);
                SetLimbSpring(0f, 0f); // the spring and damper would drag the body at the limb's speed (keepSupport stands in for the spring's hold)
                // Rotation limit off while flicked: the string twisting the limb against its limit would
                // pitch the whole body over. The spin cap (MarionetteTuning) still applies.
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
                // Stretch limit off too: hitting it would jerk the body along at the limb's speed.
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
            }
        }

        /// <summary>The share of the body the limb held at rest, applied directly while its spring is off.</summary>
        void ApplySupport()
        {
            if (!KeepsSupport || m_LimbJoint == null) return;
            var torso = m_LimbJoint.connectedBody;
            Vector3 support = torso.transform.TransformVector(m_SupportOffset) * m_LimbDrive.positionSpring;
            m_LimbJoint.GetComponent<Rigidbody>().AddForce(support, ForceMode.Force);
            // At the shoulder or hip, where the spring acts: at the body's centre it would leave the other legs' push
            // off-centre and roll the puppet over (standing on one leg while the other is dragged).
            torso.AddForceAtPosition(-support, torso.transform.TransformPoint(m_RestAnchor), ForceMode.Force);
        }

        bool IsHand => m_Target is PuppetNodeId.LeftHand or PuppetNodeId.RightHand;
        static int FootIndex(PuppetNodeId id) => id == PuppetNodeId.LeftFoot ? 0 : id == PuppetNodeId.RightFoot ? 1 : -1;
        // Hands only: a lifted foot can't hold the body up (and pressing it down pins it to the floor).
        bool KeepsSupport => keepSupport && IsHand;

        /// <summary>
        /// After a flick. A hand returns to its usual place by itself (pushing on the body would let its string, now
        /// leaning back toward the cross, pull the puppet over), then its spring takes over again. A foot stays where
        /// it was put: its spring comes back while the rest point eases home, so the body follows it. Either way the
        /// cloth tightens again once the limb is back within it.
        /// </summary>
        void Reattach()
        {
            if (m_LimbJoint == null) { RestoreNow(); return; }
            var balance = m_Spawner.rig != null ? m_Spawner.rig.GetComponent<BalanceMonitor>() : null;
            if (!IsHand && balance != null && balance.ControlsOverridden) { RestoreNow(); return; } // stumbling: the foot is the legs' again
            m_Reattach += Time.fixedDeltaTime;
            float k = reattachSeconds > 0f ? Mathf.Clamp01(m_Reattach / reattachSeconds) : 1f;
            if (IsHand)
            {
                if (ReturnHand() && m_Reattach < reattachSeconds * 3f) return;
                m_LimbJoint.connectedAnchor = m_RestAnchor;
                SetLimbSpring(1f, 1f);
                if (m_LooseCloth != null && ClothFits(m_LooseCloth)) { m_LooseCloth.SetLoose(false); m_LooseCloth = null; }
                if (m_LooseCloth == null) RestoreNow();
                return;
            }
            float s = k * k * (3f - 2f * k);
            SetLimbSpring(s, s);
            m_LimbJoint.connectedAnchor = Vector3.Lerp(m_ReleaseAnchor, m_RestAnchor, s);
            if (m_LooseCloth != null && ClothFits(m_LooseCloth)) { m_LooseCloth.SetLoose(false); m_LooseCloth = null; }
            if (k >= 1f && m_LooseCloth == null) RestoreNow();
        }

        /// <summary>Move the hand back to its place on the body. False once it's there.</summary>
        bool ReturnHand()
        {
            var hand = m_LimbJoint.GetComponent<Rigidbody>();
            var torso = m_LimbJoint.connectedBody;
            var rig = m_Spawner.rig;
            float h = rig != null ? rig.height : 0.12f;
            Vector3 home = torso.transform.TransformPoint(m_RestAnchor);
            Vector3 at = m_LimbJoint.transform.TransformPoint(m_LimbJoint.anchor);
            Vector3 relative = hand.GetPointVelocity(at) - torso.GetPointVelocity(home);
            if ((home - at).sqrMagnitude < 0.03f * h * 0.03f * h && relative.sqrMagnitude < 0.3f * h * 0.3f * h) return false; // home and settled
            ApplySupport();
            float omega = 2f * Mathf.PI * 1.5f / Mathf.Sqrt(rig != null ? rig.scale : 1f);
            Vector3 accel = (home - at) * (omega * omega) - relative * (2f * omega);
            accel = Vector3.ClampMagnitude(accel, 30f * h); // an unhurried swing back, not a snap
            var tuning = rig != null ? rig.GetComponent<MarionetteTuning>() : null;
            accel -= Physics.gravity * (tuning != null ? tuning.gravityScale : 1f);
            hand.AddForceAtPosition(accel * hand.mass, at, ForceMode.Force);
            return true;
        }

        static bool ClothFits(TextileLink link) =>
            Vector3.Distance(link.fromPoint.transform.position, link.toPoint.transform.position) < link.length;

        /// <summary>The point on the body the limb's joint anchor sits at right now, in the body's space.</summary>
        static Vector3 CurrentAnchor(ConfigurableJoint j) =>
            j.connectedBody.transform.InverseTransformPoint(j.transform.TransformPoint(j.anchor));

        void SetLimbSpring(float spring, float damper)
        {
            if (m_LimbJoint == null) return;
            var d = m_LimbDrive;
            d.positionSpring *= spring;
            d.positionDamper *= damper;
            m_LimbJoint.xDrive = m_LimbJoint.yDrive = m_LimbJoint.zDrive = d;
        }

        void RestoreNow()
        {
            if (m_LooseCloth != null) m_LooseCloth.SetLoose(false);
            m_LooseCloth = null;
            var steps = m_Spawner.rig != null ? m_Spawner.rig.GetComponent<AutoStep>() : null;
            if (steps != null) { steps.SetHeld(0, false); steps.SetHeld(1, false); }
            if (m_LimbJoint != null)
            {
                m_LimbJoint.connectedAnchor = m_RestAnchor;
                m_LimbJoint.xDrive = m_LimbJoint.yDrive = m_LimbJoint.zDrive = m_LimbDrive;
                m_LimbJoint.angularXMotion = m_LimbJoint.angularYMotion = m_LimbJoint.angularZMotion = ConfigurableJointMotion.Limited;
                m_LimbJoint.xMotion = m_LimbJoint.yMotion = m_LimbJoint.zMotion = ConfigurableJointMotion.Limited;
            }
            m_LimbJoint = null;
            m_Reattach = -1f;
        }

        void Stop()
        {
            if (m_LimbJoint != null)
            {
                m_Reattach = 0f;
                m_ReleaseAnchor = CurrentAnchor(m_LimbJoint) + (KeepsSupport ? m_SupportOffset : Vector3.zero);
                if (!IsHand) m_LimbJoint.connectedAnchor = m_ReleaseAnchor;
            }
            m_Active = false;
            m_Times = null;
            m_Path = null;
            m_Velocities = null;
        }

        Vector3 Sample(Vector3[] values, float t)
        {
            int i = 1;
            while (i < m_Times.Length - 1 && m_Times[i] < t) i++;
            float span = m_Times[i] - m_Times[i - 1];
            float k = span > 1e-5f ? Mathf.Clamp01((t - m_Times[i - 1]) / span) : 1f;
            return Vector3.Lerp(values[i - 1], values[i], k);
        }

        void LateUpdate()
        {
            var rig = m_Spawner.rig;
            PuppetNodeId? selected = m_Hands != null ? m_Hands.SelectedTarget : null;
            if (Playing) selected = m_Target;

            if (rig != null)
                foreach (var cross in new[] { rig.leftCross, rig.rightCross })
                    foreach (var c in cross.channels)
                        c.controlString.highlighted = selected.HasValue && c.controlString.attachment.node.id == selected.Value;

            List<Vector3> trail = m_Hands != null && m_Hands.Phase != XRHandsControlInput.TakePhase.Idle ? m_Hands.TrailWorld() : null;
            m_Trail.enabled = trail != null && trail.Count >= 2;
            if (!m_Trail.enabled) return;
            m_Trail.positionCount = trail.Count;
            for (int i = 0; i < trail.Count; i++) m_Trail.SetPosition(i, trail[i]);
            float scale = m_Hands.trackingSpace != null ? m_Hands.trackingSpace.lossyScale.x : 1f;
            m_Trail.widthMultiplier = 0.004f * scale;
            m_Trail.startColor = m_Trail.endColor = m_Hands.Phase switch
            {
                XRHandsControlInput.TakePhase.Recording => recordingColor,
                XRHandsControlInput.TakePhase.Full => fullColor,
                _ => expiredColor
            };
        }
    }
}

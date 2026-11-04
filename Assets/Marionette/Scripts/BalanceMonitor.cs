using System;
using System.Collections.Generic;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Balance: the player balances the upper body, the legs step by themselves, and the puppet falls
    /// when either gives out.
    ///
    /// Upper body. The palms set the lean the ankles hold the body at (leanControl): the left palm's roll
    /// leans it sideways, the right palm's pitch forward and back (fingers down = forward). Tip against
    /// the momentum to fight it. Lean is the torso's tilt from upright.
    ///
    /// Legs. The capture point is where a foot would have to land to stop the body's motion: the centre
    /// of mass plus its velocity times sqrt(height / g). When it comes within stepMargin of the feet's edge,
    /// AutoStep moves the trailing foot to it. Small momentum: a step or two, then still again.
    ///
    ///   Stable      upright, capture point over the feet
    ///   Unstable    leaning, or capture point drifting off the feet
    ///   Stepping    an automatic step is under way
    ///   Staggering  shoved off its feet: steps needed too quickly in a row (tripSteps within quickStepSeconds of
    ///               each other), a step too long to take, or a hard shove (shoveMargin). Controls are overridden,
    ///               the arms brace out, and it stumbles on with quick steps that absorb little. If the momentum
    ///               dies down it catches itself and controls return; a step that can't be made, or running out
    ///               of staggerSteps, and it falls.
    ///   Toppling    the whole body leaned past maxLean (or both feet off the ground too long). Controls are overridden,
    ///               the arms windmill for flailSeconds, then it falls flat that way.
    ///   Falling     the strings slip and the limbs go limp. Course respawn, or recover after recoverSeconds.
    /// </summary>
    [RequireComponent(typeof(MarionetteRig))]
    public class BalanceMonitor : MonoBehaviour
    {
        public enum State { Stable, Unstable, Stepping, Staggering, Toppling, Falling }

        [Header("Upper body (left palm roll = sideways, right palm pitch = forward/back)")]
        [Tooltip("Upper-body lean, degrees, past which the puppet topples while standing.")]
        [Range(5f, 60f)] public float maxLean = 18f;
        [Tooltip("Mid-step and while staggering the body may lean this many times maxLean before it topples.")]
        [Range(1f, 2.5f)] public float steppingLeanAllowance = 1.5f;
        [Tooltip("While staggering it may lean this many times maxLean before it topples: it fights to stay up.")]
        [Range(1f, 4f)] public float staggerLeanAllowance = 2.8f;
        [Tooltip("While staggering the body fights to stay upright, like someone shoved: how quickly it rights itself, Hz. 0 = off.")]
        [Range(0f, 5f)] public float staggerUpright = 2.5f;
        [Tooltip("Strongest righting effort while staggering, radians per second squared at gnome size.")]
        public float staggerUprightMax = 400f;
        [Tooltip("Lean, degrees, shown as Unstable.")]
        [Range(1f, 40f)] public float unstableLean = 8f;
        [Tooltip("Degrees of lean the ankles aim for per degree of left-palm tilt. Gravity adds to it, so the body ends up " +
                 "leaning more than this (about 1.7x with the default ankles).")]
        [Range(0f, 3f)] public float leanControl = 0.6f;
        [Tooltip("Most lean the left palm can ask for, degrees.")]
        [Range(0f, 60f)] public float maxLeanControl = 20f;

        [Header("Automatic steps (distances in puppet heights)")]
        [Tooltip("A step is taken once the capture point comes this close to the edge of the feet (negative = past it). " +
                 "Early, small steps keep it from getting going; late ones only come when it's already falling.")]
        [Range(-0.1f, 0.3f)] public float stepMargin = -0.02f;
        [Tooltip("Also step when the body leans this many degrees more than the palms ask for, toward the lean. 0 = off " +
                 "(the default: a bigger puppet rests at about 10 degrees, and this made it step and topple after growing).")]
        [Range(0f, 30f)] public float stepLean = 0f;
        [Tooltip("Longest step the legs can take. Needing a longer one trips the puppet.")]
        [Range(0.1f, 1.5f)] public float maxStepLength = 0.6f;
        [Tooltip("Half the distance between the feet when stepping.")]
        [Range(0.02f, 0.3f)] public float stanceHalfWidth = 0.12f;
        [Tooltip("A step needed within this many seconds of the last landing counts as quick (gnome size; smaller puppets scale it down).")]
        [Range(0f, 2f)] public float quickStepSeconds = 0.15f;
        [Tooltip("This many quick steps in a row trips the puppet.")]
        [Range(1, 6)] public int tripSteps = 2;
        [Tooltip("Most steps it staggers before it falls, if the momentum hasn't died down by then.")]
        [Range(0, 8)] public int staggerSteps = 4;
        [Tooltip("A shove that puts the capture point this far outside the feet (puppet heights) staggers it straight away.")]
        [Range(0.1f, 1.5f)] public float shoveMargin = 0.15f;
        [Tooltip("Body speed, puppet heights per second, below which a stagger ends with the puppet catching itself.")]
        [Range(0f, 2f)] public float catchSpeed = 0.35f;

        [Tooltip("How quickly the string tops follow the body when it moves by itself (momentum, steps), per second. " +
                 "Like a puppeteer walking along, so the strings don't hold the feet back. 0 = off.")]
        public float stringFollow = 4f;

        [Header("Falling")]
        [Tooltip("Seconds both feet may be off the ground (and not stepping) before it topples.")]
        [Range(0f, 2f)] public float airborneGrace = 0.4f;
        [Tooltip("Seconds of arm-waving before a topple becomes a fall.")]
        [Range(0f, 3f)] public float flailSeconds = 0.8f;
        [Tooltip("How fast the body is tipped over when it falls, radians per second at gnome size.")]
        public float fallSpin = 3f;
        [Tooltip("When it falls, the strings slip so the puppet really drops.")]
        public bool dropOnFall = true;
        [Tooltip("Seconds after a fall until the strings tighten again, if nothing respawns the puppet first.")]
        public float recoverSeconds = 3f;
        [Tooltip("Seconds after spawning before balance is judged, so the puppet can settle.")]
        public float settleSeconds = 0.5f;

        [Header("Debug view")]
        public bool showDebug = true;
        public Color stableColor = new Color(0.3f, 1f, 0.4f);
        public Color unstableColor = new Color(1f, 0.9f, 0.2f);
        public Color stepColor = new Color(0.3f, 0.7f, 1f);
        public Color criticalColor = new Color(1f, 0.45f, 0.1f);
        public Color fallingColor = new Color(1f, 0.15f, 0.15f);

        MarionetteRig m_Rig;
        AutoStep m_Steps;
        readonly List<GroundContact> m_Feet = new List<GroundContact>();
        readonly List<Vector2> m_Points = new List<Vector2>();
        readonly List<Vector2> m_Hull = new List<Vector2>();
        float m_Age, m_AirTime, m_PhaseTime, m_FallTime, m_LastStepTime = -10f, m_LastLanding = -10f;
        int m_QuickSteps, m_StaggerLeft;
        Vector3 m_FallDir = Vector3.forward;
        float[] m_SavedLengths;
        bool m_Paused, m_Slackened;
        float m_SupportY;
        LineRenderer m_Outline;
        Transform m_Marker;
        Renderer m_MarkerRenderer;

        public State Current { get; private set; } = State.Stable;
        /// <summary>Upper-body lean, degrees: how far the torso is tipped from upright.</summary>
        public float Lean { get; private set; }
        /// <summary>Horizontal direction the body leans.</summary>
        public Vector3 LeanDirection { get; private set; } = Vector3.forward;
        /// <summary>The lean the player asks for with the left palm. The ankles hold the body at this lean (SurfacePlacement).</summary>
        public Quaternion LeanTarget { get; private set; } = Quaternion.identity;
        /// <summary>Where a foot would have to land to stop the body's motion.</summary>
        public Vector3 CapturePoint { get; private set; }
        /// <summary>How far the capture point is inside the feet, puppet heights. Negative = outside.</summary>
        public float Margin { get; private set; }
        public int GroundedFeet { get; private set; }
        /// <summary>Tripping, toppling or down: the player's controls are ignored.</summary>
        public bool ControlsOverridden => Current is State.Staggering or State.Toppling or State.Falling;
        public int StepsTaken => m_Steps != null ? m_Steps.StepCount : 0;
        public event Action<State> StateChanged;

        /// <summary>Stop judging balance, e.g. while the puppet grows or shrinks.</summary>
        public void Pause(bool paused)
        {
            m_Paused = paused;
            if (paused && Current != State.Falling)
            {
                m_AirTime = 0f;
                m_QuickSteps = 0;
                SetState(State.Stable);
            }
        }

        void Awake()
        {
            m_Rig = GetComponent<MarionetteRig>();
            m_Steps = GetComponent<AutoStep>();
            if (m_Steps != null) m_Steps.Landed += _ => m_LastLanding = Time.time;
            foreach (var node in m_Rig.nodes)
            {
                var contact = node.GetComponent<GroundContact>();
                if (contact != null && node.id is PuppetNodeId.LeftFoot or PuppetNodeId.RightFoot) m_Feet.Add(contact);
            }
            CreateDebugView();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            m_Age += dt;
            SetOverride(ControlsOverridden);

            if (Current == State.Falling)
            {
                m_FallTime += dt;
                Recover(m_FallTime - recoverSeconds);
                return;
            }
            if (m_Paused || m_Age < settleSeconds) return;

            Measure();
            UpdateLeanTarget();
            FollowWithStrings(dt);

            switch (Current)
            {
                case State.Toppling:
                    Flail(dt);
                    if ((m_PhaseTime += dt) >= flailSeconds) Fall(m_FallDir);
                    return;
                case State.Staggering:
                    BraceArms();
                    FightToStayUp();
                    if (Lean > maxLean * staggerLeanAllowance) { Topple(LeanDirection); return; }
                    m_PhaseTime += dt;
                    Stagger();
                    return;
            }
            EaseStringsBack(dt);

            bool stepping = m_Steps != null && m_Steps.Stepping;
            bool placing = m_Steps != null && (m_Steps.IsHeld(0) || m_Steps.IsHeld(1)); // the player is dragging a foot
            // A hard shove sends it stumbling, even mid-step (the stagger carries on from this step).
            if (GroundedFeet > 0 && Margin < -shoveMargin) { Trip(); return; }
            if (Lean > maxLean * (stepping || placing ? steppingLeanAllowance : 1f))
            {
                // Knocked over while moving: it stumbles and fights to stay up. Leaning over standing still: it topples.
                if (stepping || placing || BodyVelocity().magnitude > catchSpeed * m_Rig.height) Trip(); else Topple(LeanDirection);
                return;
            }

            if (GroundedFeet == 0 && !stepping)
            {
                if ((m_AirTime += dt) > airborneGrace) { Topple(MotionDirection()); return; }
            }
            else m_AirTime = 0f;

            if (!stepping && GroundedFeet > 0)
            {
                float leanExcess = Lean - Quaternion.Angle(Quaternion.identity, LeanTarget);
                if (Margin < stepMargin) TryStep(Vector3.zero);
                else if (stepLean > 0f && leanExcess > stepLean) TryStep(LeanDirection * (0.15f * m_Rig.height)); // tipping: get a foot under it
            }

            stepping = m_Steps != null && m_Steps.Stepping;
            SetState(stepping ? State.Stepping
                : Lean > unstableLean || Margin < 0f || GroundedFeet == 0 ? State.Unstable
                : State.Stable);
        }

        // ---------- Measuring ----------

        void Measure()
        {
            m_Points.Clear();
            GroundedFeet = 0;
            float supportY = 0f;
            foreach (var f in m_Feet)
                if (f.Grounded) { GroundedFeet++; f.AddFootprint(m_Points); supportY += f.transform.position.y; }
            if (GroundedFeet > 0) m_SupportY = supportY / GroundedFeet;

            Vector3 com = Vector3.zero, momentum = Vector3.zero;
            float mass = 0f;
            foreach (var n in m_Rig.nodes)
            {
                com += n.Body.worldCenterOfMass * n.Body.mass;
                momentum += n.Body.linearVelocity * n.Body.mass;
                mass += n.Body.mass;
            }
            com /= mass;

            // Upper-body lean: how far the torso is tipped from upright. (Not measured from the feet: dragging a
            // foot ahead isn't leaning back.)
            Vector3 torsoUp = m_Rig.Node(PuppetNodeId.Center).Body.rotation * Vector3.up;
            Vector3 dir = new Vector3(torsoUp.x, 0f, torsoUp.z);
            Lean = Vector3.Angle(Vector3.up, torsoUp);
            if (dir.sqrMagnitude > 1e-10f) LeanDirection = dir.normalized;

            // Capture point: where the centre of mass, moving at its current speed, could be stopped.
            Vector3 v = momentum / mass;
            float g = Physics.gravity.magnitude * GravityScale();
            float heightAboveFeet = Mathf.Max(0.05f * m_Rig.height, com.y - m_SupportY);
            Vector3 capture = com + new Vector3(v.x, 0f, v.z) * Mathf.Sqrt(heightAboveFeet / g);
            CapturePoint = new Vector3(capture.x, m_SupportY, capture.z);

            if (m_Points.Count > 0)
            {
                ConvexHull(m_Points, m_Hull);
                Margin = SignedDistance(new Vector2(capture.x, capture.z), m_Hull) / m_Rig.height;
            }
            else
            {
                m_Hull.Clear();
                Margin = 0f;
            }
        }

        Vector3 BodyVelocity()
        {
            var c = m_Rig.Node(PuppetNodeId.Center).Body.linearVelocity;
            return new Vector3(c.x, 0f, c.z);
        }

        Vector3 MotionDirection()
        {
            Vector3 v = BodyVelocity();
            return v.sqrMagnitude > 1e-6f ? v.normalized : LeanDirection;
        }

        float GravityScale()
        {
            var tuning = m_Rig.GetComponent<MarionetteTuning>();
            return tuning != null ? tuning.gravityScale : 1f;
        }

        // ---------- Upper body: the player's lean ----------

        /// <summary>
        /// Palm tilts become the lean the ankles hold: left palm roll = sideways, right palm pitch = forward/back,
        /// in the player's frame (the cross neutral, which the follow view turns with the puppet). No input while overridden.
        /// </summary>
        void UpdateLeanTarget()
        {
            LeanTarget = Quaternion.identity;
            if (ControlsOverridden || m_Rig.leftCross == null || m_Rig.rightCross == null) return;
            Vector3 fwd = Flat(m_Rig.leftCross.neutral.forward);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 uL = m_Rig.leftCross.Tilt * Vector3.up, uR = m_Rig.rightCross.Tilt * Vector3.up;
            float side = Mathf.Atan2(Vector3.Dot(uL, right), Mathf.Max(0.05f, uL.y)) * Mathf.Rad2Deg;
            float fore = Mathf.Atan2(Vector3.Dot(uR, fwd), Mathf.Max(0.05f, uR.y)) * Mathf.Rad2Deg;
            side = Mathf.Clamp(side * leanControl, -maxLeanControl, maxLeanControl);
            fore = Mathf.Clamp(fore * leanControl, -maxLeanControl, maxLeanControl);
            if (Mathf.Abs(side) < 0.01f && Mathf.Abs(fore) < 0.01f) return;
            Vector3 up = Vector3.up + right * Mathf.Tan(side * Mathf.Deg2Rad) + fwd * Mathf.Tan(fore * Mathf.Deg2Rad);
            float angle = Mathf.Min(Vector3.Angle(Vector3.up, up), maxLeanControl);
            Vector3 axis = Vector3.Cross(Vector3.up, up);
            if (axis.sqrMagnitude < 1e-10f) return;
            LeanTarget = Quaternion.AngleAxis(angle, axis.normalized);
        }

        /// <summary>String tops glide after the body, horizontally.</summary>
        void FollowWithStrings(float dt)
        {
            if (stringFollow <= 0f || m_Rig.leftCross == null || m_Rig.rightCross == null) return;
            Vector3 mid = (m_Rig.leftCross.neutral.position + m_Rig.rightCross.neutral.position) * 0.5f;
            Vector3 behind = m_Rig.CenterOfMass - mid;
            behind.y = 0f;
            Vector3 step = behind * (1f - Mathf.Exp(-stringFollow * dt));
            m_Rig.leftCross.neutral.position += step;
            m_Rig.rightCross.neutral.position += step;
        }

        // ---------- Legs: automatic steps ----------

        void TryStep(Vector3 reach)
        {
            if (m_Steps == null) return;
            if (!PlanStep(out int leg, out Vector3 target, out float length, reach)) return;

            bool quick = Time.time - m_LastLanding < quickStepSeconds * Mathf.Sqrt(m_Rig.scale);
            m_QuickSteps = quick ? m_QuickSteps + 1 : 0;
            if (m_QuickSteps >= tripSteps || length > maxStepLength * m_Rig.height)
            {
                Trip();
                return;
            }
            m_Steps.Begin(leg, target, stagger: false);
            m_LastStepTime = Time.time;
        }

        /// <summary>The trailing foot steps to the capture point, keeping the feet apart sideways.</summary>
        bool PlanStep(out int leg, out Vector3 target, out float length, Vector3 reach = default)
        {
            leg = 0; target = Vector3.zero; length = 0f;
            var left = m_Steps.Foot(0);
            var right = m_Steps.Foot(1);
            if (left == null || right == null) return false;

            Vector3 capture = CapturePoint + reach;
            float dl = Flat(left.position - capture).magnitude, dr = Flat(right.position - capture).magnitude;
            leg = dl > dr ? 0 : 1; // the foot further from where the body is going
            // While the player is placing a foot, the other one is the one standing: no automatic steps.
            if (m_Steps.IsHeld(0) || m_Steps.IsHeld(1)) return false;
            var moving = leg == 0 ? left : right;
            var other = leg == 0 ? right : left;

            Vector3 dir = Flat(capture - other.position);
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : MotionDirection();
            Vector3 lateral = Vector3.Cross(Vector3.up, dir);
            float side = Mathf.Sign(Vector3.Dot(moving.position - other.position, lateral));
            if (side == 0f) side = leg == 0 ? -1f : 1f;
            Vector3 aim = capture + lateral * (side * stanceHalfWidth * m_Rig.height);

            target = new Vector3(aim.x, GroundHeight(aim), aim.z);
            length = Flat(target - moving.position).magnitude;
            return true;
        }

        float GroundHeight(Vector3 at)
        {
            float h = m_Rig.height;
            var hits = Physics.RaycastAll(at + Vector3.up * h, Vector3.down, h * 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = m_SupportY;
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<MarionetteRig>() == m_Rig) continue;
                if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
            }
            return y;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---------- Losing it ----------

        void Trip()
        {
            m_StaggerLeft = staggerSteps;
            m_FallDir = MotionDirection();
            m_PhaseTime = 0f;
            SetState(State.Staggering);
            SlackenStrings(handsOnly: true, factor: 2f); // so the arms can brace out
        }

        /// <summary>
        /// Shoved: keep stepping into the momentum, absorbing little, arms out. Catches itself if the momentum
        /// dies down; falls when a step can't be made or it runs out of steps.
        /// </summary>
        void Stagger()
        {
            if (m_Steps.Stepping || m_Steps.IsHeld(0) || m_Steps.IsHeld(1)) return; // a dragged foot is let go next step
            float speed = BodyVelocity().magnitude / m_Rig.height;
            bool tipping = Lean > maxLean;
            if (Margin > 0f && speed < catchSpeed && !tipping && GroundedFeet == 2 && m_PhaseTime > 0.3f)
            {
                m_QuickSteps = 0;
                SetState(State.Unstable); // caught it: controls come back, the strings ease back
                return;
            }
            if (m_PhaseTime > 4f) { Fall(LeanDirection); return; } // never settled
            if (Margin > stepMargin && !tipping) return; // over its feet for now: wait for the momentum to settle or build
            // Tipping over: step the way it tips, to get the feet back under the body.
            Vector3 reach = tipping ? LeanDirection * (0.2f * m_Rig.height) : Vector3.zero;
            if (m_StaggerLeft <= 0 || !PlanStep(out int leg, out Vector3 target, out float length, reach) || length > maxStepLength * m_Rig.height * 1.5f)
            {
                Fall(MotionDirection());
                return;
            }
            m_StaggerLeft--;
            m_Steps.Begin(leg, target, stagger: true);
            m_LastStepTime = Time.time;
        }

        /// <summary>The body tries to right itself while it stumbles, with limited strength: a big enough shove still wins.</summary>
        void FightToStayUp()
        {
            if (staggerUpright <= 0f) return;
            var center = m_Rig.Node(PuppetNodeId.Center).Body;
            Quaternion.FromToRotation(center.rotation * Vector3.up, Vector3.up).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) axis = Vector3.zero;
            float omega = 2f * Mathf.PI * staggerUpright;
            Vector3 spin = Vector3.ProjectOnPlane(center.angularVelocity, Vector3.up); // tipping only, not turning
            Vector3 accel = axis * (angle * Mathf.Deg2Rad * omega * omega) - spin * (2f * omega);
            accel = Vector3.ClampMagnitude(accel, staggerUprightMax / Mathf.Sqrt(m_Rig.scale));
            // The head is locked on top of the body and makes up most of the turning inertia, so scale by both.
            var head = m_Rig.Node(PuppetNodeId.Head).Body;
            float inertia = center.inertiaTensor.magnitude + head.inertiaTensor.magnitude
                            + head.mass * (head.worldCenterOfMass - center.worldCenterOfMass).sqrMagnitude;
            center.AddTorque(accel * inertia, ForceMode.Force);
        }

        /// <summary>While staggering the arms reach out to the sides and a little forward, bobbing, like someone shoved.</summary>
        void BraceArms()
        {
            float h = m_Rig.height;
            var center = m_Rig.Node(PuppetNodeId.Center).Body;
            Vector3 up = center.rotation * Vector3.up;
            Vector3 dir = MotionDirection();
            Vector3 lateral = Vector3.Cross(Vector3.up, dir);
            float pd = 2f * Mathf.PI * 5f;
            float g = GravityScale();
            float bob = Mathf.Sin(m_PhaseTime * 2f * Mathf.PI * 3f / Mathf.Sqrt(m_Rig.scale));
            for (int side = 0; side < 2; side++)
            {
                var hand = m_Rig.Node(side == 0 ? PuppetNodeId.LeftHand : PuppetNodeId.RightHand).Body;
                float sgn = side == 0 ? -1f : 1f;
                Vector3 target = center.position + up * ((0.15f + 0.06f * bob * sgn) * h) + lateral * (sgn * 0.35f * h) + dir * (0.1f * h);
                Vector3 accel = (target - hand.position) * (pd * pd) - hand.linearVelocity * (2f * pd);
                accel = Vector3.ClampMagnitude(accel, 300f * h) - Physics.gravity * g;
                hand.AddForce(accel * hand.mass, ForceMode.Force);
            }
        }

        void Topple(Vector3 direction)
        {
            m_FallDir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            m_PhaseTime = 0f;
            SetState(State.Toppling);
            // Let the hand strings give, so the arms can wave.
            SlackenStrings(handsOnly: true, factor: 3f);
        }

        /// <summary>Windmill both arms around the shoulders, in the plane of the fall, as if fighting it.</summary>
        void Flail(float dt)
        {
            float h = m_Rig.height;
            var center = m_Rig.Node(PuppetNodeId.Center).Body;
            Vector3 up = center.rotation * Vector3.up;
            Vector3 lateral = Vector3.Cross(Vector3.up, m_FallDir);
            float omega = 2f * Mathf.PI * 2.5f / Mathf.Sqrt(m_Rig.scale); // 2.5 turns/s at gnome size
            float radius = 0.2f * h;
            float pd = 2f * Mathf.PI * 8f;
            float g = GravityScale();
            for (int side = 0; side < 2; side++)
            {
                var hand = m_Rig.Node(side == 0 ? PuppetNodeId.LeftHand : PuppetNodeId.RightHand).Body;
                Vector3 shoulder = center.position + up * (0.12f * h) + lateral * ((side == 0 ? -1f : 1f) * 0.15f * h);
                float phase = omega * m_PhaseTime + (side == 0 ? 0f : Mathf.PI);
                Vector3 offset = (up * Mathf.Cos(phase) + m_FallDir * Mathf.Sin(phase)) * radius;
                Vector3 velocity = (-up * Mathf.Sin(phase) + m_FallDir * Mathf.Cos(phase)) * (radius * omega);
                Vector3 accel = (shoulder + offset - hand.position) * (pd * pd) + (velocity - hand.linearVelocity) * (2f * pd);
                accel = Vector3.ClampMagnitude(accel, 400f * h) - Physics.gravity * g;
                hand.AddForce(accel * hand.mass, ForceMode.Force);
            }
            // And the body keeps going over.
            float s = m_Rig.scale;
            center.AddTorque(Vector3.Cross(Vector3.up, m_FallDir) * (fallSpin * 2f / Mathf.Sqrt(s)), ForceMode.Acceleration);
        }

        void Fall(Vector3 direction)
        {
            m_FallDir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            SetState(State.Falling);
            m_FallTime = 0f;
            if (m_Steps != null) m_Steps.ResetLegs();
            if (!dropOnFall) return;

            SlackenStrings(handsOnly: false, factor: 8f);
            // Limbs go limp and the body is tipped over, so it lands flat on its front, back or side.
            if (m_Rig.limbJoints != null)
                foreach (var j in m_Rig.limbJoints)
                {
                    if (j == null) continue;
                    var d = j.xDrive; d.positionSpring *= 0.1f; j.xDrive = j.yDrive = j.zDrive = d;
                    var a = j.slerpDrive; a.positionSpring *= 0.05f; j.slerpDrive = a;
                }
            var center = m_Rig.Node(PuppetNodeId.Center).Body;
            center.angularVelocity += Vector3.Cross(Vector3.up, m_FallDir) * (fallSpin / Mathf.Sqrt(m_Rig.scale));
        }

        void SlackenStrings(bool handsOnly, float factor)
        {
            var strings = Strings();
            if (m_SavedLengths == null)
            {
                m_SavedLengths = new float[strings.Count];
                for (int i = 0; i < strings.Count; i++) m_SavedLengths[i] = strings[i].length;
            }
            m_Slackened = true;
            for (int i = 0; i < strings.Count; i++)
            {
                bool hand = strings[i].attachment.node.id is PuppetNodeId.LeftHand or PuppetNodeId.RightHand;
                if (!handsOnly || hand) strings[i].SetLength(m_SavedLengths[i] * factor);
            }
        }

        /// <summary>t below 0: still down. 0..1: strings tightening back. Past 1: up again.</summary>
        void Recover(float t)
        {
            if (t < 0f) return;
            if (m_Slackened && m_SavedLengths != null)
            {
                var strings = Strings();
                float k = Mathf.Clamp01(t);
                for (int i = 0; i < strings.Count && i < m_SavedLengths.Length; i++)
                    strings[i].SetLength(Mathf.Lerp(strings[i].length, m_SavedLengths[i], k * k));
                if (t < 1f) return;
                for (int i = 0; i < strings.Count && i < m_SavedLengths.Length; i++) strings[i].SetLength(m_SavedLengths[i]);
            }
            m_SavedLengths = null;
            m_Slackened = false;
            var tuning = m_Rig.GetComponent<MarionetteTuning>();
            if (tuning != null) tuning.Apply(); // limb springs back to normal
            m_Age = 0f; // settle again before judging
            m_AirTime = 0f;
            m_QuickSteps = 0;
            SetState(State.Stable);
        }

        /// <summary>After a stagger it caught: the strings slackened for the arms tighten back over about 0.3 s.</summary>
        void EaseStringsBack(float dt)
        {
            if (!m_Slackened || m_SavedLengths == null) return;
            var strings = Strings();
            bool done = true;
            float k = 1f - Mathf.Exp(-10f * dt);
            for (int i = 0; i < strings.Count && i < m_SavedLengths.Length; i++)
            {
                float l = Mathf.Lerp(strings[i].length, m_SavedLengths[i], k);
                if (Mathf.Abs(l - m_SavedLengths[i]) < 0.002f * m_Rig.height) l = m_SavedLengths[i];
                else done = false;
                strings[i].SetLength(l);
            }
            if (done) { m_SavedLengths = null; m_Slackened = false; }
        }

        List<ControlString> Strings()
        {
            var list = new List<ControlString>(4);
            foreach (var cross in new[] { m_Rig.leftCross, m_Rig.rightCross })
                foreach (var c in cross.channels) list.Add(c.controlString);
            return list;
        }

        void SetOverride(bool on)
        {
            if (m_Rig.leftCross != null) m_Rig.leftCross.overridden = on;
            if (m_Rig.rightCross != null) m_Rig.rightCross.overridden = on;
        }

        void SetState(State s)
        {
            if (s == Current) return;
            Current = s;
            StateChanged?.Invoke(s);
        }

        // ---------- Geometry ----------

        /// <summary>Andrew's monotone chain. Result is counter-clockwise.</summary>
        static void ConvexHull(List<Vector2> pts, List<Vector2> hull)
        {
            hull.Clear();
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[pass == 0 ? i : pts.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>Distance from p to the polygon's edge: positive inside, negative outside.</summary>
        static float SignedDistance(Vector2 p, List<Vector2> poly)
        {
            if (poly.Count == 0) return float.NegativeInfinity;
            if (poly.Count == 1) return -Vector2.Distance(p, poly[0]);
            float best = float.MaxValue;
            bool inside = poly.Count >= 3;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                if (Cross(a, b, p) < 0f) inside = false;
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside ? best : -best;
        }

        // ---------- Debug view: feet outline + capture point dot, coloured by state ----------

        void CreateDebugView()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var go = new GameObject("BalanceOutline");
            go.transform.SetParent(transform, false);
            m_Outline = go.AddComponent<LineRenderer>();
            m_Outline.useWorldSpace = true;
            m_Outline.loop = true;
            if (shader != null) m_Outline.sharedMaterial = new Material(shader);

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "BalanceCapturePoint";
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(transform, false);
            m_Marker = dot.transform;
            m_MarkerRenderer = dot.GetComponent<Renderer>();
            if (shader != null) m_MarkerRenderer.material = new Material(shader);
        }

        void LateUpdate()
        {
            bool show = showDebug && m_Outline != null;
            if (m_Outline != null) m_Outline.enabled = show && m_Hull.Count >= 2 && Current != State.Falling;
            if (m_Marker != null) m_Marker.gameObject.SetActive(show);
            if (!show) return;

            Color color = Current switch
            {
                State.Stable => stableColor,
                State.Unstable => unstableColor,
                State.Stepping => stepColor,
                State.Falling => fallingColor,
                _ => criticalColor
            };
            float h = m_Rig.height;
            float y = m_SupportY + h * 0.01f;
            if (m_Outline.enabled)
            {
                m_Outline.positionCount = m_Hull.Count;
                for (int i = 0; i < m_Hull.Count; i++) m_Outline.SetPosition(i, new Vector3(m_Hull[i].x, y, m_Hull[i].y));
                m_Outline.widthMultiplier = h * 0.012f;
                m_Outline.startColor = m_Outline.endColor = color;
            }
            m_Marker.position = new Vector3(CapturePoint.x, y, CapturePoint.z);
            m_Marker.localScale = DivideByParentScale(Vector3.one * (h * 0.05f));
            m_MarkerRenderer.material.color = color;
        }

        Vector3 DivideByParentScale(Vector3 world)
        {
            Vector3 s = transform.lossyScale;
            return new Vector3(world.x / s.x, world.y / s.y, world.z / s.z);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Marionette
{
    /// <summary>
    /// Quest hand tracking -> control crosses.
    ///
    /// Palm down, fingers extended is the neutral pose. The palm's movement relative to that
    /// neutral is the cross translation (H), its rotation around world up is the yaw / swing
    /// axis, and how far each mapped finger is raised above the back of the hand is that channel's
    /// value (F): lift a finger, lift its string. (FingerAction.Press is the older mapping, where
    /// pressing a finger down pulls the string.) Small finger motions are enough: each finger is
    /// measured against its own pose at calibration.
    ///
    /// Palm tilt sets the upper body's lean, in every mode: the left palm's roll leans the puppet
    /// sideways, the right palm's pitch leans it forward and back (fingers down = forward). Each cross
    /// tilts with its palm too.
    ///
    /// FingerAction.PinchFlick is a different control scheme, "pinch and flick":
    ///   - Left hand pinches to select one string: thumb to index / middle / ring / little picks the
    ///     puppet's left hand / right hand / left foot / right foot.
    ///   - While the pinch is held, the right index fingertip's path is recorded, for up to
    ///     recordSeconds. Hold longer than recordSeconds + expireSeconds and the take is cancelled.
    ///   - Releasing the pinch fires FlickReleased with the recorded path. StringFlick drives the
    ///     selected body part along it at the speed it was drawn: a delayed effect, faster = harder.
    ///   - Both crosses freeze during a take, so drawing doesn't move the puppet, and resume without a
    ///     jump afterwards. Finger channels are unused in this scheme.
    ///
    /// Size gesture: both hands closed into fists and held. Palms down = grow, palms up = shrink.
    /// A closed fist sends no string input, so clenching doesn't yank every string at once.
    /// </summary>
    public class XRHandsControlInput : ControlInputSource
    {
        public enum Finger { Thumb, Index, Middle, Ring, Little }
        public enum FingerAction { Lift, Press, PinchFlick, PointPlace }
        public enum TakePhase { Idle, Recording, Full, Expired, Holding }

        /// <summary>One recorded flick: which body part, and the right index path in world metres.</summary>
        public struct FlickTake
        {
            public PuppetNodeId target;
            public float[] times;     // seconds since the pinch started
            public Vector3[] path;    // world-space offset from the first sample (includes the player's scale)
        }

        /// <summary>Fired when the left pinch is released after a valid take.</summary>
        public event System.Action<FlickTake> FlickReleased;

        // ---- Point & place (PartPlacer) ----
        /// <summary>The part held by the left pinch (PointPlace), while the pinch lasts.</summary>
        public PuppetNodeId? HeldTarget => fingerAction == FingerAction.PointPlace && m_Phase == TakePhase.Holding ? TargetFor(m_SelectMask) : null;
        /// <summary>Right hand's pointing ray, world space: from an estimated shoulder out through the pinch point.</summary>
        public Ray RightAim { get; private set; }
        public bool RightAimValid { get; private set; }
        /// <summary>Right thumb and index pinched together.</summary>
        public bool RightPinching { get; private set; }
        /// <summary>Set by PartPlacer while the right hand holds a part directly: both crosses hold still.</summary>
        [System.NonSerialized] public bool externalFreeze;

        [Tooltip("XR Origin's Camera Floor Offset. Hand poses are converted from this space to world.")]
        public Transform trackingSpace;

        [Tooltip("Finger driving each channel of the LEFT cross (left hand, right hand, head).")]
        public Finger[] leftFingers = { Finger.Index, Finger.Middle, Finger.Ring };
        [Tooltip("Finger driving each channel of the RIGHT cross (left foot, right foot, center).")]
        public Finger[] rightFingers = { Finger.Index, Finger.Middle, Finger.Ring };

        [Tooltip("Lift = raise a finger to lift its string. Press = push a finger down to pull it. " +
                 "PinchFlick = pinch left fingers to pick a string, draw with the right index, release to apply.")]
        public FingerAction fingerAction = FingerAction.PointPlace;

        [Header("Palm tilt (all modes)")]
        [Tooltip("Tilting the palm tilts the cross.")]
        public bool useTilt = true;
        [Tooltip("Degrees of palm tilt ignored, so a slightly uneven hand doesn't tip the puppet.")]
        [Range(0f, 20f)] public float tiltDeadZone = 4f;
        [Tooltip("Cross tilt per degree of palm tilt past the dead zone.")]
        [Range(0f, 2f)] public float tiltGain = 1f;

        [Header("Pinch & flick")]
        [Tooltip("Thumb-to-fingertip distance that starts a pinch, metres (real hand size).")]
        public float pinchStart = 0.02f;
        [Tooltip("Distance that ends it. Larger than pinchStart so a pinch doesn't flicker.")]
        public float pinchRelease = 0.035f;
        [Tooltip("Seconds after a pinch starts during which more fingers can join it (two-finger pinches).")]
        public float selectSeconds = 0.15f;
        [Tooltip("Seconds of right index movement recorded per pinch.")]
        [Range(1f, 3f)] public float recordSeconds = 2f;
        [Tooltip("Seconds you may keep pinching after recording ends before the take is cancelled.")]
        public float expireSeconds = 1f;
        public PuppetNodeId pinchIndex = PuppetNodeId.LeftHand;
        public PuppetNodeId pinchMiddle = PuppetNodeId.RightHand;
        public PuppetNodeId pinchRing = PuppetNodeId.LeftFoot;
        public PuppetNodeId pinchLittle = PuppetNodeId.RightFoot;

        [Header("Lift (finger raised above the back of the hand)")]
        [Tooltip("Degrees a finger must rise above its calibrated angle for full string lift.")]
        [Range(5f, 60f)] public float fullLiftAngle = 25f;
        [Tooltip("Degrees of rise ignored, so jitter and resting fingers don't lift strings.")]
        [Range(0f, 20f)] public float liftDeadZone = 4f;

        [Header("Press (finger pushed down)")]
        [Tooltip("Fraction of a finger's extended distance at which it counts as fully pressed.")]
        [Range(0.2f, 0.95f)] public float fullPressRatio = 0.6f;
        [Tooltip("Ignore finger motion below this value to stop jitter lifting strings.")]
        [Range(0f, 0.3f)] public float deadZone = 0.08f;

        [Header("Size gesture")]

        [Tooltip("Every non-thumb finger must be pressed at least this much to count as a fist.")]
        [Range(0.5f, 1f)] public float fistThreshold = 0.8f;

        [Header("Smoothing (One Euro filter)")]
        [Tooltip("Lower = steadier when the hand is still, but more lag. Hz.")]
        public float minCutoff = 1.5f;
        [Tooltip("Higher = less lag during fast moves.")]
        public float speedCoefficient = 4f;

        class HandState
        {
            public Vector3 palmTrack; // filtered palm position, tracking space
            public bool tracked;
            public bool wasTracked;
            public Vector3 lastTranslation;
            public readonly OneEuro3 filter = new OneEuro3();
            public bool fist;
            public bool palmUp;
            public bool calibrated;
            public Vector3 neutralPos;
            public float neutralYaw;
            public readonly float[] extended = new float[5];
            public readonly float[] restAngle = new float[5];
            public Vector3 restUp = Vector3.up;       // back of the hand at calibration, tracking space
            public float lastYaw;
            public Quaternion lastTilt = Quaternion.identity; // tracking space
            public bool frozen;
            public Vector3 frozenTranslation;
            public float frozenYaw;
            public Quaternion frozenTilt = Quaternion.identity;
        }

        // Pinch & flick take
        TakePhase m_Phase;
        int m_SelectMask;
        float m_TakeStart;
        readonly List<float> m_Times = new List<float>();
        readonly List<Vector3> m_Samples = new List<Vector3>(); // right index tip, tracking space
        readonly List<Vector3> m_Trail = new List<Vector3>();

        public TakePhase Phase => m_Phase;
        /// <summary>Seconds since the current pinch started.</summary>
        public float TakeAge => m_Phase == TakePhase.Idle ? 0f : Time.time - m_TakeStart;
        /// <summary>Body part the current pinch selects, or null if none or an unmapped finger combination.</summary>
        public PuppetNodeId? SelectedTarget => m_Phase == TakePhase.Idle ? null : TargetFor(m_SelectMask);

        /// <summary>The recorded right index path in world space, for drawing.</summary>
        public List<Vector3> TrailWorld()
        {
            m_Trail.Clear();
            foreach (var p in m_Samples) m_Trail.Add(trackingSpace != null ? trackingSpace.TransformPoint(p) : p);
            return m_Trail;
        }

        /// <summary>Finger bitmask (1 index, 2 middle, 4 ring, 8 little) to body part.</summary>
        public PuppetNodeId? TargetFor(int mask) => mask switch
        {
            1 => pinchIndex,
            2 => pinchMiddle,
            4 => pinchRing,
            8 => pinchLittle,
            _ => null // more than one finger: ambiguous, ignored
        };

        readonly HandState m_Left = new HandState();
        readonly HandState m_Right = new HandState();
        readonly float[] m_Distances = new float[5];
        readonly float[] m_Angles = new float[5];

        static readonly XRHandJointID[] k_Knuckles =
        {
            XRHandJointID.ThumbProximal, XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal,
            XRHandJointID.RingProximal, XRHandJointID.LittleProximal
        };
        XRHandSubsystem m_Subsystem;
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        static readonly XRHandJointID[] k_Tips =
        {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
            XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        /// <summary>Re-capture the neutral pose on the next tracked frame (hold hands flat, palms down).</summary>
        public void Calibrate()
        {
            m_Left.calibrated = m_Right.calibrated = false;
            m_LineNeutralSet = false; // the hands' line now becomes "level"
        }

        // ---- Lean from the line between the hands (Point & Place) ----
        Quaternion m_LineLean = Quaternion.identity, m_LineNeutral = Quaternion.identity;
        bool m_LineNeutralSet;

        public override bool ReadLean(out Quaternion lean)
        {
            lean = m_LineLean;
            if (fingerAction != FingerAction.PointPlace) return false;
            bool frozen = m_Phase != TakePhase.Idle || externalFreeze; // choosing a target: hands are busy, lean holds
            if (!frozen && m_Left.tracked && m_Right.tracked)
            {
                var tilt = HandLineTilt(m_Left.palmTrack, m_Right.palmTrack, trackingSpace != null ? trackingSpace.rotation : Quaternion.identity);
                if (!m_LineNeutralSet) { m_LineNeutral = tilt; m_LineNeutralSet = true; }
                m_LineLean = tilt * Quaternion.Inverse(m_LineNeutral);
            }
            lean = m_LineLean;
            return true;
        }

        /// <summary>
        /// The line between the hands as a tilt of "up" (world space): toward where the line slopes down to, by how
        /// steeply. Right hand lower: toward the right. One hand in front of the other with the front one lower:
        /// forward. Hands closer than 5 cm apart horizontally: no tilt (the slope is meaningless).
        /// </summary>
        public static Quaternion HandLineTilt(Vector3 leftPalm, Vector3 rightPalm, Quaternion space)
        {
            bool rightLower = rightPalm.y < leftPalm.y;
            Vector3 down = rightLower ? rightPalm - leftPalm : leftPalm - rightPalm; // from the higher hand to the lower
            Vector3 flat = new Vector3(down.x, 0f, down.z);
            if (flat.magnitude < 0.05f) return Quaternion.identity;
            float slope = Mathf.Atan2(-down.y, flat.magnitude) * Mathf.Rad2Deg;
            Vector3 dir = space * flat.normalized;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) return Quaternion.identity;
            return Quaternion.AngleAxis(slope, Vector3.Cross(Vector3.up, dir.normalized));
        }

        public override void ResetPose() => Calibrate();

        /// <summary>True when a hand tracking subsystem is running (headset on, hand tracking enabled).</summary>
        public bool IsAvailable => EnsureSubsystem();

        public override bool SupportsTravel => true;

        public override SizeGesture ReadSizeGesture()
        {
            if (!m_Left.tracked || !m_Right.tracked || !m_Left.fist || !m_Right.fist) return SizeGesture.None;
            if (m_Left.palmUp && m_Right.palmUp) return SizeGesture.Shrink;
            if (!m_Left.palmUp && !m_Right.palmUp) return SizeGesture.Grow;
            return SizeGesture.None; // mixed palms: no gesture
        }

        public override bool Read(ControlSide side, ref CrossInput input)
        {
            input.Tracked = false;
            HandState state = side == ControlSide.Left ? m_Left : m_Right;
            state.tracked = false;
            if (!EnsureSubsystem()) return false;

            XRHand hand = side == ControlSide.Left ? m_Subsystem.leftHand : m_Subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm))
            {
                state.wasTracked = false;
                if (side == ControlSide.Right) { RightAimValid = false; RightPinching = false; }
                if (side == ControlSide.Left && m_Phase == TakePhase.Holding) m_Phase = TakePhase.Idle; // lost the pinching hand: set it down
                // Left hand lost mid-take: don't leave the crosses frozen for ever.
                if (side == ControlSide.Left && m_Phase != TakePhase.Idle && TakeAge > recordSeconds + expireSeconds)
                {
                    m_Phase = TakePhase.Idle;
                    m_Samples.Clear();
                    m_Times.Clear();
                }
                return true;
            }

            // Measured in tracking space (relative to the player), not world space: when PuppetTravel
            // carries the player along, the hands come too, and that mustn't count as hand motion.
            Vector3 rawPos = palm.position;
            if (!state.wasTracked) state.filter.Reset(rawPos);
            Vector3 palmPos = state.filter.Filter(rawPos, Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime, minCutoff, speedCoefficient);
            state.palmTrack = palmPos;
            // Relative to the player too, so the follow view turning the player isn't read as a wrist twist.
            float yaw = YawOf(palm.rotation);

            float[] dist = m_Distances;
            Vector3 backOfHand = palm.rotation * Vector3.up;
            for (int f = 0; f < 5; f++)
            {
                dist[f] = FingerDistance(hand, f, palm.position);
                m_Angles[f] = FingerAngle(hand, f, backOfHand);
            }

            if (!state.calibrated)
            {
                state.neutralPos = palmPos;
                state.neutralYaw = yaw;
                for (int f = 0; f < 5; f++)
                {
                    state.extended[f] = Mathf.Max(dist[f], 0.01f);
                    state.restAngle[f] = m_Angles[f];
                }
                state.restUp = backOfHand;
                state.calibrated = true;
            }
            else if (!state.wasTracked)
            {
                // Hand came back into view, possibly somewhere else: continue from where the
                // strings were instead of jumping to the new hand position.
                state.neutralPos = palmPos - state.lastTranslation;
            }
            state.wasTracked = true;

            // Back of the hand is the palm joint's up axis: pointing at the floor = palm up.
            state.tracked = true;
            state.palmUp = Vector3.Dot(ToWorldRotation(palm.rotation) * Vector3.up, Vector3.down) > 0.5f;
            state.fist = true;
            for (int f = 1; f < 5; f++)
                if (Press(state, f) < fistThreshold) { state.fist = false; break; }

            if (fingerAction == FingerAction.PinchFlick && side == ControlSide.Left)
                UpdateTake(hand);
            if (fingerAction == FingerAction.PointPlace && side == ControlSide.Left)
                UpdateHold(hand);
            if (side == ControlSide.Right) UpdateRightAim(hand);

            bool frozen = ((fingerAction is FingerAction.PinchFlick or FingerAction.PointPlace) && m_Phase != TakePhase.Idle) || externalFreeze;
            if (frozen && !state.frozen)
            {
                state.frozen = true;
                state.frozenTranslation = state.lastTranslation;
                state.frozenYaw = state.lastYaw;
                state.frozenTilt = state.lastTilt;
            }
            else if (!frozen && state.frozen)
            {
                // Take over: carry on from the frozen pose instead of jumping to where the hand is now.
                state.frozen = false;
                state.neutralPos = palmPos - state.frozenTranslation;
                state.neutralYaw = yaw - state.frozenYaw;
                state.restUp = Quaternion.Inverse(state.frozenTilt) * backOfHand;
            }

            input.Tracked = true;
            if (state.frozen)
            {
                state.lastTranslation = state.frozenTranslation;
                state.lastYaw = state.frozenYaw;
                state.lastTilt = state.frozenTilt;
            }
            else
            {
                state.lastTranslation = palmPos - state.neutralPos;
                state.lastYaw = Mathf.DeltaAngle(state.neutralYaw, yaw);
                state.lastTilt = useTilt ? Tilt(state.restUp, backOfHand) : Quaternion.identity;
            }
            // To world metres. Includes the player's scale, so at mouse size hand motion shrinks too.
            input.Translation = ToWorldVector(state.lastTranslation);
            input.Yaw = state.lastYaw;
            // Upper-body lean, split between the hands: the left palm's roll leans the puppet sideways,
            // the right palm's pitch leans it forward and back (fingers pointing down = forward).
            // Point & place leans from the line between the hands (ReadLean); the crosses stay level.
            input.Tilt = fingerAction == FingerAction.PointPlace ? Quaternion.identity
                : ToWorldTilt(KeepTiltAxis(state.lastTilt, sideways: side == ControlSide.Left));

            Finger[] map = side == ControlSide.Left ? leftFingers : rightFingers;
            for (int i = 0; i < input.Channels.Length && i < map.Length; i++)
            {
                if (state.fist || fingerAction is FingerAction.PinchFlick or FingerAction.PointPlace) { input.Channels[i] = 0f; continue; }
                int f = (int)map[i];
                if (fingerAction == FingerAction.Lift)
                {
                    float rise = m_Angles[f] - state.restAngle[f];
                    input.Channels[i] = Mathf.InverseLerp(liftDeadZone, fullLiftAngle, rise);
                }
                else
                {
                    float press = Press(state, f);
                    input.Channels[i] = press < deadZone ? 0f : Mathf.InverseLerp(deadZone, 1f, press);
                }
            }
            return true;
        }

        /// <summary>Pinch & flick: start, select, record, then fire or cancel on release. Called once per step.</summary>
        /// <summary>Point & place: the left pinch holds a string's part for as long as it lasts (no recording).</summary>
        void UpdateHold(XRHand left)
        {
            int touching = 0, holding = 0;
            if (left.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb))
            {
                for (int f = 1; f < 5; f++)
                {
                    if (!left.GetJoint(k_Tips[f]).TryGetPose(out Pose tip)) continue;
                    float d = Vector3.Distance(thumb.position, tip.position);
                    if (d < pinchStart) touching |= 1 << (f - 1);
                    if (d < pinchRelease) holding |= 1 << (f - 1);
                }
            }
            if (m_Phase == TakePhase.Idle)
            {
                if (touching == 0 || m_Left.fist) return;
                m_Phase = TakePhase.Holding;
                m_TakeStart = Time.time;
                m_SelectMask = touching;
                return;
            }
            if (Time.time - m_TakeStart < selectSeconds) m_SelectMask |= touching;
            if ((holding & m_SelectMask) == 0) m_Phase = TakePhase.Idle; // let go: the part is set down
        }

        /// <summary>The right hand's pointing ray and pinch, for placing parts.</summary>
        void UpdateRightAim(XRHand right)
        {
            RightAimValid = false;
            if (!right.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb) ||
                !right.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)) { RightPinching = false; return; }
            float gap = Vector3.Distance(thumb.position, index.position);
            RightPinching = RightPinching ? gap < pinchRelease : gap < pinchStart;
            Vector3 pinch = trackingSpace != null ? trackingSpace.TransformPoint((thumb.position + index.position) * 0.5f) : (thumb.position + index.position) * 0.5f;
            var cam = Camera.main;
            float scale = trackingSpace != null ? trackingSpace.lossyScale.x : 1f;
            Vector3 head = cam != null ? cam.transform.position : pinch - Vector3.forward * 0.4f * scale;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 shoulder = head + (Vector3.down * 0.18f + Vector3.Cross(Vector3.up, fwd) * 0.17f) * scale;
            Vector3 dir = pinch - shoulder;
            if (dir.sqrMagnitude < 1e-10f) return;
            RightAim = new Ray(pinch, dir.normalized);
            RightAimValid = true;
        }

        void UpdateTake(XRHand left)
        {
            int touching = 0, holding = 0;
            if (left.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb))
            {
                for (int f = 1; f < 5; f++)
                {
                    if (!left.GetJoint(k_Tips[f]).TryGetPose(out Pose tip)) continue;
                    float d = Vector3.Distance(thumb.position, tip.position);
                    if (d < pinchStart) touching |= 1 << (f - 1);
                    if (d < pinchRelease) holding |= 1 << (f - 1);
                }
            }

            if (m_Phase == TakePhase.Idle)
            {
                if (touching == 0 || m_Left.fist) return;
                m_Phase = TakePhase.Recording;
                m_TakeStart = Time.time;
                m_SelectMask = touching;
                m_Times.Clear();
                m_Samples.Clear();
            }

            float age = Time.time - m_TakeStart;
            if (age < selectSeconds) m_SelectMask |= touching;

            if ((holding & m_SelectMask) != 0)
            {
                if (age <= recordSeconds)
                {
                    m_Phase = TakePhase.Recording;
                    if (m_Subsystem.rightHand.isTracked &&
                        m_Subsystem.rightHand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index))
                    {
                        m_Times.Add(age);
                        m_Samples.Add(index.position);
                    }
                }
                else m_Phase = age <= recordSeconds + expireSeconds ? TakePhase.Full : TakePhase.Expired;
                return;
            }

            // Released.
            var target = TargetFor(m_SelectMask);
            if (m_Phase != TakePhase.Expired && target.HasValue && m_Samples.Count >= 3)
            {
                var take = new FlickTake
                {
                    target = target.Value,
                    times = m_Times.ToArray(),
                    path = new Vector3[m_Samples.Count]
                };
                for (int i = 0; i < m_Samples.Count; i++) take.path[i] = ToWorldVector(m_Samples[i] - m_Samples[0]);
                FlickReleased?.Invoke(take);
            }
            m_Phase = TakePhase.Idle;
            m_Samples.Clear();
            m_Times.Clear();
        }

        /// <summary>Palm tilt from rest as a rotation in tracking space, with dead zone and gain.</summary>
        Quaternion Tilt(Vector3 restUp, Vector3 up)
        {
            var full = Quaternion.FromToRotation(restUp, up);
            full.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float a = Mathf.Abs(angle);
            if (a <= tiltDeadZone) return Quaternion.identity;
            return Quaternion.AngleAxis(Mathf.Sign(angle) * (a - tiltDeadZone) * tiltGain, axis);
        }

        /// <summary>
        /// Keep one component of a palm tilt (tracking space: x = player's right, z = player's forward).
        /// sideways keeps the roll (back of the hand tipping left/right), otherwise the pitch (tipping forward/back).
        /// </summary>
        public static Quaternion KeepTiltAxis(Quaternion t, bool sideways)
        {
            Vector3 u = t * Vector3.up;
            if (sideways) u.z = 0f; else u.x = 0f;
            if (u.y < 0.05f) u.y = 0.05f;
            return Quaternion.FromToRotation(Vector3.up, u.normalized);
        }

        Quaternion ToWorldTilt(Quaternion t)
        {
            if (trackingSpace == null) return t;
            Quaternion r = trackingSpace.rotation;
            return r * t * Quaternion.Inverse(r);
        }

        /// <summary>
        /// Degrees the finger points above the back-of-hand plane, knuckle to tip. Flat = about 0,
        /// raised = positive, curled down = negative.
        /// </summary>
        static float FingerAngle(XRHand hand, int finger, Vector3 backOfHand)
        {
            if (!hand.GetJoint(k_Knuckles[finger]).TryGetPose(out Pose knuckle) ||
                !hand.GetJoint(k_Tips[finger]).TryGetPose(out Pose tip))
                return 0f;
            Vector3 dir = tip.position - knuckle.position;
            if (dir.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir.normalized, backOfHand), -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>0 at the calibrated extended distance, 1 at fullPressRatio of it.</summary>
        float Press(HandState state, int finger) =>
            Mathf.InverseLerp(state.extended[finger], state.extended[finger] * fullPressRatio, m_Distances[finger]);

        float FingerDistance(XRHand hand, int finger, Vector3 palmPos)
        {
            // The thumb moves sideways rather than down, so measure it against the index knuckle.
            Vector3 reference = palmPos;
            if (finger == 0 && hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose knuckle))
                reference = knuckle.position;
            return hand.GetJoint(k_Tips[finger]).TryGetPose(out Pose tip)
                ? Vector3.Distance(tip.position, reference)
                : 0f;
        }

        /// <summary>
        /// One Euro filter (Casiez et al. 2012): a low-pass whose cutoff rises with speed, so a still
        /// hand is steady and a moving hand stays responsive. Standard for hand tracking input.
        /// </summary>
        class OneEuro3
        {
            Vector3 m_Value, m_Speed;
            bool m_Init;

            public void Reset(Vector3 v) { m_Value = v; m_Speed = Vector3.zero; m_Init = true; }

            public Vector3 Filter(Vector3 v, float dt, float minCutoff, float beta)
            {
                if (!m_Init) { Reset(v); return v; }
                Vector3 rawSpeed = (v - m_Value) / dt;
                m_Speed = Vector3.Lerp(m_Speed, rawSpeed, Alpha(1f, dt));
                float cutoff = minCutoff + beta * m_Speed.magnitude;
                m_Value = Vector3.Lerp(m_Value, v, Alpha(cutoff, dt));
                return m_Value;
            }

            static float Alpha(float cutoff, float dt)
            {
                float tau = 1f / (2f * Mathf.PI * cutoff);
                return 1f / (1f + tau / dt);
            }
        }

        bool EnsureSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return true;
            SubsystemManager.GetSubsystems(s_Subsystems);
            m_Subsystem = null;
            foreach (var s in s_Subsystems)
                if (s.running) { m_Subsystem = s; break; }
            return m_Subsystem != null;
        }

        Vector3 ToWorldVector(Vector3 v) => trackingSpace != null ? trackingSpace.TransformVector(v) : v;
        Quaternion ToWorldRotation(Quaternion r) => trackingSpace != null ? trackingSpace.rotation * r : r;

        static float YawOf(Quaternion r)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(r * Vector3.forward, Vector3.up);
            return fwd.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }
    }
}

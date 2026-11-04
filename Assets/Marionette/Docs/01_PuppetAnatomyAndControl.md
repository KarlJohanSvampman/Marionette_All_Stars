# Quantum Puppeteers: 01 Puppet Anatomy & Control

Draft 0.2. This version replaces the articulated-skeleton draft (0.1).

## 1. Body: six rigid nodes, textile in between

The puppet is a network of six rigid physics nodes. **Everything between them is textile.** There are no upper arms, forearms, thighs or shins as rigid bodies.

```
              [HEAD]
                 ●
                 ┊  textile (neck)
 [L HAND] ●┄┄┄┄┄●┄┄┄┄┄● [R HAND]
          textile ┊ [CENTER]   textile
                 ╱ ╲
       textile  ┊   ┊  textile
                ●   ●
          [L FOOT] [R FOOT]
```

| Node | Shape (prototype) | Mass (kg) | Role |
|---|---|---|---|
| Head | sphere | 0.6 | balance, head string |
| Center | capsule | 2.0 | main mass, houses both grapple winches |
| Left/Right hand | flattened sphere | 0.3 each | grabbing, palm suction cup, grapple exit |
| Left/Right foot | box | 0.5 each | contact, sole suction cup |

A **textile link** is a distance limit. The cloth can bunch up freely but cannot stretch past its length (rest distance + 5% slack by default). It renders as a cloth tube that sags when slack. Real cloth simulation can come later.

## 2. Attachment points

Every attachment point is an explicit data object (`PuppetAttachment`): id, node, local position, type, strength. It lives as a child transform, so it can be moved in the editor without rebuilding the puppet.

Types: `ExternalControl`, `Grapple`, `Suction`, `Textile`.

### Rule: control points go on the outside

All six marionette strings attach to the **outside surface** of their node, so the player can always see which string moves which part. The grapple lines are the opposite: internal, hidden until they leave the palm.

| String | Outside attachment point |
|---|---|
| Head | crown of the head |
| Center | upper back of the center body |
| Left/Right hand | back of the hand (palm faces down) |
| Left/Right foot | top of the foot (instep) |

The exact positions are tuning values. Example: the head string may feel better slightly behind the crown.

## 3. Default control rig (the starting set)

Each player hand is one **control cross** with three string channels. More inputs are possible later; this is the starting set.

| Player hand | Channel 1 | Channel 2 | Channel 3 |
|---|---|---|---|
| **Left** | puppet left hand | puppet right hand | head / center |
| **Right** | puppet left foot | puppet right foot | center |

Upper body on one hand, lower body on the other.

## 4. Coupling: hand transform + finger

This is the defining control mechanic and must be tested first.

- The **hand transform (H)** is the palm's position and yaw. It moves the whole cross, so it moves *every* string on that cross. Yaw around the palm axis turns the cross: that is rotation and swing.
- A **finger (F)** lifts *its own* string vertically, wherever the hand currently is.

```
string top = H(channel offset) + up × F × liftRange
```

**Consequence:** with the hand flat on the horizontal plane, moving the hand while pressing a finger mapped to a foot moves that foot horizontally (from the hand) **and** vertically (from the finger) at the same time. That is a step. The player doesn't learn "finger X = foot Y". They learn "this finger is this string, and my hand is where I'm puppeteering from".

**Lift a finger, lift its string.** A finger's value is how far it's raised above the back of the hand (knuckle-to-tip angle), compared with its angle at calibration (hands flat, palms down). The first 4° are ignored as jitter (`liftDeadZone`), and 25° above rest is full lift (`fullLiftAngle`). Fingers need only small motions. The older mapping, where pushing a finger down pulls the string, is still available as `fingerAction = Press` on `XRHandsControlInput`. A closed fist still sends no string input.

## 3b. Hands-and-feet strings only (current build)

This replaces the six-string rig in sections 2-3: **only the hands and feet have strings.** The head and center have none.

| Player hand | String 1 | String 2 |
|---|---|---|
| **Left** | puppet left hand | puppet right hand |
| **Right** | puppet left foot | puppet right foot |

- **The body is dragged along by the limbs.** Each hand and foot is sprung to the body (`limbSpring`, max `limbMaxStretch`), so moving one pulls the body, and through it the other limbs. Tested: flicking the right hand moves the body 0.49 H, the head 0.78 H and the other hand 0.57 H.
- **The puppet really stands on its feet.** The leg springs hold the body up, and stiff ankles (`ankleStiffness`) keep it upright over the feet, so leaning is real physics. The balance rule (section 8b) still decides when it has lost it. At rest the body sits at 0.51 H, as built.
- **The head rides on the body** on a stiff neck joint (`neckLimit`, ±15°). Without its string, a cloth neck would let it flop down.
- **Hands and feet can't spin.** Each turns at most `limbPitchLimit`/`limbYawLimit`/`limbRollLimit` (40°/35°/35°) against the body, and spin speed is capped (`limbMaxSpin`). Flicks push through the part's centre of mass, so they add no spin. Tested: after a hard hand flick, no limb had turned more than 58° against the body, across all axes combined.
- **Foot IK:** near the ground, each ankle's target angle follows the surface, measured against the body standing upright. Feet lie flat on slopes, and the ankles still push the body upright. Tested on the 15° ramp: 0.3° off flat with placement, 10.1° without.
- **Older scenes upgrade themselves:** a puppet saved with the old six-string rig is rebuilt this way when Play starts.

## 4b. Palm tilt and the "Pinch & Flick" scheme (experimental, default)

**Palm tilt (all schemes) = upper-body lean.** The left palm's **roll** leans the puppet sideways. The right palm's **pitch** leans it forward and back: fingers pointing down = forward. Each hand passes on only its own axis, measured against the calibrated flat hand and the player's facing. Each cross also tilts with its palm. The first 4° are ignored (`tiltDeadZone`), and `tiltGain` sets the response. See 8a for how lean is held and lost.

**Pinch & Flick** (`XRHandsControlInput.fingerAction = PinchFlick`) replaces finger lifting:

| Left hand pinch (thumb to...) | String |
|---|---|
| index | left hand |
| middle | right hand |
| ring | left foot |
| little | right foot |

1. **Pinch with the left hand** to select a string. A pinch involving two fingers at once is ignored. The selected string turns yellow.
2. **Draw with the right index.** Its path is recorded while you pinch, for up to `recordSeconds` (2 s, adjustable 1-3). A green trail shows it, turning grey when the window is full. Keep pinching more than `expireSeconds` (1 s) past that and the take is cancelled: release and pinch again.
3. **Release to apply (delayed effect): the part is dragged, as if someone pulled it from the other end.** `StringFlick` drives the part along the recorded path (times `forceGain`, 0.7), following both its position and its speed. The drawing is clamped to `reach` (1.15× the arm's or leg's length from its shoulder or hip), so a wave can't stretch the limb out. Tested: a big swing stretches the arm to 1.27× at most.
4. **The dragged limb lets go of the body.**
   - Its spring, damper, cloth and limits go loose.
   - A dragged hand's arm still carries the share of the body's weight it held at rest (`keepSupport`). That force is applied directly at the shoulder.
   - A dragged foot lets go completely. No automatic steps happen while a foot is being placed.
   - The string carries the dragged part's weight (`carryWeight`).
5. **The rest of the body is dragged along, weaker the further along the chain.**
   - As the drag starts, every other part gets a short pulse the way the drag went: first the body, then the head and other limbs `pulseHopDelay` later.
   - Each link further from the dragged limb gets `chainFalloff` (0.6) of the one before.
   - The size is `momentumGain` (0.6) × the drawn speed × how much of `fullRange` the arc covered × how directly it went. Waving back and forth goes nowhere.
   - **Hands don't walk the puppet:** a hand drag pushes the body only `handPulse` (20%) as hard as a foot drag. Tested: a big arm swing moves the body 0.00 H, and a little wave does nothing.
   - **The other arms go loose** for `trailSeconds` (2 s) after letting go: 20% stiffness (`trailLooseness`), still carrying their share of the body. They are swung along `trailSwing` (5×) harder, so they visibly flop after the drag (1.1 H/s on a big swing). Looser than 20% and they stop steadying the puppet sideways, and it falls over. The legs stay firm because they hold the puppet up.
   - After the drag, a hand swings back to its place by itself, then reattaches. A foot stays where it was put, and the body follows it over `reattachSeconds`.
6. **Both crosses freeze during a take** and resume from the frozen pose on release, without a jump.

The headset status panel shows the current step. Switch schemes with `Marionette > Hand Tracking > Controls - Pinch and Flick` / `Controls - Lift Finger`, or with `fingerAction` on the Puppeteer.

## 5. Strings behave like rope

The top of each string is a kinematic handle moved by its cross. The bottom is a rope limit on the outside attachment point. A string only pulls; when the top comes closer, it goes slack. Taut strings render bright and slack strings render dim, so the player can read which strings carry weight.

## 6. Internal grappling lines (not implemented yet)

There are two lines, one per arm. Each winch sits in the center body, the line runs inside the textile arm, and it exits at the palm suction cup (`Grapple_LeftPalm` / `Grapple_RightPalm`). They fire, attach, reel in, swing, rappel and release. See document 04.

## 7. Suction cups

There are four: two palms and two soles (`Suction_*` attachments). Grip depends on the surface: glass/metal excellent, painted wall good, wood moderate, brick poor, dirt/fabric none. See document 04.

## 8. Prototype in this project

- `Marionette > Create Test Scene` builds floor, camera and puppet into `Assets/Marionette/Scenes/MarionetteTest.unity`.
- Keyboard stand-in: WASD/QE move and yaw both crosses. IJKL/UO move only the legs cross. Z X C = left hand / right hand / head. B N M = left foot / right foot / center. R = reset.
- **Finger tracking only, no controllers.**
  - The `Puppeteer` object's `AutoControlInput` uses finger tracking whenever a headset is running, and the keyboard otherwise.
  - Scenes include an `XR Origin (Hands)` rig: head-tracked camera plus hand meshes, no controller objects.
  - OpenXR has the controller profiles off and hand tracking, Meta Hand Tracking Aim and the Hand Interaction Profile on, for Quest and PC.
  - Quest builds declare hand tracking as required at high frequency, so the headset starts the app in hands mode.
  - `Marionette > Hand Tracking > Check Setup` reports the state; `Use Hands Only` reapplies it.
  - Quest Link needs Developer Mode on the headset and Meta Horizon Link app > Settings > Developer > Developer Runtime Features on. If hands show in the Quest menu but freeze as soon as the app has focus, update the headset software: an outdated headset gave apps a frozen hand pose and no joints, in the editor and in builds alike.
  - Don't apply the OpenXR validation page's hand suggestions blindly: the Hand Tracking Data Source feature throws on Link and breaks hand tracker creation.
- **Hand input details:**
  - Finger-per-channel mapping is in the `XRHandsControlInput` inspector (default index, middle, ring).
  - Palm position is smoothed with a One Euro filter (`minCutoff`, `speedCoefficient`).
  - If a hand drops out of view and returns elsewhere, the strings continue from where they were instead of jumping.
  - Hand motion is measured relative to the player (tracking space), then converted to world metres. So being carried by travel doesn't count as hand motion.
- **Travel (`PuppetTravel` on the Puppeteer), edge-push:**
  - Within `reachRadius` (0.35 puppet heights, horizontally) of a cross's calibrated neutral, the hand puppeteers as normal.
  - Reach past it and the player, the view and both crosses' resting points glide that way. Speed is `speedGain` (3/s) × how far past the edge, capped at `maxSpeed` (1 puppet height/s). The strings draw the puppet along, and the fingers still lift feet, so you can walk it while gliding.
  - Pull both hands back inside the radius to stop. Both hands count (averaged), so one hand reaching out travels at half speed.
  - Only finger tracking uses it. The keyboard moves the crosses without limit and doesn't need it.
  - At the default view scale, the radius is about 28 cm of real hand motion from where your hands were at calibration.
  - After travelling, a respawn puts the player back at the same offset from the puppet.

Planned comparison (A/B/C): the `MarionetteTuning` component on the puppet root has a preset dropdown: A Textile (5% slack, soft fold resistance), B Stiff (no slack, strong fold resistance), C Loose (30% slack, none). Same controls for all three. Every physics value (mass, damping, cloth, string springs, lift ranges, gravity scale, timestep, self-collision) is live-editable in Play mode.

## 8a. Drag pulses, automatic steps, staggering and falling (current build)

This replaces the balance rule in 8b. **The player balances only the upper body; the legs step by themselves; a careless drag sends it stumbling.** The feel to aim for is a shoved pedestrian: a few stumbling steps, arms out, then either it catches itself or it goes down.

- **Drag pulse (see 4b).** Dragging a hand or foot yanks the rest of the body the way it went, as hard as the arc was fast and long.
- **Lean** is the torso's own tilt from upright. Dragging a foot ahead doesn't count as leaning back. The ankles (`ankleStiffness` 15, capped at `ankleMaxTorque` 2) hold small leans.
- **Upper body = both palms:** left roll sideways, right pitch forward and back (`leanControl` 0.6, up to `maxLeanControl` 20°). Tip against the momentum to fight it.
- **Automatic steps (`AutoStep`).**
  - The capture point is the centre of mass plus its velocity × √(height / g): where a foot must land to stop the motion. A step starts once it is `stepMargin` (0.02 H) past the edge of the feet. That's earlier than before (0.04 H), so steps catch the body before it's already falling. Starting right at the edge made a grown puppet, which rests near that line, step and topple.
  - Steps are slower and calmer: `stepSeconds` 0.45 s at gnome size (faster when smaller). Each landing absorbs `absorb` (35%) of the body's speed. "Quick steps in a row" are timed from the last landing (`quickStepSeconds`).
  - The string tops follow the body (`stringFollow`), like a puppeteer walking along.
- **Turning on the spot (`StepTurn`), right hand yaw.**
  - Standing still, rotate the right hand about the vertical past `yawThreshold` (15°): clockwise turns right, counter-clockwise turns left.
  - Each turn is `stepAngle` (30°): the foot on the turning side steps round, then the other foot closes up. The body twists to the new heading, and the strings' resting poses swing round, so the follow view comes round behind it.
  - Then it waits `interval` (1 s). If the hand is still rotated, it takes the next turn.
  - The right hand's yaw no longer twists the leg strings. The old smooth two-hand twist turn is off (`PuppetFollowView.twistToTurn`).
  - Tested: holding a 30° rotation for 4 s gives 3 turns, one at a time, with the body turned 65-85° and the view 90°, staying within 0.1 H of the spot, without falling.
- **Staggering (shoved).** Any of these starts it, and controls are overridden:
  - the capture point is more than `shoveMargin` (0.15 H) outside the feet, even mid-step
  - two quick steps in a row (`tripSteps` within `quickStepSeconds`)
  - a step longer than `maxStepLength`
  - the lean limit is passed while moving, stepping or dragging a foot

  While staggering:
  - The arms brace out to the sides, bobbing.
  - The body fights to right itself with limited strength (`staggerUpright` 2.5 Hz, capped at `staggerUprightMax`).
  - It keeps stepping into the momentum, toward the lean when it's tipping, with hurried steps (`staggerStepTime` 60% of a normal step). Each absorbs only `staggerAbsorb` (15%).
  - It catches itself, and controls come back, when the capture point is back over both feet, the body is slower than `catchSpeed`, and it's upright.
  - It falls when a step can't be made or after `staggerSteps` (4). Leaning past `maxLean` × `staggerLeanAllowance` (2.8, so 50°) turns it into a topple.
- **Toppling.** Leaning past `maxLean` (**18°**) while standing still (or ×`steppingLeanAllowance` 1.5 mid-step), or both feet off the ground longer than `airborneGrace`. The arms windmill for `flailSeconds` (0.8 s), then it falls flat that way: front, back or either side. On a fall the strings slip, the limbs go limp, and the body is tipped over.
- **States** (shown on the course readout): Stable, Unstable, Stepping, Staggering, Toppling, Falling.
- **Tested (headless, mouse size, all 34 tests pass, same result on repeat runs):**
  - A small foot step (0.25 H in 0.5 s) stays up, peaking at 8°.
  - A hard step forward (a foot, 1 H in 0.25 s): 4 stumbling steps, arms out, then a fall.
  - Leaning back (right palm tipped back 15°) against that keeps it up 0.86 s instead of 0.34 s.
  - Leaning too far forward, back or right: it steps or staggers, then waves its arms and falls that way.
  - A hard kick (a foot shot forward) takes the leg out from under it, and it usually lands on its back.
  - A big arm swing or a little wave: it sways but doesn't step, walk or fall.
  - **Not seen yet:** a stagger that ends with the puppet catching itself.
- **Test cleanup lesson:** results used to change with test order. Tests now take their objects out of physics immediately (`SetActive(false)` before `Destroy`), and the scale sweep restores the global contact offset it changed.

## 8b. Balance, foot placement and the follow view (Episode 1)

- **Balance (`BalanceMonitor` on the puppet root).**
  - The legs are cloth, so the feet can't hold the body up and the strings always carry it. Balance is therefore a rule: the centre of mass, seen from above, must stay within the outline of the grounded feet (and hands), plus `stringAssist` (0.12 H).
  - States: Stable, Unstable (near the edge, or briefly airborne), Critical (outside, or airborne longer than 0.4 s), Falling (Critical for 0.6 s).
  - Falling makes the strings slip 8x longer, so the puppet really drops. They tighten again after 3 s if nothing respawns it.
  - It's paused while the puppet changes size. The debug outline and dot are drawn at the feet.
- **Feet rest with slight slack.** Foot strings are 0.02 H (at gnome size) longer than their built length, so resting feet press on the ground instead of hovering on taut strings.
- **Placement (`SurfacePlacement` on feet and hands).** Within 0.1 H of a surface, a foot or hand is sprung (6 Hz, critically damped) to lie flat on it. It only tilts, never turns.
- **Follow view (`PuppetFollowView` on the Puppeteer).**
  - The view sits 1.5 H behind and 1.6 H up (eye height) from the strings' resting point, so it doesn't bob with each step. It turns to face the way the strings face.
  - With hand tracking, twisting both hands past 20° turns the view and the resting direction of both crosses at 2°/s per degree past the threshold, up to 60°/s. Hand yaw and hand motion are measured relative to the player.
  - With the keyboard, the view swings round behind the cross.
  - Head movement still shows; drift is recentred at 0.5/s.
- **Episode 1 stage:** `Marionette > Create Episode 1 Stage`. See 03_VideoEpisodeGuide.md for the dimensions.

## 8c. The puppeteer's room (start scene)

`Marionette > Create Puppeteer Room (start here)` builds `Scenes/PuppeteerRoom.unity` and puts it first in Build Settings.

- **You are the puppeteer, in first person:** a stand-in figure in a dark robe. Your head shows only as a shadow; your hands are the tracked hand meshes. **Your own character model:** put it at `Assets/Marionette/Models/Resources/PuppeteerModel` (`.glb` through the glTF importer package, or `.fbx`) and the standing puppeteer uses it, scaled to 1.75 m with its feet on the floor. Set `modelYaw` on `PuppeteerAvatar` to 90 instead of -90 if it faces backwards. **Chair model:** `Assets/Marionette/Models/Resources/ChairModel` replaces the stand-in chair. The stand-in's colliders stay, so the chair can still be pinched, and the armrest panel moves onto the model's right arm (`modelPanelPosition` on `RoomChair`; tune it if the panel floats or sinks into the arm). **3D models are never committed:** `.gitignore` excludes the Models folder and all model files. Until the model is rigged (e.g. Mixamo, uploaded as FBX, with a sitting animation), it stands in a T-pose and the seated puppeteer stays the stand-in figure.
- **Moving about:** point at the floor and pinch. The 0.5 m grid cell under the ray lights up, and you teleport there when you let go. On a desktop the mouse is your right hand: left-click pinches, right-drag looks around.
- **Pointing:** a faint ray always comes from each hand, from the pinch point outward. It turns bright, with a dot, when it's on something you can use: the floor, the chair, a button or a globe pin.
- **Turning:** hold an open hand still at the left or right edge of your view. A ring fills at the hand (0.5 s), then you turn 22.5° that way, and again every 0.45 s while you hold it. The hand turns with you, so it stays at the edge. Not while seated or connected. (`RoomTurn`: `edge`, `notch`, `dwell`, `repeat`.)
- **The chair** is in the middle of the room, facing the puppet's platform in a corner. Point at it and pinch, or just touch it, to get Enter Chair or Cancel (or Exit Chair when you're sitting), then point and pinch or poke the option. You can't teleport while seated.
- **The armrest panel** (right armrest; poke a button with a fingertip, or point and pinch):
  - **Take Control:** the headset view jumps to the third-person camera behind the puppet, scaled to it, and your hands work the strings.
  - **Globe:** shows or hides the hologram globe. Pinch near it to grab and spin it. Pins are places to go: point and pinch a pin, or touch it, to pick it.
  - **Beam:** sends whoever is on the platform to the picked place in a column of light. From a stage the puppet can be beamed onward, or home from anywhere ("The workshop" pin).
- **Destinations:**
  - The Episode 1 stage at gnome size.
  - The desk course, which is made for a mouse-sized puppet, so arriving there shrinks it.
  - Each stage is built far from the room and switched on only while the puppet is there, with its course (checkpoints, falls) running.
- **The puppet** starts gnome-sized (0.575 m) on its platform and can shrink to mouse size.
- **The connection breaks when:**
  - **Anything physically touches you.** While you're connected, your body stays in the chair, and its head and hands follow your real ones. Anything they touch, or anything that bumps into them, breaks the link. The view returns to the chair and the panel says so; press Take Control again. Things already touching when you connect (the finger on the button) don't count. The chair, the floor and the hologram never count.
  - **You let go:** hold your right palm up with a pinch for 1 s (Escape at a desk).
  - **You leave the chair.**

  While nobody is connected, the puppet's strings hold where they are and it ignores flicks, turns and size gestures.
- **Tested headless (6 tests):**
  - The chair menu, sitting and standing.
  - Teleport, refused while seated.
  - Take Control needs the chair. The view lands 1.1 m behind the gnome at its scale, and comes back to the chair at full size.
  - A bump breaks the link and it stays broken until Take Control is pressed again. A touch that was already there at connect is ignored.
  - Beam to the Episode 1 stage lands the puppet 0.01 m from the start pad with the course running, and beaming home lands it back on the platform with the stage switched off.
  - Flicks are ignored while disconnected.
  - None of this has been tried in the headset yet.

## 9. Changing size: grow and shrink

The puppet exists at two sizes for now: **mouse** (default, 12 cm, see 02_ScaleStudy.md) and **gnome** (57.5 cm). More sizes can be added to the `SizeChanger.sizes` list.

| Gesture | Hold | Result |
|---|---|---|
| Both fists, **palms down** | 5-10 s (default 7), no interruption | grow one size |
| Both fists, **palms up** | 5-10 s (default 7), no interruption | shrink one size |

- Opening a hand, turning a palm or tracking loss resets the timer to zero.
- A ring at the puppet's feet fills while charging (green = grow, orange = shrink).
- After a change the gesture must be released before it can charge again, so a long hold doesn't run through every size.
- A closed fist sends no string input. Clenching would otherwise press every finger channel at once.
- Keyboard stand-in: hold **G** to grow, **H** to shrink.
- Once charged, the live puppet **grows or shrinks smoothly in place over 2 s** (`growSeconds`, 0-5) while physics keeps running.
  - It scales about the ground point under it, so the feet stay planted.
  - It grows geometrically with ease-in/out, so a 4.8x change doesn't look slow at the start and rushed at the end.
  - The crosses return to neutral first, so it grows from a resting pose.
  - `growSeconds` 0 gives an instant swap.
- Tuning carries over: all physics values are authored at gnome size and scale automatically (lengths x s, masses x s^3, springs x s^2, dampers x s^2.5), so one tuning works at every size.

**The player scales with the puppet (`ViewScaler` on the Puppeteer).**
- The XR Origin is scaled to puppet height / `apparentHeight` (default 0.8 m). So in the headset the puppet always looks about 80 cm tall. At mouse size the player is shrunk to 0.15x and the desk becomes a landscape. At gnome size the player is 0.72x.
- The origin scales about the puppet's ground point, so the view relative to the puppet doesn't change: same distance and same eye height, measured in puppet heights. During a smooth size change the view grows with the puppet.
- Hand motion scales too, since it's measured through the origin. A 10 cm hand move is always the same fraction of the puppet's height.
- When the puppet respawns at a checkpoint, the view moves with it (`followRespawn`).
- The camera's near clip plane and the hand status panel follow the player's scale.
- Scenes built before this get the component automatically in Play mode when they have an XR rig.

Not done yet: a check that there is room to grow (growing under a shelf will push the puppet through it), and sound/visual flourish during the growth.

## 10. Obstacle course, section 1: Movement and Balance

Built by `Marionette > Create Obstacle Course - Section 1 (mouse size)`. It sits on an office desk with real-world sized objects; the puppet is the small thing.

| # | Checkpoint | Tests |
|---|---|---|
| 0 | Start coaster | |
| 1 | Walk down a lane between two rulers, **stop and hold still 1 s** on a sticky note | walk, stop |
| 2 | Turn right around a coffee mug | turn |
| 3 | Step up onto a 1.5 cm notebook | lift + place |
| 4 | Cross a 3 cm wide ruler bridging two notebooks over spilled coffee | balance beam |
| 5 | Cross sticky-note pads of different heights and tilts over spilled water | uneven platforms |
| 6 | Finish coaster | |

**A fall** is any puppet part touching a fail surface (coffee, water, floor), or the head or center landing on any surface. After a fall the puppet respawns at the last checkpoint after 1.2 s. The on-screen readout shows time, falls, the next checkpoint, size and grow/shrink charge.

# Quantum Puppeteers: Video Episode Guide

Every episode is one **2-hour recording session** that answers **one question**. It ends with a verdict, even when the verdict is "not yet".

## The 2-hour template

| Time | Segment | What happens on camera |
|---|---|---|
| 0:00-0:10 | **Recap / hook** | Where the project is, a 20-second clip of the puppet, today's question |
| 0:10-0:20 | **The question** | State it and say what "yes" looks like: a pass/fail you can test at the end |
| 0:20-1:30 | **Build blocks** | Two or three blocks of about 25 min. Each block: explain the idea, build it, try it, tune it |
| 1:30-1:50 | **Playtest** | Headset on, play the test from start to finish, count falls and time |
| 1:50-2:00 | **Verdict + next** | Did we answer the question? What surprised us? Tease the next episode |

**Rules that keep it inside 2 hours**
- Prepare everything that's boring before recording: headset updated, Link running, scene opens, recording software tested.
- Decide the build list before you press record. Anything new that comes up goes on a "next time" list on screen.
- If a block runs 10 minutes over, cut it and move on. The playtest and verdict always happen.
- Save a known-good copy of the project before each episode. Then a broken build on camera costs a minute, not the episode.
- Show numbers on screen: falls, time, puppet size. They make "fun and challenging" visible.

## Before every episode (checklist)

- [ ] Headset software up to date, controllers off, hand tracking on
- [ ] Meta Horizon Link app > Settings > Developer > Developer Runtime Features on
- [ ] Unity opens the episode's scene, Play works, hand status panel shows TRACKED
- [ ] Recording: headset view + Unity editor + face/voice, 30-second test recording checked
- [ ] Project backed up (copy the folder, or commit if you're using git)
- [ ] Hide the debug panel for the final playtest: untick **show** on Hand Tracking Status

---

## Episode 1: Can puppeteering be fun and still challenging?

**Focus:** physics only. How you get around with the puppet, and when it should lose balance.
No grappling, no suction, no interaction. Those come once movement feels right.

### The question
> Can a player move a physics marionette with their fingers through a small course, so that it feels controllable when you're careful, and falls when you're careless?

**What "yes" looks like (test at 1:30):**
- **Controllable:** you can go up the ramp, across the walkway and down the stairs within about 5 attempts.
- **Challenging:** the first attempts produce falls, and you can explain why each one happened. "I leaned too far" is good. "It just fell over" is bad.
- **Fair:** a careful, slow crossing almost never falls. A rushed one often does.

### Run sheet

| Time | Segment | Content |
|---|---|---|
| 0:00-0:12 | **Concept intro** | The pitch: your hands are the marionette's control cross. Show the prototype: mouse-sized wooden puppet, finger tracking, strings on the outside, grow/shrink, edge-push travel. Say clearly that this is **experimental**: we don't know yet whether it's fun, and this series is how we find out. |
| 0:12-0:22 | **The fundamental question** | Read the question and the "yes" criteria above. Explain why physics comes first: if moving the puppet isn't fun, grappling and climbing won't save it. |
| 0:22-0:45 | **Block 1: the test stage** | Build the course (dimensions below). Walk it with the keyboard to check scale. |
| 0:45-1:10 | **Block 2: when should it fall?** | The balance model (below). Show the debug view: center of mass, support area, state. Push the puppet until it tips over. |
| 1:10-1:30 | **Block 3: feet and hands on the ground** | Foot and hand placement on the ramp and stairs (below). Before/after comparison on the ramp. |
| 1:30-1:50 | **Playtest** | Headset on. Ramp up → walkway → stairs down, 3-5 attempts. Note every fall and its cause. Tune one value live if something is clearly off. |
| 1:50-2:00 | **Verdict** | Answer the question against the criteria. What's next: probably tuning the balance and string support, then grappling. |

### Block 1: The test stage

Two blocks with a narrow walkway between them. Block A has a ramp up its far side. Block B has a staircase down its far side. Route: **ramp up A → walkway → B → stairs down**.

Sizes are in puppet heights (H) so the stage works at any puppet size. The mouse puppet is H = 12 cm.

| Part | Size (H) | At mouse size | Why |
|---|---|---|---|
| Block A and B top | 2.0 H × 2.0 H, height 0.6 H | 24 × 24 cm, 7.2 cm high | Room to stand, turn and recover on top |
| Gap between blocks | 3 H | 36 cm | Long enough that balance matters for several steps |
| Walkway | 3 H long, 0.4 H wide, 0.05 H thick | 36 × 4.8 × 0.6 cm | The feet sit 0.35 H apart, and the player can't bring them closer. 0.4 H leaves almost no room either side. |
| Ramp on Block A | rises 0.6 H at about 15° | about 27 cm long, 2 H wide | Gentle enough to walk, steep enough that the feet must tilt |
| Stairs on Block B | 4 steps, riser 0.15 H, tread 0.6 H, 2 H wide | 1.8 cm rise, 7.2 cm deep | Each tread holds the whole puppet standing. The riser is a deliberate foot lift. |
| Floor below | | desk or floor surface | Touching it is a fall |

**On camera:** explain each number. "The tread is 0.6 H, so the puppet can stand with both feet on one step and stop."

**Built:** `Marionette > Create Episode 1 Stage - Walkway, Ramp, Stairs (mouse size)` → `Assets/Marionette/Scenes/Episode1_Balance.unity`. There are checkpoints at the start pad, the top of the ramp, across the walkway and down the stairs. The pit under the walkway counts as a fall. In a headless test the puppet stands still with both feet down on every part of the stage: start, both blocks, the walkway, each tread and the finish.

**Follow view:** in the headset you stay behind the puppet and a little above it (`PuppetFollowView`). Rotate your right hand about the vertical and the puppet turns on the spot, 30° a step, a second between steps, and the view comes round behind it. Worth showing early in the playtest.

### Block 2: When should it fall? (the balance model)

> **Updated build:** the rule below has been replaced by drag pulses, automatic steps, staggering and toppling. See section 8a of 01_PuppetAnatomyAndControl.md. The story for camera is now: dragging a hand or foot yanks the whole body after it, the legs step to catch it, and you lean the upper body with your palms (left roll = sideways, right pitch = forward/back). Drag too hard and it staggers like a shoved pedestrian, arms out, then falls. Lean past 18° standing still and it waves its arms and falls flat. The feet do the walking: waving the arms sways it but doesn't move it. Rotate the right hand to turn on the spot, a step at a time. The text below is kept as the earlier version, which is useful for showing how the design got here.

**The problem to explain:** a real marionette never falls while the puppeteer holds the strings, because the strings carry its weight. Our puppet makes this even more extreme. Its legs are cloth, which can only pull and never push, so its feet *can't* hold its body up. The strings always carry the body. Physically it could never fall. (We tried "strings carry only 60%": the puppet just folds up. That's a good on-camera discovery.)

**So balance is a rule, not just physics:**
1. **The puppeteer can only hold the puppet while its body is over its feet.**
2. **Support area:** the outline around every foot (and hand) touching the ground. It's drawn on the ground in the scene.
3. **Center of mass** projected straight down. It's the dot in the scene, coloured by state.
4. **String assist:** the dot may stray 0.12 H past the outline before it counts as off balance. That's how much the strings help.
5. **State from the distance between them:**

| State | Center of mass is... | Feedback |
|---|---|---|
| Stable | well inside the support area | none |
| Unstable | near the edge | puppet wobbles, string creaks |
| Critical | just past the edge | strings strain, short warning |
| Falling | past the edge for longer than a moment | strings go slack, puppet falls: turn over |

6. **Both feet off the ground** for more than 0.4 s also counts as Critical. A step or a hop is fine, but dangling isn't. Brief scrapes against an edge don't reset an off-balance puppet.
7. **Falling = the strings slip.** They go slack and the puppet really drops. If nothing respawns it, they tighten again after 3 s.
8. **Failure** = head or center touching a surface, or touching the pit. Falling isn't automatically failure: later, a grapple will let you recover mid-fall.

**Tuning knobs to show** (`BalanceMonitor` on the puppet): `stringAssist`, `unstableMargin`, `criticalSeconds`, `airborneGrace`. The course readout shows the current state.

**Tested headless:** a small lean on the walkway stays Stable. Lifting both feet makes it fall. A big careless swing sideways goes Critical, the strings slip, and it drops into the pit.

**Demo:** stand the puppet on the walkway and slowly move one hand sideways until it goes Stable → Unstable → Critical → Falling. That shot answers "when should it lose balance" visually.

### Block 3: Feet and hands on the ground (placement / IK)

**Explain first:** this puppet has no rigid legs or arms. Its six rigid nodes are joined by cloth. So "IK" here doesn't mean bending knees and elbows. It means:
- **Foot placement:** when a foot is near the ground, it turns to lie flat on the surface underneath (ramp, step edge) instead of landing on a corner. It also settles onto the surface instead of bouncing.
- **Hand placement:** when a hand is lowered onto the ramp or a step, it lies flat on the surface, so the puppet can crawl up or steady itself.
- **Step assist (optional):** a lifted foot that comes down over a step edge lands on the tread, not half off it.

All of this is a gentle physical nudge (a spring toward the surface angle), not an animation. The puppet can still fall.

**Demo:** walk up the ramp with placement off (untick `SurfacePlacement` on the feet) and then on.

**From testing (hands-and-feet-only puppet):** the puppet now stands on stiff ankles, so without placement its feet stay level with its body and stick out at an angle on the ramp: **10.1° off the slope**. With placement, the ankle's target angle follows the surface: **0.3° off**. That's a clear before/after even while standing still.

**Built:** `SurfacePlacement` (feet and hands) and `GroundContact` (what balance counts as standing). Step assist isn't built yet.

---

## Later episodes (one question each)

| # | Question | Builds on |
|---|---|---|
| 2 | Does tuning the balance make the course feel fair? Playtesters, fall counts, string support values | Episode 1 verdict |
| 3 | Is the grappling line fun? Fire, attach, reel in, swing across a gap | Balance, hand suction cup |
| 4 | Can you climb with suction? Surface grip table, wall walking, glass vs. wood | Grapple, foot suction cups |
| 5 | Can the puppet do things? Doors, drawers, cabinets with one hand anchored | Suction + hand placement |
| 6 | Is falling a skill? Deliberate falls rescued by a grapple | Grapple + balance states |
| 7 | Course section 2: can it be completed in several different ways? | Everything above |
| 8 | Grow and shrink as a puzzle: why would you change size mid-course? | Course + size change |

# Quantum Puppeteers: 02 Scale Study (mouse size)

**Result: the mouse-sized puppet is 12 cm tall** (feet to crown). The gnome size stays at 57.5 cm.

## How it was measured

`Tests/ScaleSweepTests.cs` (PlayMode test) builds the same puppet at seven heights side by side. Each one runs the same scripted routine at a fixed 90 Hz:

1. Settle (0-1.5 s).
2. Quiet idle (1.5-3 s): rest jitter.
3. Idle with **2 mm of simulated hand-tracking jitter** plus finger jitter (3-4.5 s). The jitter is in absolute metres, because tracking error doesn't shrink with the puppet.
4. A step (4.5-6.5 s): lift the left foot, move the legs cross forward 0.35 H, then lower the foot.
5. Settle (6.5-8 s).

Motions scale with the puppet; jitter doesn't. %H means "as a percentage of the puppet's height".

| Height | Stable | Upright after step | Sag %H | Rest jitter %H | Wobble from 2 mm hand jitter %H | Foot lift %H | Foot advance %H | Body advance %H | Max string stretch % | Peak speed H/s | Head swing period s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 5 cm | yes | yes | 0.0 | 2.61 | 3.98 | 23.9 | 25.4 | 19.6 | 3.4 | 2.8 | 0.42 |
| 7 cm | yes | yes | 0.1 | 2.12 | 3.13 | 24.0 | 27.8 | 21.9 | 1.7 | 1.6 | 0.49 |
| 9 cm | yes | yes | 0.2 | 1.70 | 2.55 | 24.0 | 30.0 | 20.3 | 1.9 | 2.1 | 0.56 |
| **12 cm** | yes | yes | 0.4 | **1.37** | **2.10** | 24.0 | 32.9 | 20.4 | 1.4 | 1.4 | 0.65 |
| 15 cm | yes | yes | 0.7 | 1.40 | 1.38 | 24.0 | 28.2 | 20.7 | 1.2 | 0.9 | 0.72 |
| 25 cm | yes | yes | 1.0 | 1.33 | 1.48 | 24.0 | 30.2 | 21.0 | 1.2 | 0.8 | 0.94 |
| 57.5 cm | yes | yes | 1.1 | 1.38 | 1.32 | 24.8 | 22.4 | 20.5 | 1.2 | 1.0 | 1.42 |

## Reading it

- **Physics works at every size.** Nothing exploded, every puppet stayed upright and completed the step, and foot lift and body advance are nearly identical. That confirms the automatic scaling in `MarionetteTuning` (lengths x s, masses x s^3, springs x s^2, dampers x s^2.5).
- **Rest jitter** bottoms out at about 1.35% H from 12 cm upward. Below that it climbs: 1.7% at 9 cm, 2.6% at 5 cm. **12 cm is the smallest size that is as calm as the big puppets.**
- **Hand-tracking jitter** is the real cost of being small. 2 mm of noise wobbles a 5 cm puppet 4% of its height, a 9 cm one 2.6%, and a 12 cm one 2.1%. From 15 cm up it is lost in the baseline.
- **Strings get stretchier** relative to size at 5 cm (3.4%), so the puppet feels rubbery.
- **Swing period** scales with the square root of size. At 12 cm the head swings back and forth in 0.65 s versus 1.42 s at gnome size, so small puppets look quicker and twitchier. That is physically correct (small things move fast), but it is a tuning lever: lowering `gravityScale` at mouse size slows it down.

## Decision

- **Mouse size = 12 cm.** It is the knee of the curve: the smallest puppet with baseline stability, and only modestly more hand-jitter sensitive than larger sizes. That is roughly a mouse standing on its hind legs.
- 9 cm stays possible, but expect about 25% more visible wobble. Below 7 cm the controls will feel noisy on Quest without stronger input smoothing.
- At 12 cm the course in section 1 fits as intended:
  - The 1.5 cm notebook step is 12.5% H. The foot lifts 24% H (2.9 cm), so there is margin.
  - The puppet's stance is about 4.2 cm wide, wider than the 3 cm beam. That makes the beam a real balance test.
  - The rulers leave a 10 cm lane, which fits the 8.4 cm hand span.

## Next measurements

- Repeat on device with real Quest hand data, not simulated jitter.
- Try `gravityScale` 0.6-0.7 at 12 cm to see if a slower swing reads better.
- Add input smoothing (One Euro filter) and re-run: it may allow going smaller than 12 cm.

Rerun anytime: Unity Test Runner > PlayMode > `ScaleSweep`, results in `Logs/ScaleSweep.md`.

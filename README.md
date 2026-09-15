# Gradient Lander

## Map selection

The game opens with two modes:

- **Quartic Valley** is fitted to the supplied objective-function graph. Its nine-section hidden route contains six ordered platform landings, with two gradient-descent iterations applied between each landing using `x(next) = x - learningRate * f'(x)`. Three ordinary route stops are paired with an identical-looking decoy, five additional decoys are staggered through the quieter map heights, and exactly three full-width checkpoints provide occasional guidance. Each checkpoint carries a coin at its calculated step. A collected coin adds `1.0000` to the reward only when the ground target is reached. Skipping an ordered route stop, landing on a decoy, or missing a checkpoint coin invalidates the challenge reward.
- **Random** preserves the original randomized platforms and ground target with no predetermined solution path.

Every ship position is recorded. A cyan line remains on the scrolling map during play, including while the ship is stopped on a platform, so the route builds continuously from launch to ground. After reaching the ground, the camera zooms out to show the complete map and flown route. Quartic Valley also reveals its computed correct route for comparison; Random shows only the player's route. The review provides **Try Again** and **Select Map** controls.

## Ship skin shop

The map-selection screen includes a **Ship Skin Shop**. Successful landing rewards stored in the bank can purchase two persistent cosmetic skins:

- **Spotty Dog** costs `5.0000` and adds floppy dog ears, a cream hull, and polka dots.
- **Comet Cat** costs `7.5000` and adds cat ears, a lavender hull, and a curled tail.

Purchased skins remain unlocked, and the equipped skin is restored the next time the game starts. The original Classic ship is always available at no cost.

A scrolling Unity game that teaches the intuition behind gradient descent. The player chooses a shared weight (`w`) and base thrust bias (`b`), optionally lands on narrow platforms, and tries to hit a hidden target on the lunar ground.

## Play in Unity

1. Open this folder in Unity `6000.3.11f1` (Unity 6.3).
2. Open `Assets/Scenes/GradientLander.unity`.
3. Press **Play**.
4. Enter values from `-3` to `3` for `w` and `b`, then choose **Descend**.
5. When the ship overlaps a platform, review the landing cost, adjust the parameters, and continue.

The continuous map contains several small platforms at different heights. Platform positions and the final ground target are randomized for every new mission. If the calculated path overlaps a platform, the ship lands and the player can retune. If none overlap, the same trajectory continues while the camera scrolls smoothly downward, with no landing cost or visible section transition.

## Model

Horizontal positions are normalized from `0` to `1` across the field.

```text
e   = x_target - x_drone
P_L = b + w e
P_R = b - w e

J = distance² + 0.1(P_L² + P_R²)
```

The horizontal response uses half of the differential thruster command:

```text
x_land = x_start + 0.5(P_L - P_R)
```

The thrust equations are evaluated when the player launches from the start or continues from a platform. That trajectory keeps the same horizontal direction until the next actual landing; invisible map boundaries never recalculate or reverse it. Platform collision is based on the ship's calculated horizontal position when it reaches each platform height. The mission begins with a reward budget of `4.0000`, and only actual landing costs are deducted.

```text
remaining reward = max(0, 4 - sum of all landing costs)
```

The target is rendered only as part of the final ground and never appears in the air. Only a successful final target landing deposits the remaining budget into the persistent bank. A miss still reports the complete cumulative cost but deposits nothing. Values remain signed, and a blue flame indicates a negative/reverse thruster command.

## Project structure

- `Assets/Scripts/LanderMath.cs` — seeded random layouts, deterministic thrust, world-depth collision checks, and landing costs
- `Assets/Scripts/LunarLanderController.cs` — optional landing flow, continuous descent, reward, and persistent bank
- `Assets/Scripts/ProceduralGraphics.cs` — continuous scrolling camera, randomized platforms, atmosphere, rocket, and ground-only target
- `Assets/Editor/ProjectSetup.cs` — scene/build-settings generator

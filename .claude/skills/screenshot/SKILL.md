---
name: screenshot
description: Run the game on a virtual display and take screenshots to check how something looks (view model, animations, HUD, effects, other players through lag). Use after any visual change, and whenever you need to see the game instead of guessing.
argument-hint: "[what to look at]"
---

# Look at the game

What to check: $ARGUMENTS

`tools/screenshot.sh` builds the project, runs the game under `xvfb-run` and prints the paths of
the PNGs it saved. Save them in your scratchpad directory (`--out=<scratchpad>/shots`), never in
the repo. Read the script's header for every option.

## Pick a scene

| To see | Command |
|---|---|
| You fighting a still bot, first person (default) | `tools/screenshot.sh --out=DIR` |
| A bot that fights back | `tools/screenshot.sh --out=DIR -- --practice --bot=fight --dummy-mode=fight` |
| Several strafing bots | `tools/screenshot.sh --out=DIR -- --host --bot=fight --dummies=3 --dummy-mode=strafe` |
| A player as seen through lag | `tools/screenshot.sh --out=DIR --host-args="--dummies=1 --dummy-mode=fight" -- --join=127.0.0.1 --bot=fight --ping=150 --jitter=20` |
| The main menu | `tools/screenshot.sh --out=DIR -- --name=Me` |
| The pause menu (Esc) | `tools/screenshot.sh --out=DIR -- --practice --bot=fight --pause` |
| The Twists screen, with twists on | `tools/screenshot.sh --out=DIR -- --practice --bot=fight --twists --strafe=130 --grip=50 --wasted-clicks=0.15` |
| The settings screen | `tools/screenshot.sh --out=DIR -- --name=Me --settings` (or in a match: `-- --practice --bot=fight --settings`) |

- `--at=3` sets when to shoot (seconds after the match starts). For motion, take a burst:
  `--at=3,3.05,3.1,3.15` (one frame every few ticks; a sword swing lasts 0.3 s).
- `--bot=fight` makes "you" play by yourself, so the camera moves and attacks like a player.
- More launch options (ping, loss, names, bot modes) are listed in `scripts/Autoload/LaunchArgs.cs`.
  For a pose or situation no option reaches, add a launch option for it rather than editing the
  scene by hand.

## Check the result

1. Open every PNG with the Read tool and compare it with what you expected. Say concretely what
   is wrong (too big, wrong angle, clipped, hidden behind the HUD) before changing anything.
2. If the script reports errors in the game log, read the log it names.
3. Fix, then shoot the same scene again to confirm.
4. Send a screenshot to the owner (SendUserFile) only when it shows them something new they'd
   want to see, such as a visual change they asked for. Don't send debugging shots.

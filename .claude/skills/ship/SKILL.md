---
name: ship
description: Check, commit and push to main, then wait for the Windows build and confirm it's published. Use at the end of any change the owner should play, and to save finished work.
argument-hint: "[optional note on what changed]"
---

# Ship a change

Ship everything in the working tree to `main` and confirm the owner can download it.
Notes from the caller, if any: $ARGUMENTS

Repo: `jonathancheng31/uhc-pvp`. Pass `--repo jonathancheng31/uhc-pvp` to every `gh` command.

## 1. Look at what's changing

- `git status --short` and `git diff --stat`. Nothing from `build/`, `.godot/`, `bin/`, `obj/`,
  logs or screenshots gets committed. New assets need their `*.import` and `*.uid` files.
- If assets were added or regenerated, run `godot --headless --path . --import` first.

## 2. Run every check (all must pass)

```bash
dotnet format UhcPvp.csproj          # fixes formatting; CI rejects unformatted code
dotnet build UhcPvp.csproj --nologo  # warnings are errors
tools/run_tests.sh
tools/smoke_test.sh                  # ~20 s; always run it, gameplay changes break it in surprising ways
```

If something fails, fix the cause (don't weaken a test to make it pass) and run the checks again.

## 3. Bring the docs and version up to date

- `docs/design.md`: decisions changed in this work, milestone marked done, and the
  "Current status / next up" section.
- The feature doc (`docs/combat.md`, `docs/movement.md`, `docs/netcode.md`, ...) if behavior changed.
- `docs/playtest-notes.md`: move items this change fixes to "Done", with the new version.
- `CLAUDE.md` if commands, layout or conventions changed.
- Version (`config/version` in `project.godot`, shown in the menu and debug screen):
  the middle number for a milestone (0.3.0 → 0.4.0), the last number for fixes and tweaks the
  owner will notice (0.3.0 → 0.3.1). Leave it alone for docs- or tooling-only changes.

## 4. Commit and push

- One commit per logical change. Imperative subject under 72 characters; the body says what
  changed for the player and why. End the message with the attribution lines this session's
  instructions give. Never put a model name in the commit.
- `git push -u origin main`. On a network error retry after 2, 4, 8 and 16 seconds. If the push
  is rejected because `main` moved, `git pull --rebase origin main`, rerun step 2, push again.
  Never force-push `main`.

## 5. Wait for the Windows build

```bash
sha=$(git rev-parse HEAD)
gh run list --repo jonathancheng31/uhc-pvp --commit "$sha" --json databaseId,status,conclusion
gh run watch <databaseId> --repo jonathancheng31/uhc-pvp --exit-status   # use a 10-minute timeout
```

The run can take a few seconds to show up; list again if it's empty. A run usually takes
about 2 minutes. If it fails: `gh run view <databaseId> --repo jonathancheng31/uhc-pvp --log-failed | tail -100`,
fix it, and go back to step 2.

Then confirm the download really is this commit:

```bash
gh release view dev-build --repo jonathancheng31/uhc-pvp --json targetCommitish,assets \
  --jq '.targetCommitish, (.assets[] | "\(.name) \(.size)")'
```

`targetCommitish` must equal `$sha` and `UHC-PvP-Windows.zip` must be listed.

## 6. Tell the owner

Only once the build is confirmed. Plain language, outcome first:

1. "New build ready (version X)" with the link
   https://github.com/jonathancheng31/uhc-pvp/releases/download/dev-build/UHC-PvP-Windows.zip
2. What's new, in player terms, and how to try it (keys, menu options, practice bot mode).
3. What feedback would help most (one or two specific questions).

For docs- or tooling-only changes, skip the build announcement: say what was saved, in one line.

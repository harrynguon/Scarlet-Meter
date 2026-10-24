---
name: scarlet-upstream-update
description: Move Scarlet Meter's upstream RATmeter submodule to a newer release after a game patch (or when the meter's update notice says a release is out) - releases only, never upstream dev - then build, test, smoke with demo data, fix only the engine's mapping if upstream changed an API, and ship it as a pull request. Use when the player says the meter broke after a patch or a new RATmeter release exists.
---

# Updating upstream RATmeter

Upstream is `Kuroukihime/AIon2-Dps-Meter`, the `upstream/` submodule, pinned to a release tag. Parser fixes for a game patch arrive there; Scarlet takes them by moving the pin. Never edit a file under `upstream/` and never use upstream's `dev` branch or an unreleased commit.

## 1. Find the release

```powershell
gh release list -R Kuroukihime/AIon2-Dps-Meter --limit 5
gh release view <tag> -R Kuroukihime/AIon2-Dps-Meter
```

Read the release notes for packet, settings, history-database or game-data changes. Compare with the pin in `docs/ARCHITECTURE.md` (first paragraph).

## 2. Move the pin on a branch

Work in a worktree (see the scarlet-ship skill), then:

```powershell
& '<worktree>\scripts\Update-Upstream.ps1' -Tag <tag>
```

It refuses non-release tags, moves and stages the submodule, and runs the build and tests.

The player may already have run `scripts/Update-Meter.ps1`, which leaves the pin move committed on a local branch `update/upstream-<tag>` (and a portable zip). Start the PR from that branch rather than moving the pin again: add the docs below, then ship it.

## 3. When something breaks

`docs/ARCHITECTURE.md`, "What breaks on an upstream update, and where", maps each kind of failure to the one place to fix it:

- Compile errors in `src/ScarletMeter.Engine/Upstream` (an upstream API the engine maps changed): fix the mapping there only. Keep upstream types out of the engine's public surface.
- `EngineRegistrationTests` fail resolving the graph: upstream added a required service; register it in `ScarletEngineServiceCollectionExtensions.cs`.
- `IAppSettingsService` gained a member: implement it in `Upstream/UpstreamSettingsBridge.cs`, inert unless it is a fight rule.
- Game data moved or was renamed: `Test-PublishLayout.ps1` and the engine tests say which file.
- The root `Directory.Build.props` overlay (for example the SQLite bundle override): remove an entry once upstream ships the fix itself, in the same change.

## 4. Check and ship

- Both builds and tests, a Release publish with `Test-PublishLayout.ps1`, and a smoke run with `--demo` (rows, details, history) via the scarlet-smoke skill.
- Read the upstream diff, not only the release notes: a fix can rewrite more than it says (v1.11.5.341's boss HP fix also rewrote how a summon's owner is read). Record every parsing change that reaches the engine, and add an in-game item for each.
- Update `docs/ARCHITECTURE.md` (the pinned release and commit) and add a `docs/WORKLOG.md` entry naming the release, what upstream changed and what was checked. The game itself still needs the player: add or reset the relevant in-game items in `docs/HANDOFF.md`.
- Ship through the scarlet-ship skill. Upstream bumps touch capture and parsing, so run a deep `scarlet-auditor` pass.
- After merging, the player reinstalls with `scripts\Install-Meter.ps1` (when present).

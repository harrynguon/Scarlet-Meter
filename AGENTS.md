# Repository instructions

Read docs/README.md and docs/HANDOFF.md before changes. Update relevant docs and docs/WORKLOG.md for each meaningful change: reason, actual checks, unresolved issues and next action. Keep architecture and reproducible commands current. Do not infer acceptance from unchecked items.

Never embed, print, log or commit secrets, authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures and validate untrusted data.

## Paths

Never rely on a relative path or on the starting directory: anyone, on any machine, must be able to pick up this work. Shell commands, scripts and file operations use absolute paths derived from a trusted root: this repository (the script's own location or the git root), the application's base directory, or the platform's per-user data directory. Never hard-code a developer's home, drive letter or machine-specific location in code, scripts or documentation. Module references, documentation links and web assets stay repository-relative, which is portability, not reliance on the working directory.

## Upstream boundary

`upstream/` is a git submodule of Kuroukihime/AIon2-Dps-Meter (RATmeter), pinned to a release tag. Never edit a file under `upstream/`. Move the pin only to a release tag, with scripts/Update-Upstream.ps1, never to `dev` or an unreleased commit. `ScarletMeter.Engine` is the only project that references upstream, and it keeps every upstream type out of its public surface; `ScarletMeter.App` references the engine only. tests/ScarletMeter.Tests guards both rules; keep those tests passing. A fix that upstream code needs goes to upstream or to the harrynguon/AIon2-Dps-Meter fork, not here. The root `Directory.Build.props` is the one exception: MSBuild imports only the nearest such file, and Scarlet's projects have theirs in `src/` and `tests/`, so the root file reaches only upstream's projects. It carries minimal, generic build fixes (never behaviour), each listed in docs/ARCHITECTURE.md ("Upstream overlay") and guarded by a test.

## Review and merge

Changes reach `main` through a PR and a squash merge, after CI passes and with the player's go-ahead. Docs-only changes (Markdown only) need no review beyond CI. Run checks appropriate to the change; a rendered window does not validate combat.

## Structure and comments

Write modular, reusable code with focused responsibilities. Keep capture and parsing (upstream), the engine's mapping, settings, storage and UI separate. Prefer shared helpers over duplication. Briefly comment each new or changed module and function with its purpose, and explain non-obvious behaviour or invariants without narrating every statement. Keep comments current. Add boundary and compatibility tests and maintain documentation as work proceeds.

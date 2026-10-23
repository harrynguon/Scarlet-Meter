# Repository instructions

Read docs/README.md, docs/HANDOFF.md and docs/LESSONS.md before changes. Update relevant docs and docs/WORKLOG.md for each meaningful change: reason, actual checks, unresolved issues and next action. Keep architecture and reproducible commands current. Do not infer acceptance from unchecked items.

## Security

Security comes first in every change. Never store sensitive data in the repository, in code, configuration, fixtures, scripts, docs, commit messages or PR text: no API keys, tokens, passwords or other credentials, no authentication data, personal data or progress, packet captures or private combat logs. Never print or log them either. Use synthetic fixtures, validate untrusted data (network answers, `settings.json`, imported files) and preserve recovery protections (backups, keeping unreadable files aside, read-only for newer formats). Paths in the repository never depend on the starting directory (Paths, below).

## Lessons

When a mistake is found, by a test, the auditor, Codex, a smoke run or the player, record it in docs/LESSONS.md in the same change that fixes it: what went wrong, why it got through, and the guard that now catches it (a test, a check, a rule). Read docs/LESSONS.md before starting work, and prefer adding a guard to adding a note.

## Paths

Never rely on a relative path or on the starting directory: anyone, on any machine, must be able to pick up this work. Shell commands, scripts and file operations use absolute paths derived from a trusted root: this repository (the script's own location or the git root), the application's base directory, or the platform's per-user data directory. Never hard-code a developer's home, drive letter or machine-specific location in code, scripts or documentation. Module references, documentation links and web assets stay repository-relative, which is portability, not reliance on the working directory.

## Upstream boundary

`upstream/` is a git submodule of Kuroukihime/AIon2-Dps-Meter (RATmeter), pinned to a release tag. Never edit a file under `upstream/`. Move the pin only to a release tag, with scripts/Update-Upstream.ps1, never to `dev` or an unreleased commit. `ScarletMeter.Engine` is the only project that references upstream, and it keeps every upstream type out of its public surface; `ScarletMeter.App` references the engine and dependency-free Scarlet modules (`ScarletMeter.WorldEvents`), never upstream. tests/ScarletMeter.Tests guards both rules; keep those tests passing. A fix that upstream code needs goes to upstream or to the harrynguon/AIon2-Dps-Meter fork, not here. The root `Directory.Build.props` is the one exception: MSBuild imports only the nearest such file, and Scarlet's projects have theirs in `src/` and `tests/`, so the root file reaches only upstream's projects. It carries minimal, generic build fixes (never behaviour), each listed in docs/ARCHITECTURE.md ("Upstream overlay") and guarded by a test.

## Review and merge

Changes reach `main` through a PR and a squash merge, after CI passes and with the player's go-ahead. Docs-only changes (Markdown only) need no audit. Otherwise run the `scarlet-auditor` agent (.claude/agents/scarlet-auditor.md): a standard pass by default, a deep pass at high effort for window lifecycle or WebView2 hosting, threading, saved data, validation or security, capture or the engine's upstream mapping, or package versions. Address its findings before merging; small fixes that apply its own suggestions need no second audit. Every PR also gets the Codex bot's automatic review (chatgpt-codex-connector): before merging, wait until its summary comment says the review completed (or it reacts with a thumbs-up for no findings), read every inline comment and review on the PR (`gh api repos/harrynguon/ScarletMeter/pulls/<n>/comments`, `.../pulls/<n>/reviews`, `.../issues/<n>/comments`), fix each valid finding or answer why not, and record them in the WORKLOG entry. After pushing fixes, comment `@codex review` for a fresh review and check it the same way. Run checks appropriate to the change; a rendered window does not validate combat. The repeatable workflows are skills in .claude/skills: `scarlet-ship` (branch to merge), `scarlet-smoke` (driving the UI without the game) and `scarlet-upstream-update` (moving the upstream pin).

## Structure and comments

Write modular, reusable code with focused responsibilities. Keep capture and parsing (upstream), the engine's mapping, settings, storage and UI separate. Prefer shared helpers over duplication. Briefly comment each new or changed module and function with its purpose, and explain non-obvious behaviour or invariants without narrating every statement. Keep comments current. Add boundary and compatibility tests and maintain documentation as work proceeds.

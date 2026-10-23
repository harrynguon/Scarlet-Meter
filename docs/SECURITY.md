# Security, provenance and game terms

Never embed, print, log or commit credentials, tokens, game authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures. Treat anything read from the network or from files as untrusted; upstream validates packet fields before use, and the engine validates paths at its boundary.

Filesystem operations use absolute paths derived from trusted roots (AGENTS.md, "Paths"). The meter writes only under its data directory (per-user by default, or `--data-dir`): history database, logs and the WebView2 browser profile (ARCHITECTURE.md, "Storage").

Raw packet logging, which upstream offers as a developer setting and which writes other players' data to disk, is not reachable here: the settings bridge reports it off and no packet writer is registered. The history database still stores other players' names and combat statistics for completed boss fights, as upstream does.

## Dependencies

Restore runs with NuGet audit advisories as errors (`-warnaserror:NU1901..NU1904`). Upstream's `AionDpsMeter.Services` at the pinned release pulls the vulnerable native SQLite package (CVE-2025-6965, GHSA-2m69-gcr7-jv3q); Scarlet's root `Directory.Build.props` overlays it with SQLitePCLRaw.bundle_e_sqlite3 3.0.5, mirroring the override the harrynguon/AIon2-Dps-Meter fork carries in its copy of that project (ARCHITECTURE.md, "Upstream overlay"). Upstream itself does not carry it. Audit results are time-dependent, not a security review.

## Provenance

Upstream: https://github.com/Kuroukihime/AIon2-Dps-Meter at the release tag recorded by the `upstream` submodule (v1.11.4.334, commit 57974f8, at this baseline), GPLv3, "RATmeter, Copyright (c) 2026 Kuroukihime" (`upstream/LICENSE`). Scarlet Meter is GPLv3 as well, because the built program links upstream's code; private use carries no obligation, and any distribution must ship the source under the same licence. The root `LICENSE` carries Scarlet Meter's own notice and names upstream's.

`src/ScarletMeter.App/DeferredWindowClose.cs` and `tests/ScarletMeter.Tests/DeferredWindowCloseTests.cs` are ported from the harrynguon/AIon2-Dps-Meter fork (GPLv3), where they are fork-owned files.

World events (`src/ScarletMeter.WorldEvents`, `tests/ScarletMeter.Tests/WorldEvents/`):

- The schedule engine (rules, validation, override merging, recurrence) and its test cases are ported from the harrynguon/AIon2-Dps-Meter fork's World Companion (GPLv3; `AionDpsMeter.Companion/wwwroot/schedule.mjs`, `presets.mjs` and `state.mjs`, `tests/companion.test.mjs` and `tests/companion-state.test.mjs`, at 4feb72baf86c8f5e4eaf8e03684401f1249de910), where they were written for the fork. The alert planner is new here.
- The Global preset's times and window lengths follow taengu/A2Tools-Plugins event-timers data at b11f345c93494a2b6a77142002dc26a656b610d2 (https://github.com/taengu/A2Tools-Plugins), as the fork's docs/TIMER-COMPARISON.md records: schedule parameters only, no code. That repository's licence terms have not been reviewed.
- The legacy defaults are adapted from Byanski/aion2timeroverlay at 2a5727ecdf5487e9becb6b3a756819f9d14ff564 (https://github.com/Byanski/aion2timeroverlay), whose package.json declares MIT; the snapshot contains no standalone MIT text. The fork's SECURITY.md ("Provenance") has the full record, including the cube coordinates and maps the fork once bundled from there, none of which are here.
- The schedules are **not verified against Global Asia / Nezekan** (ARCHITECTURE.md, "World events"). They are scheduled times, not live reports from the game.

Confirm the schedule data's redistribution terms before any public binary release.

## Game terms

The fork's assessment at https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md ("Game terms") applies unchanged: NC's EULA forbids reverse engineering and access outside its published interfaces, and the AION 2 Global Operation Policy sanctions creating or distributing unauthorised programs with an integrated account ban. A passive meter arguably neither circumvents protection nor interferes with operations, but building and running it is the player's risk. Re-read both documents before any public release or any new packet decoding. This repository is private; keeping it private does not change the risk of running the meter.

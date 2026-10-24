# Security, provenance and game terms

Never embed, print, log or commit credentials, tokens, game authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures. Treat anything read from the network or from files as untrusted; upstream validates packet fields before use, and the engine validates paths at its boundary.

Filesystem operations use absolute paths derived from trusted roots (AGENTS.md, "Paths"). The meter writes only under its data directory (per-user by default, the `data` folder beside the exe for a portable copy, or `--data-dir`), apart from the native libraries every single-file .NET app unpacks to `%TEMP%\.net`: history database, settings, logs, the imported background picture and the WebView2 browser profile (ARCHITECTURE.md, "Storage").

`settings.json` is untrusted input: it passes one validation boundary (`SettingsNormalizer`), is size-limited, and can only name a background picture the meter itself imported, so it cannot point the page at another file. A damaged file is kept aside, never deleted; when it (or the backup) cannot be moved aside, or the file comes from a newer build, saving is off for that run so nothing overwrites it (ARCHITECTURE.md, "Settings"). The pages reach local images only through two WebView2 virtual hosts that each map a single folder (the backgrounds folder and the class icons beside the executable), never the database, logs or settings. A Content-Security-Policy allows scripts only from the app and images only from the app, those hosts and NC's game-data CDN, with no plugins. Player names from the network are rendered as text (Blazor escapes them).

`scripts/Smoke-Meter.ps1` opens a WebView2 remote-debugging port on a free localhost port for the duration of a test run only, and drives only pages served from the meter's own origin; the meter itself never enables it. Do not run the script against a data directory holding real data.

Raw packet logging, which upstream offers as a developer setting and which writes other players' data to disk, is not reachable here: the settings bridge reports it off and no packet writer is registered. The history database still stores other players' names and combat statistics for completed boss fights, as upstream does.

## Network

Capture is passive: Npcap reads the game's traffic and the meter sends nothing to the game. The meter's one request of its own is the upstream release check (ARCHITECTURE.md, "Updates"): an unauthenticated HTTPS GET to `api.github.com` for RATmeter's latest release, at most once every six hours while the meter runs (ten minutes after an attempt that got no answer, when GitHub saw no request), carrying a fixed User-Agent and the two headers GitHub's API asks for. It sends no token, no cookies and nothing about the player, the game or combat; GitHub sees the address it comes from and when. Redirects are not followed. The answer is untrusted: only the tag, the release page and the date are read, the tag must be a release tag, the page must be an https page on upstream's repository, at most 1 MiB is read, and the answer's text is never logged. A build that does not know its pinned release sends nothing. A switch in Settings, About turns it off, and then no request is made. The switch lives in `settings.json`: if that file is lost or reset to defaults (an unreadable file is kept aside and defaults are used), or an older build that does not know the `updates` section saves over it, checking is on again. The meter opens a page in the browser only when the player clicks "Release notes", and only an https page under upstream's releases on github.com.

`scripts/Install-Meter.ps1` uses the network only as a build does (NuGet restore); CI's tag step lists and fetches upstream's tags from GitHub. The installer renames and deletes only folders carrying its own marker (or empty ones), never one holding a link, and refuses install paths that overlap the data directory or the checkout after Windows resolves links and short names (ARCHITECTURE.md, "Updates").

## Dependencies

Restore runs with NuGet audit advisories as errors (`-warnaserror:NU1901..NU1904`). Upstream's `AionDpsMeter.Services` at the pinned release pulls the vulnerable native SQLite package (CVE-2025-6965, GHSA-2m69-gcr7-jv3q); Scarlet's root `Directory.Build.props` overlays it with SQLitePCLRaw.bundle_e_sqlite3 3.0.5, mirroring the override the harrynguon/AIon2-Dps-Meter fork carries in its copy of that project (ARCHITECTURE.md, "Upstream overlay"). Upstream itself does not carry it. Audit results are time-dependent, not a security review.

## Provenance

Upstream: https://github.com/Kuroukihime/AIon2-Dps-Meter at the release tag recorded by the `upstream` submodule (v1.11.5.341, commit aa3ffc6, since 2026-10-24), GPLv3, "RATmeter, Copyright (c) 2026 Kuroukihime" (`upstream/LICENSE`). Scarlet Meter is GPLv3 as well, because the built program links upstream's code; private use carries no obligation, and any distribution must ship the source under the same licence. The root `LICENSE` carries Scarlet Meter's own notice and names upstream's.

`src/ScarletMeter.App/DeferredWindowClose.cs` and `tests/ScarletMeter.Tests/DeferredWindowCloseTests.cs` are ported from the harrynguon/AIon2-Dps-Meter fork (GPLv3), where they are fork-owned files.

World events (`src/ScarletMeter.WorldEvents`, `tests/ScarletMeter.Tests/WorldEvents/`):

- The schedule engine (rules, validation, override merging, recurrence) and its test cases are ported from the harrynguon/AIon2-Dps-Meter fork's World Companion (GPLv3; `AionDpsMeter.Companion/wwwroot/schedule.mjs`, `presets.mjs` and `state.mjs`, `tests/companion.test.mjs` and `tests/companion-state.test.mjs`, at 4feb72baf86c8f5e4eaf8e03684401f1249de910), where they were written for the fork. The alert planner is new here.
- The Global preset's times and window lengths follow taengu/A2Tools-Plugins event-timers data at b11f345c93494a2b6a77142002dc26a656b610d2 (https://github.com/taengu/A2Tools-Plugins), as the fork's docs/TIMER-COMPARISON.md records: schedule parameters only, no code. That repository's licence terms have not been reviewed.
- The legacy defaults are adapted from Byanski/aion2timeroverlay at 2a5727ecdf5487e9becb6b3a756819f9d14ff564 (https://github.com/Byanski/aion2timeroverlay), whose package.json declares MIT; the snapshot contains no standalone MIT text. The fork's SECURITY.md ("Provenance") has the full record, including the cube coordinates and maps the fork once bundled from there, none of which are here.
- The schedules are **not verified against Global Asia / Nezekan** (ARCHITECTURE.md, "World events"). They are scheduled times, not live reports from the game.

Confirm the schedule data's redistribution terms before any public binary release.

A portable build from `Publish-Portable.ps1` (the live copy and its `-Zip` zip) carries no debug symbols or documentation files, so it does not name the builder's folders or Windows account. The zip leaves out the live copy's `data` folder (settings, history, logs); hand on the zip, never the `portable\ScarletMeter` folder itself, which holds that data. It has not been reviewed for public release (licence notices, schedule data terms; see Provenance): hand it only to people you would give the source to.

The live copy's data sits inside the checkout, in `portable\ScarletMeter\data`, which `.gitignore` excludes (`portable/`); `Publish-Portable.ps1` refuses any other output root inside the checkout. Never `git add -f` anything under `portable\`, and never run `git clean -x` or `-X`, which deletes ignored files and so the player's history and settings.

## Game terms

The fork's assessment at https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md ("Game terms") applies unchanged: NC's EULA forbids reverse engineering and access outside its published interfaces, and the AION 2 Global Operation Policy sanctions creating or distributing unauthorised programs with an integrated account ban. A passive meter arguably neither circumvents protection nor interferes with operations, but building and running it is the player's risk. Re-read both documents before any public release or any new packet decoding. This repository is private; keeping it private does not change the risk of running the meter.

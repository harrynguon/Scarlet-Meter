# Security, provenance and game terms

Never embed, print, log or commit credentials, tokens, game authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures. Treat anything read from the network or from files as untrusted; upstream validates packet fields before use, and the engine validates paths at its boundary.

Filesystem operations use absolute paths derived from trusted roots (AGENTS.md, "Paths"). The meter writes only under its data directory (per-user by default, or `--data-dir`): history database, settings, logs, the imported background picture and the WebView2 browser profile (ARCHITECTURE.md, "Storage").

`settings.json` is untrusted input: it passes one validation boundary (`SettingsNormalizer`), is size-limited, and can only name a background picture the meter itself imported, so it cannot point the page at another file. A damaged file is kept aside, never deleted; when it (or the backup) cannot be moved aside, or the file comes from a newer build, saving is off for that run so nothing overwrites it (ARCHITECTURE.md, "Settings"). The pages reach local images only through two WebView2 virtual hosts that each map a single folder (the backgrounds folder and the class icons beside the executable), never the database, logs or settings. A Content-Security-Policy allows scripts only from the app and images only from the app, those hosts and NC's game-data CDN, with no plugins. Player names from the network are rendered as text (Blazor escapes them).

`scripts/Smoke-Meter.ps1` opens a WebView2 remote-debugging port on a free localhost port for the duration of a test run only, and drives only pages served from the meter's own origin; the meter itself never enables it. Do not run the script against a data directory holding real data.

Raw packet logging, which upstream offers as a developer setting and which writes other players' data to disk, is not reachable here: the settings bridge reports it off and no packet writer is registered. The history database still stores other players' names and combat statistics for completed boss fights, as upstream does.

## Dependencies

Restore runs with NuGet audit advisories as errors (`-warnaserror:NU1901..NU1904`). Upstream's `AionDpsMeter.Services` at the pinned release pulls the vulnerable native SQLite package (CVE-2025-6965, GHSA-2m69-gcr7-jv3q); Scarlet's root `Directory.Build.props` overlays it with SQLitePCLRaw.bundle_e_sqlite3 3.0.5, mirroring the override the harrynguon/AIon2-Dps-Meter fork carries in its copy of that project (ARCHITECTURE.md, "Upstream overlay"). Upstream itself does not carry it. Audit results are time-dependent, not a security review.

## Provenance

Upstream: https://github.com/Kuroukihime/AIon2-Dps-Meter at the release tag recorded by the `upstream` submodule (v1.11.4.334, commit 57974f8, at this baseline), GPLv3, "RATmeter, Copyright (c) 2026 Kuroukihime" (`upstream/LICENSE`). Scarlet Meter is GPLv3 as well, because the built program links upstream's code; private use carries no obligation, and any distribution must ship the source under the same licence. The root `LICENSE` carries Scarlet Meter's own notice and names upstream's.

`src/ScarletMeter.App/DeferredWindowClose.cs` and `tests/ScarletMeter.Tests/DeferredWindowCloseTests.cs` are ported from the harrynguon/AIon2-Dps-Meter fork (GPLv3), where they are fork-owned files.

## Game terms

The fork's assessment at https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md ("Game terms") applies unchanged: NC's EULA forbids reverse engineering and access outside its published interfaces, and the AION 2 Global Operation Policy sanctions creating or distributing unauthorised programs with an integrated account ban. A passive meter arguably neither circumvents protection nor interferes with operations, but building and running it is the player's risk. Re-read both documents before any public release or any new packet decoding. This repository is private; keeping it private does not change the risk of running the meter.

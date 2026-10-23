# Security, provenance and game terms

Never embed, print, log or commit credentials, tokens, game authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures. Treat anything read from the network or from files as untrusted; upstream validates packet fields before use, and the engine validates paths at its boundary.

Filesystem operations use absolute paths derived from trusted roots (AGENTS.md, "Paths"). The meter writes only under its per-user data directory.

Raw packet logging, which upstream offers as a developer setting and which writes other players' data to disk, is not reachable here: the settings bridge reports it off and no packet writer is registered. The history database still stores other players' names and combat statistics for completed boss fights, as upstream does.

## Dependencies

Restore runs with NuGet audit advisories as errors (`-warnaserror:NU1901..NU1904`). Upstream's `AionDpsMeter.Services` overrides the SQLite native bundle (SQLitePCLRaw.bundle_e_sqlite3 3.0.5) for CVE-2025-6965; that override is upstream's own at the pinned release. Audit results are time-dependent, not a security review.

## Provenance

Upstream: https://github.com/Kuroukihime/AIon2-Dps-Meter at the release tag recorded by the `upstream` submodule (v1.11.4.334, commit 57974f8, at this baseline), GPLv3. Scarlet Meter is GPLv3 as well, because the built program links upstream's code; private use carries no obligation, and any distribution must ship the source under the same licence.

`src/ScarletMeter.App/DeferredWindowClose.cs` is ported from the harrynguon/AIon2-Dps-Meter fork (GPLv3), where it is a fork-owned file.

## Game terms

The fork's assessment at https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md ("Game terms") applies unchanged: NC's EULA forbids reverse engineering and access outside its published interfaces, and the AION 2 Global Operation Policy sanctions creating or distributing unauthorised programs with an integrated account ban. A passive meter arguably neither circumvents protection nor interferes with operations, but building and running it is the player's risk. Re-read both documents before any public release or any new packet decoding. This repository is private; keeping it private does not change the risk of running the meter.

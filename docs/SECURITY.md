# Security, provenance and game terms

Never embed, print, log or commit credentials, tokens, game authentication data, personal progress, packet captures or private combat logs. Use synthetic fixtures. Treat anything read from the network or from files as untrusted; upstream validates packet fields before use, and the engine validates paths at its boundary.

Filesystem operations use absolute paths derived from trusted roots (AGENTS.md, "Paths"). The meter writes only under its data directory (per-user by default, or `--data-dir`): history database, logs and the WebView2 browser profile (ARCHITECTURE.md, "Storage").

Raw packet logging, which upstream offers as a developer setting and which writes other players' data to disk, is not reachable here: the settings bridge reports it off and no packet writer is registered. The history database still stores other players' names and combat statistics for completed boss fights, as upstream does.

## Network

Capture is passive: Npcap reads the game's traffic and the meter sends nothing to the game. The meter's one request of its own is the upstream release check (ARCHITECTURE.md, "Updates"): an unauthenticated HTTPS GET to `api.github.com` for RATmeter's latest release, at most once every six hours while the meter runs, carrying a fixed User-Agent and the two headers GitHub's API asks for. It sends no token, no cookies and nothing about the player, the game or combat; GitHub sees the address it comes from and when. Redirects are not followed. The answer is untrusted: only the tag, the release page and the date are read, the tag must be a release tag, the page must be an https page on upstream's repository, at most 1 MiB is read, and the answer's text is never logged. A build that does not know its pinned release sends nothing. The check is not wired into the UI yet; when it is, a settings switch turns it off and then no request is made.

`scripts/Install-Meter.ps1` uses the network only as a build does (NuGet restore); CI's tag step lists and fetches upstream's tags from GitHub.

## Dependencies

Restore runs with NuGet audit advisories as errors (`-warnaserror:NU1901..NU1904`). Upstream's `AionDpsMeter.Services` at the pinned release pulls the vulnerable native SQLite package (CVE-2025-6965, GHSA-2m69-gcr7-jv3q); Scarlet's root `Directory.Build.props` overlays it with SQLitePCLRaw.bundle_e_sqlite3 3.0.5, mirroring the override the harrynguon/AIon2-Dps-Meter fork carries in its copy of that project (ARCHITECTURE.md, "Upstream overlay"). Upstream itself does not carry it. Audit results are time-dependent, not a security review.

## Provenance

Upstream: https://github.com/Kuroukihime/AIon2-Dps-Meter at the release tag recorded by the `upstream` submodule (v1.11.4.334, commit 57974f8, at this baseline), GPLv3, "RATmeter, Copyright (c) 2026 Kuroukihime" (`upstream/LICENSE`). Scarlet Meter is GPLv3 as well, because the built program links upstream's code; private use carries no obligation, and any distribution must ship the source under the same licence. The root `LICENSE` carries Scarlet Meter's own notice and names upstream's.

`src/ScarletMeter.App/DeferredWindowClose.cs` and `tests/ScarletMeter.Tests/DeferredWindowCloseTests.cs` are ported from the harrynguon/AIon2-Dps-Meter fork (GPLv3), where they are fork-owned files.

## Game terms

The fork's assessment at https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md ("Game terms") applies unchanged: NC's EULA forbids reverse engineering and access outside its published interfaces, and the AION 2 Global Operation Policy sanctions creating or distributing unauthorised programs with an integrated account ban. A passive meter arguably neither circumvents protection nor interferes with operations, but building and running it is the player's risk. Re-read both documents before any public release or any new packet decoding. This repository is private; keeping it private does not change the risk of running the meter.

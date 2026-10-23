# Scarlet Meter

A network-based DPS meter for AION 2 Global with its own UI, built on the packet capture and decoding of [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter) (Kuroukihime/AIon2-Dps-Meter), which is included unmodified as the `upstream/` git submodule and pinned to a release tag.

Status: scaffold. Capture, decoding, fight tracking and history come from upstream through `ScarletMeter.Engine`, which also offers per-player details (skills, buffs, damage graph, combat log), boss-fight history and a demo mode; the window shows the active target, party DPS, ping and per-player damage. See [docs/HANDOFF.md](docs/HANDOFF.md) for what has been checked and what is next.

## Layout

| Path | Purpose |
| --- | --- |
| `upstream/` | RATmeter at a release tag (submodule). Never edited here. |
| `src/ScarletMeter.Engine` | The only project that references upstream. Wraps its pipeline behind Scarlet's own contracts (`ICombatEngine`, `CombatSnapshot`, `PlayerDetails`, the history records). |
| `src/ScarletMeter.App` | WPF window hosting a Blazor WebView; references the engine only. |
| `tests/ScarletMeter.Tests` | Boundary guards and engine mapping tests (no Npcap needed). |
| `scripts/` | `Build.ps1` (restore, build, test), `Update-Upstream.ps1 -Tag vX.Y.Z.N` and `Test-PublishLayout.ps1`. Each derives its paths from its own location. |
| `docs/` | Maintainer documentation. Start with [docs/README.md](docs/README.md). |

## Build and run

Requires Windows, the .NET 10 SDK, the WebView2 runtime and, for live capture, [Npcap](https://npcap.com) installed in WinPcap API-compatible mode.

`<checkout>` below is the absolute path of your clone.

```powershell
git clone --recurse-submodules https://github.com/harrynguon/ScarletMeter.git "<checkout>"
& "<checkout>\scripts\Build.ps1"
```

Run `src\ScarletMeter.App` from your IDE, or publish it as described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). `--no-capture` opens the window without Npcap; `--demo` opens it without Npcap on a synthetic party's boss fights (kept in their own history database), for UI work; `--data-dir <absolute path>` moves everything the meter writes (history database, logs, WebView2 profile) away from `%LocalAppData%\ScarletMeter`.

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.4.334
```

Releases only, never upstream's `dev` branch. The script moves the submodule, rebuilds and runs the tests; a compile error in `src/ScarletMeter.Engine/Upstream` is the signal that upstream changed an API the engine maps.

## Licence and terms

GPLv3, the same as upstream, because the published program links upstream's code. Decoding the game's traffic is against NC's terms as read in the fork's [SECURITY.md](https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md); use at your own risk, and see [docs/SECURITY.md](docs/SECURITY.md) before any release.

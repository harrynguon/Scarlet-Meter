# Scarlet Meter

A network-based DPS meter for AION 2 Global with its own UI, built on the packet capture and decoding of [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter) (Kuroukihime/AIon2-Dps-Meter), which is included unmodified as the `upstream/` git submodule and pinned to a release tag.

Status: in development, not yet tried in the game. Capture, decoding, fight tracking and history come from upstream through `ScarletMeter.Engine`. The overlay is a chromeless, translucent, always-on-top window showing the active target, party DPS, ping and per-player damage, with a settings window for background opacity and picture, class colours, relative bars, deaths, hidden names, row size, fight options (boss-only, combined summons, one-minute dummy, history retention) and a show/hide shortcut (`Ctrl+Shift+D` by default). Window positions are remembered. A slim strip at the bottom of the overlay announces world events (Spacetime Rift, Shugo Festival, Dimensional Invasion, bosses and more) five minutes ahead and while their entry is open; times follow the World Companion's Global preset and are not yet verified in the game. See [docs/HANDOFF.md](docs/HANDOFF.md) for what has been checked and what is next.

## Layout

| Path | Purpose |
| --- | --- |
| `upstream/` | RATmeter at a release tag (submodule). Never edited here. |
| `src/ScarletMeter.Engine` | The only project that references upstream. Wraps its pipeline behind Scarlet's own contracts (`ICombatEngine`, `CombatSnapshot`). |
| `src/ScarletMeter.App` | WPF windows hosting Blazor WebViews (overlay, settings) and the settings store; references the engine and the world-events module, never upstream. |
| `src/ScarletMeter.WorldEvents` | World event schedules (Global preset) and the alert planner for overlay notices; pure .NET, no dependencies; drives the overlay's notices. Times are not verified in the game. |
| `tests/ScarletMeter.Tests` | Boundary guards, engine mapping, settings, close-ordering and world-event schedule tests (no Npcap needed). |
| `scripts/` | `Build.ps1` (restore, build, test), `Update-Upstream.ps1 -Tag vX.Y.Z.N`, `Test-PublishLayout.ps1` and `Smoke-Meter.ps1` (drives the UI for checks). Each derives its paths from its own location. |
| `docs/` | Maintainer documentation. Start with [docs/README.md](docs/README.md). |

## Build and run

Requires Windows, the .NET 10 SDK, the WebView2 runtime and, for live capture, [Npcap](https://npcap.com) installed in WinPcap API-compatible mode.

`<checkout>` below is the absolute path of your clone.

```powershell
git clone --recurse-submodules https://github.com/harrynguon/ScarletMeter.git "<checkout>"
& "<checkout>\scripts\Build.ps1"
```

Run `src\ScarletMeter.App` from your IDE, or publish it as described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). `--no-capture` opens the window without Npcap; `--data-dir <absolute path>` moves everything the meter writes (history database, logs, WebView2 profile) away from `%LocalAppData%\ScarletMeter`.

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.4.334
```

Releases only, never upstream's `dev` branch. The script moves the submodule, rebuilds and runs the tests; a compile error in `src/ScarletMeter.Engine/Upstream` is the signal that upstream changed an API the engine maps.

## Licence and terms

GPLv3, the same as upstream, because the published program links upstream's code. Decoding the game's traffic is against NC's terms as read in the fork's [SECURITY.md](https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md); use at your own risk, and see [docs/SECURITY.md](docs/SECURITY.md) before any release.

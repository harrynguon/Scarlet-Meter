# Scarlet Meter

A network-based DPS meter for AION 2 Global with its own UI, built on the packet capture and decoding of [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter) (Kuroukihime/AIon2-Dps-Meter), which is included unmodified as the `upstream/` git submodule and pinned to a release tag.

Status: in development, not yet tried in the game. Capture, decoding, fight tracking and history come from upstream through `ScarletMeter.Engine`. The overlay is a chromeless, translucent, always-on-top window showing the active target, party DPS, ping and per-player damage, with a settings window for background opacity and picture, class colours, relative bars, deaths, hidden names, row size, fight options (boss-only, combined summons, one-minute dummy, history retention) and a show/hide shortcut (`Ctrl+Shift+D` by default). Window positions are remembered. Clicking a player opens their details (skills, buffs, damage graph, combat log), and a history window browses saved boss fights; `--demo` runs synthetic fights for trying the UI without the game. A slim strip at the bottom of the overlay announces world events (Spacetime Rift, Shugo Festival, Dimensional Invasion, bosses and more) five minutes ahead and while their entry is open; times follow the World Companion's Global preset and are not yet verified in the game. A quiet line says when RATmeter publishes a release newer than the one the meter is built on, and `scripts/Install-Meter.ps1` rebuilds and reinstalls it. See [docs/HANDOFF.md](docs/HANDOFF.md) for what has been checked and what is next.

## Layout

| Path | Purpose |
| --- | --- |
| `upstream/` | RATmeter at a release tag (submodule). Never edited here. |
| `src/ScarletMeter.Engine` | The only project that references upstream. Wraps its pipeline behind Scarlet's own contracts (`ICombatEngine`, `CombatSnapshot`, `PlayerDetails`, the history records). |
| `src/ScarletMeter.App` | WPF windows hosting Blazor WebViews (overlay, settings, player details, history), the settings store and the upstream release check (`Updates/`); references the engine and the world-events module, never upstream. |
| `src/ScarletMeter.WorldEvents` | World event schedules (Global preset) and the alert planner for overlay notices; pure .NET, no dependencies; drives the overlay's notices. Times are not verified in the game. |
| `tests/ScarletMeter.Tests` | Boundary guards, engine mapping, settings, close-ordering, world-event schedule, build-stamp and release-check tests (no Npcap needed). |
| `scripts/` | `Build.ps1` (restore, build, test), `Publish-Portable.ps1` (build and update the one portable copy, `portable\ScarletMeter`, in place) with its folder rules `PortableFolders.ps1` and `Test-PortableFolders.ps1`, `Update-Meter.ps1` (move to RATmeter's latest release and update the portable copy, in one command), `Install-Meter.ps1` (build and install for the current user) with its folder rules `InstallFolders.ps1` and `Test-InstallMeter.ps1` (its refusal, delete and swap checks), `Update-Upstream.ps1 -Tag vX.Y.Z.N`, `Test-PublishLayout.ps1` and `Smoke-Meter.ps1` (drives the UI for checks). Each derives its paths from its own location. |
| `docs/` | Maintainer documentation. Start with [docs/README.md](docs/README.md). |

## Requirements

To **run** the installed meter:

- Windows 11, 64-bit. Windows 10 (version 1607 or later, 64-bit) should work but has not been tried.
- [Npcap](https://npcap.com), for live capture. In its installer, tick "Install Npcap in WinPcap API-compatible Mode": the capture library loads Npcap's `wpcap.dll`, which only that mode installs. Leave "Restrict Npcap driver's access to Administrators only" unticked, or the meter must be started as administrator to capture. Without Npcap, a normal start shows a message saying so and closes; `--demo` and `--no-capture` run without it.
- The Microsoft Edge WebView2 Runtime, which draws the meter's windows. Windows 11 ships with it; on Windows 10, install the Evergreen Runtime from Microsoft if the meter's windows stay blank.
- No .NET install: the portable and installed builds are self-contained, so the .NET runtime is bundled inside `ScarletMeter.exe`.

To **build** it (once, and again for each update):

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `Build.ps1` and `Install-Meter.ps1` use the `dotnet` on PATH and stop with a message if it is not a .NET 10 SDK; when the SDK is installed elsewhere, set `SCARLET_DOTNET` to its `dotnet.exe` (an absolute path) for the session.
- Git, to clone the repository with its `upstream/` submodule.

## Run it

**Portable, like RATmeter** (no installer): build it once,

```powershell
& "<checkout>\scripts\Publish-Portable.ps1"
```

and run **`<checkout>\portable\ScarletMeter\ScarletMeter.exe`**, already unzipped. That is the only copy and the only exe to click: every rebuild or update replaces the program files in that folder and keeps its `data` folder, where the meter keeps its settings, history and logs, and anything else you put there, such as a backup of `data` (Windows also unpacks a few native libraries to `%TEMP%\.net\ScarletMeter`, as for any single-file app). Earlier builds are not kept; `-Zip` also writes `<checkout>\portable\ScarletMeter-win-x64.zip`, the latest build without your files, for handing on. Close the meter before a rebuild: the script refuses while it runs. Never run `git clean -x` in the checkout, which would delete `portable\` and your data with it. **Updating** (when the meter says a newer RATmeter release is out, usually after a game patch) is one command, with the meter closed:

```powershell
& "<checkout>\scripts\Update-Meter.ps1"
```

It finds RATmeter's latest release, moves the pin to it and builds and tests in a temporary copy, so your checkout's files are untouched (it leaves a local branch holding the pin move), then updates `portable\ScarletMeter` in place, keeping its data. If the new release breaks something Scarlet uses, it stops with a message and the copy stays as it was.

**Installed** instead (below, "Install and update"): the meter is `%LocalAppData%\Programs\ScarletMeter\ScarletMeter.exe`, with "Scarlet Meter" in the Start menu if installed with `-Shortcut`, and its data in `%LocalAppData%\ScarletMeter`.

To look around without the game first, start it with `--demo`, for example:

```powershell
& "<checkout>\portable\ScarletMeter\ScarletMeter.exe" --demo
```

`--demo` plays synthetic boss fights (kept in their own history file), `--no-capture` opens the meter with capture off, and `--data-dir <absolute path>` keeps everything it writes somewhere other than `%LocalAppData%\ScarletMeter`. For the real thing, start the game, then the meter; it finds the game's connection on its own ("Waiting for the game's connection..." until it does).

## Build from source

`<checkout>` below is the absolute path of your clone.

```powershell
git clone --recurse-submodules https://github.com/harrynguon/Scarlet-Meter.git "<checkout>"
& "<checkout>\scripts\Build.ps1"
```

Run `src\ScarletMeter.App` from your IDE, or publish it as described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). `--no-capture` opens the window without Npcap; `--demo` opens it without Npcap on a synthetic party's boss fights (kept in their own history database), for UI work; `--data-dir <absolute path>` moves everything the meter writes (history database, logs, WebView2 profile) away from `%LocalAppData%\ScarletMeter`.

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.5.341
```

Releases only, never upstream's `dev` branch. The script moves the submodule, rebuilds and runs the tests; a compile error in `src/ScarletMeter.Engine/Upstream` is the signal that upstream changed an API the engine maps.

## Install and update

```powershell
& "<checkout>\scripts\Install-Meter.ps1" -Shortcut
```

Builds and tests a Release, publishes it and installs it to `%LocalAppData%\Programs\ScarletMeter`, keeping the previous install in `ScarletMeter.previous` for a rollback by hand; close the meter first. It only replaces folders it made itself, and your data in `%LocalAppData%\ScarletMeter` is never touched. There is no self-updater, because the meter is built from source: when upstream publishes a release (the meter will say so, from one anonymous request to GitHub at most every six hours), run `Update-Upstream.ps1` with the new tag, then `Install-Meter.ps1` again. Details: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md), "Install and update".

## Licence and terms

GPLv3, the same as upstream, because the published program links upstream's code. Decoding the game's traffic is against NC's terms as read in the fork's [SECURITY.md](https://github.com/harrynguon/AIon2-Dps-Meter/blob/master/docs/SECURITY.md); use at your own risk, and see [docs/SECURITY.md](docs/SECURITY.md) before any release.

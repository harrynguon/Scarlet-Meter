# Architecture

Scarlet Meter is a .NET 10 WPF application hosting a Blazor WebView. Capture and decoding of the game's traffic are not written here: they come from RATmeter (Kuroukihime/AIon2-Dps-Meter), included unmodified as the `upstream/` git submodule, pinned to release tag **v1.11.4.334** (commit 57974f8).

## Data flow

```
network adapters --SharpPcap/Npcap--> upstream CaptureDevice (finds the game's port by its heartbeat)
  -> upstream TcpStreamBuffer (TCP reassembly) -> upstream PacketProcessor (framing, LZ4)
  -> upstream PacketDispatchService -> opcode processors -> upstream EntityTracker + CombatSessionManager
  -> ScarletMeter.Engine UpstreamCombatEngine.GetSnapshot() -> CombatSnapshot (Scarlet types)
  -> ScarletMeter.App MeterPage (polls ten times a second) -> BlazorWebView in MainWindow
```

| Source | Responsibility |
| --- | --- |
| `upstream/AionDpsMeter.Core` | Game data (mobs, skills, classes as JSON) and entity models. |
| `upstream/AionDpsMeter.Services` | Capture, reassembly, decoding, fight tracking, SQLite history, upstream settings. |
| `src/ScarletMeter.Engine/Contracts` | Scarlet's contracts: `ICombatEngine`, `CombatSnapshot`, `EngineOptions`, `EngineSettings`. |
| `src/ScarletMeter.Engine/Upstream` | The only code that maps upstream types: `UpstreamCombatEngine`, `UpstreamSettingsBridge`, `NoCaptureDevice`. |
| `src/ScarletMeter.Engine/ScarletEngineServiceCollectionExtensions.cs` | `AddScarletEngine`: the upstream service graph, as upstream's own App.xaml.cs builds it, minus UI, updater and packet logger. |
| `src/ScarletMeter.App/App.xaml.cs` | Composition root: host, Serilog file log, engine start, window; startup failures end in a dialog. |
| `src/ScarletMeter.App/MainWindow.xaml(.cs)`, `DeferredWindowClose.cs` | The window and the close ordering that keeps Blazor from crashing the process. |
| `src/ScarletMeter.App/Pages/MeterPage.razor(.cs)`, `wwwroot/` | The meter UI. |
| `src/ScarletMeter.App/Updates` | The build stamp (`BuildStamp.targets`, read by `BuildInfo`) and `UpstreamReleaseChecker`, which tells whether upstream has a newer release (see Updates). |

## Upstream boundary

The boundary keeps upstream updates a one-line pin change and keeps Scarlet's own code independent of upstream's internals.

- **Never edit `upstream/`.** It is a submodule; a change there would be lost on the next pin and would fork upstream silently. A fix upstream needs goes to upstream, or to the harrynguon/AIon2-Dps-Meter fork, which carries minimal generic fixes.
- **Releases only.** The pin moves with `scripts/Update-Upstream.ps1 -Tag vX.Y.Z.N`, which refuses anything that is not a release tag. Upstream's `dev` branch is never used.
- **One project references upstream.** `ScarletMeter.Engine` references `AionDpsMeter.Core` and `AionDpsMeter.Services` through `$(ScarletUpstreamRoot)` (src/Directory.Build.props). `ScarletMeter.App` references the engine only.
- **No upstream type in the engine's public surface.** The app and future modules program against Scarlet's records and interfaces.
- **Build settings do not leak into upstream.** Scarlet's settings live in `src/Directory.Build.props` (imported by `tests/`), never at the repository root. MSBuild imports only the nearest `Directory.Build.props` above a project, so the root file reaches only the upstream projects, which ship none: that file is the upstream overlay (below), nothing else. The build stamp (Updates) is an MSBuild target that `ScarletMeter.App.csproj` alone imports, so it is not an overlay; `BuildInfoTests.OnlyTheAppIsStamped` checks that upstream's assemblies and the engine carry no stamp.

`tests/ScarletMeter.Tests/UpstreamBoundaryTests.cs` enforces the reference rules from the compiled assemblies, the build's dependency graph (every Scarlet project the tests reference, directly or not, except the engine) and reflection over the engine's exported types (base types, interfaces, generic constraints and member signatures). A new Scarlet project is guarded once the test project references it.

### Upstream overlay

The root `Directory.Build.props` is the one place that adjusts how upstream builds here without editing a file under `upstream/`. It holds minimal, generic build fixes only, never behaviour, and each one is listed here with its guard.

| Upstream project | Overlay | Why | Guard |
| --- | --- | --- | --- |
| `AionDpsMeter.Services` | `PackageReference` SQLitePCLRaw.bundle_e_sqlite3 3.0.5 | EF Core's legacy bundle pins the vulnerable native package SQLitePCLRaw.lib.e_sqlite3 2.1.11 (GHSA-2m69-gcr7-jv3q), which the audit-as-error restore rejects. The fork carries the same override in its copy of the project. | `SqliteRuntimeTests` checks the loaded SQLite is 3.50.2 or later |

When upstream adopts a fix itself, remove the overlay entry in the same change as the pin update.

### What breaks on an upstream update, and where

| Upstream change | Effect | Where to fix |
| --- | --- | --- |
| New member on `IAppSettingsService` | Compile error | `Upstream/UpstreamSettingsBridge.cs` |
| Renamed or re-shaped `CombatSessionManager`, `PlayerStats`, `Mob`, `IPacketService`, `IPacketCaptureDevice` | Compile error | `Upstream/UpstreamCombatEngine.cs` |
| New required service in upstream's graph | `EngineRegistrationTests` fail when the engine resolves | `ScarletEngineServiceCollectionExtensions.cs` |
| New packet layout for a game patch | None here; upstream's processors change | Take the next upstream release |
| Changed game-data file names or location | `EngineRegistrationTests` fail loading data | `AddScarletEngine` or the app's output layout |

## Engine contract

`ICombatEngine` is the whole surface: `Start`, `Stop`, `Reset`, `Capture` (running, adapter name once locked), `GetSnapshot()` and `PingUpdated`. `CombatSnapshot` is immutable: the active target (name, HP, boss flag), duration, party DPS and one `PlayerRow` per player (identity, class, totals, DPS, damage share, critical rate, deaths). Players the game has not named yet are included with `IsIdentified` false; the UI hides them until they hold over one percent of the damage, as upstream's UI does.

`Start` throws `CaptureStartException` in the two cases upstream fails on: Npcap's `wpcap.dll` cannot be loaded (the message says how to install Npcap), or upstream found no eligible adapter: Npcap listed none, or only kinds upstream skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which, so the message names both and suggests running as administrator when Npcap is restricted to administrators. An adapter that is found but fails to open is only logged by upstream, so `Start` can return with nothing listening; the header then stays at "Waiting for the game's connection...".

`Reset` maps to `CombatSessionManager.Reset` only, which runs under the session manager's lock and drops every target's session, the active target and buffered buffs. Upstream's `IPacketService.Reset` is deliberately not called: it also clears the summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture thread writes them, and upstream's own UI never calls it.

Threading: upstream's opcode processors write `EntityTracker` and `CombatSessionManager` on the capture thread. `GetSnapshot` reads the session manager under its lock, but player identities come from `EntityTracker` dictionaries that the capture thread writes outside that lock; upstream's own UI reads them the same way. A racing read can return a stale value or throw; `MeterPage` logs the first failure of a run and retries on the next tick.

`EngineOptions` is host policy: an absolute `DataDirectory` (default `%LocalAppData%\ScarletMeter`, a relative path throws) holding everything the meter writes (see Storage), and `CaptureMode` (`Live` or `None`). `EngineSettings` are the runtime toggles upstream consults on every hit (boss-only capture, grouped summon damage, one-minute dummy mode, history retention); the bridge forwards their `Changed` event to upstream's `SettingsChanged`.

Deliberately unsupported through the bridge: upstream's raw packet logging (`IsPacketLoggingEnabled` is always false and no `FilePacketWriter` is registered) and its buff and skill-cooldown overlays (disabled, so the timed trackers track nothing). Upstream's update checker is not registered.

## Window lifecycle

WPF disposes a window's WebView2 control when the window closes, but BlazorWebView is never disposed automatically, and a Blazor message sent afterwards terminates the process. `MainWindow` therefore cancels its first close, awaits `BlazorWebView.DisposeAsync` on a later dispatcher turn (components first, so `MeterPage.DisposeAsync` stops the refresh loop and unsubscribes from ping events), then closes again. `DeferredWindowClose` is ported from the fork, where the crash was reproduced and the fix is covered by unit tests and a native lifecycle check. The unit tests are ported here (`DeferredWindowCloseTests`); the native check is not yet (see HANDOFF.md).

BlazorWebView 10's `DisposeAsync` disposes the components and then the WebView2 composition control on whichever thread the component disposal completed on; the control refuses cross-thread COM access with an `InvalidCastException` (E_NOINTERFACE). `MainWindow.ReleaseBlazorContentAsync` catches that and disposes the control again on the UI thread, which succeeds; both steps are logged at Information. The fork's companion window logs the same failure, so the fallback is a candidate fix to offer it.

Shutdown order: window closes, `App.OnExit` disposes the engine (stops capture, completes open fights, flushes history), then the host.

Startup failures: any exception while creating the data directory, building the host, constructing upstream's graph (game data, history database) or showing the window ends in a "could not start" dialog and exit code 1; a failed capture start gets its own dialog. Once the host exists the failure is also logged at Critical (Error for capture). Before that (bad arguments, a data directory that cannot be created) there is no log file, and the dialog is the only report. Unhandled exceptions on the UI thread or elsewhere are logged at Critical, not handled, so the process still terminates.

## Storage

Everything the meter writes lives under `EngineOptions.DataDirectory`:

- `combat-history.db`: upstream's EF Core SQLite store of completed boss fights, retention per `EngineSettings.HistoryRetentionDays`.
- `logs/scarlet-<date>.log`: Serilog, 14 days.
- `webview2/`: the WebView2 browser profile (cache, local storage, GPU cache; no meter data). `MainWindow` sets it through `BlazorWebViewInitializing`; otherwise BlazorWebView would use `%LocalAppData%\ScarletMeter.WebView2` and `--data-dir` would not isolate a run.

Nothing is written next to the executable or in the working directory. Upstream's `appsettings.user.json` is not used; Scarlet's own settings persistence is still to be designed.

## Updates

There is no self-updater. RATmeter's updater downloads a published release and replaces its own executable; Scarlet Meter is a private build made from this repository, with no published binaries to download, and swapping in a binary from elsewhere would skip the build, the tests and the audit-as-errors restore. What changes for the player is upstream: a game patch breaks decoding until upstream releases parser fixes, and the meter gets them when the submodule pin moves. Updating is therefore: notice the release, move the pin (`scripts/Update-Upstream.ps1`), rebuild and reinstall (`scripts/Install-Meter.ps1`). Commands are in DEVELOPMENT.md ("Install and update").

- **Build stamp.** `src/ScarletMeter.App/Updates/BuildStamp.targets` runs before the SDK generates the assembly attributes and stamps two `AssemblyMetadata` entries into the app: `ScarletUpstreamRelease`, the tag naming the submodule's commit (`git describe --tags --exact-match` in `$(ScarletUpstreamRoot)`), else the submodule's short commit, else `unknown`; and `ScarletCommit`, Scarlet's short commit, else `unknown`. A missing git, repository, submodule or tag changes the value and never fails the build. The target runs on every build; the SDK hashes the attributes, so only a changed value recompiles. `BuildInfo.Current` reads the stamp. A clone made with `--recurse-submodules` has upstream's tags; CI's shallow submodule clone lacks the pinned one, so CI fetches it first (DEVELOPMENT.md, "CI").
- **Release check.** `UpstreamReleaseChecker` (behind `IUpstreamReleaseChecker`, registered by `AddUpstreamReleaseChecker`) asks `https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest` for upstream's latest release and compares it with the stamp. It reads only `tag_name`, `html_url` and `published_at`. The tag must be a release tag, `v` and four numeric parts (ASCII digits, nothing after), compared as numbers, so v1.10.0.1 is newer than v1.9.4.277. The page must be an https page under `https://github.com/Kuroukihime/AIon2-Dps-Meter/`, with no query, fragment or percent-encoding, so the UI can open it. The result is `UpToDate`, `NewerAvailable` (tag, page, publication date) or `Unknown` (an `UpstreamReleaseCheckFailure` and a one-sentence reason: pinned release unknown, timeout, network, rate limit, 404, other HTTP status, invalid answer, caller cancelled, unexpected). It never throws to callers; failures are logged as warnings with the reason only, never GitHub's text. The last result is cached in memory, not on disk.
- **Network footprint.** At most one unauthenticated HTTPS GET to api.github.com every six hours per process, timed from the start of the previous request, whatever its outcome; concurrent callers share one request. The request carries a fixed User-Agent, `Accept: application/vnd.github+json` and `X-GitHub-Api-Version`: no token, no cookies, nothing about the player, the game or combat. Redirects are not followed, the timeout is ten seconds and at most 1 MiB of answer is read. A build stamped with a commit or `unknown` sends nothing, and nothing is sent until the UI calls `CheckAsync`. The checker is not registered in `App.xaml.cs` yet: the UI rewrite wires it into the overlay (a quiet notice when a newer release exists) and into settings (a switch that turns the check off, so no request is made at all).
- **Install.** `scripts/Install-Meter.ps1` builds Release with the tests, publishes self-contained single-file win-x64 into `<install>.staging` beside the install (default `%LocalAppData%\Programs\ScarletMeter`), checks it with `Test-PublishLayout.ps1` and swaps folders by renames that are undone if one fails, keeping the old install as `<install>.previous` for a manual rollback. It refuses while the meter runs from the install, and never touches the data directory.

## Tests

- `UpstreamBoundaryTests`: the app references no upstream assembly; no Scarlet project but the engine declares an upstream project reference (and the check confirms it sees the engine's); the engine's public surface (public and protected members) exposes no upstream type, with a self-check that the surface scan sees an upstream interface.
- `EngineRegistrationTests`: the real upstream graph resolves without Npcap, for `CaptureMode.None` and the production `Live` graph (scope validation on; every opcode processor is constructed through the registry; `ValidateOnBuild` stays off because upstream also registers nested and compiler-generated types from its processor namespace that nothing resolves); a snapshot maps synthetic hits fed to upstream's session manager (identity, totals, share, critical rate, duration, party DPS); reset empties it and keeps summon ownership; the two capture-start failures get their own messages; packet logging cannot be enabled; settings and their change event reach upstream.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).
- `BuildInfoTests`: the app carries a stamp, a release tag or a short commit (in CI, exactly the pinned tag CI fetched, named by `SCARLET_EXPECTED_UPSTREAM_RELEASE`); an assembly without one reads as `unknown`; upstream's assemblies and the engine carry none; which strings are release tags (no `\d` look-alikes, no trailing newline, each part fits an int).
- `UpstreamReleaseCheckerTests`: against a fake handler and `FakeTimeProvider`, never the network: a newer release with its page and date, same or older releases up to date, numeric comparison; the request is one GET with only the three headers; the six-hour cache (failures included) and one shared request for concurrent callers; the ten-second timeout for headers and for the body; 403 and 429 rate limits, 404, redirects and other statuses; malformed JSON, invalid tags and pages off upstream's repository (other host, http, user info, port, `..`, query); oversized answers with and without a length; network and unexpected exceptions; no request for an unknown pinned release; caller cancellation keeps the shared request; GitHub's text never reaches the log; the registration and the production handler (no redirects, no cookies).

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by `Install-Meter.ps1` after its publish, in CI too) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

None of these validate live capture or decoding. That needs the game (HANDOFF.md, in-game acceptance).

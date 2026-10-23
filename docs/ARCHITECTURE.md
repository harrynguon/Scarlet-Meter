# Architecture

Scarlet Meter is a .NET 10 WPF application hosting a Blazor WebView. Capture and decoding of the game's traffic are not written here: they come from RATmeter (Kuroukihime/AIon2-Dps-Meter), included unmodified as the `upstream/` git submodule, pinned to release tag **v1.11.4.334** (commit 57974f8).

## Data flow

```
network adapters --SharpPcap/Npcap--> upstream CaptureDevice (finds the game's port by its heartbeat)
  -> upstream TcpStreamBuffer (TCP reassembly) -> upstream PacketProcessor (framing, LZ4)
  -> upstream PacketDispatchService -> opcode processors -> upstream EntityTracker + CombatSessionManager
  -> ScarletMeter.Engine UpstreamCombatEngine.GetSnapshot() -> CombatSnapshot (Scarlet types)
  -> ScarletMeter.App MeterPage (polls ten times a second) -> BlazorWebView in the overlay window
```

| Source | Responsibility |
| --- | --- |
| `upstream/AionDpsMeter.Core` | Game data (mobs, skills, classes as JSON) and entity models. |
| `upstream/AionDpsMeter.Services` | Capture, reassembly, decoding, fight tracking, SQLite history, upstream settings. |
| `src/ScarletMeter.Engine/Contracts` | Scarlet's contracts: `ICombatEngine`, `CombatSnapshot`, `EngineOptions`, `EngineSettings`. |
| `src/ScarletMeter.Engine/Upstream` | The only code that maps upstream types: `UpstreamCombatEngine`, `UpstreamSettingsBridge`, `NoCaptureDevice`. |
| `src/ScarletMeter.Engine/ScarletEngineServiceCollectionExtensions.cs` | `AddScarletEngine`: the upstream service graph, as upstream's own App.xaml.cs builds it, minus UI, updater and packet logger. |
| `src/ScarletMeter.App/App.xaml.cs` | Composition root: host, Serilog file log, engine start, window; startup failures end in a dialog. |
| `src/ScarletMeter.App/Windows/` | `ScarletWindow` (shared Blazor hosting and close ordering), `OverlayWindow`, `ToolWindow`, `MeterWindows` (opens windows, owns the hotkey), `GlobalHotkey`, `WindowPlacementTracker`, `WebAssetHosts`. |
| `src/ScarletMeter.App/DeferredWindowClose.cs` | The close ordering that keeps Blazor from crashing the process. |
| `src/ScarletMeter.App/Settings/` | `MeterSettings` (the model), `SettingsNormalizer` (validation), `SettingsStore` (durable file), `SettingsService` (live settings), `HotkeyGesture`, `BackgroundImages`. |
| `src/ScarletMeter.App/Pages/`, `Components/`, `wwwroot/` | The UI: `MeterPage` (overlay), `SettingsPage`, shared setting controls, one stylesheet. |

## Upstream boundary

The boundary keeps upstream updates a one-line pin change and keeps Scarlet's own code independent of upstream's internals.

- **Never edit `upstream/`.** It is a submodule; a change there would be lost on the next pin and would fork upstream silently. A fix upstream needs goes to upstream, or to the harrynguon/AIon2-Dps-Meter fork, which carries minimal generic fixes.
- **Releases only.** The pin moves with `scripts/Update-Upstream.ps1 -Tag vX.Y.Z.N`, which refuses anything that is not a release tag. Upstream's `dev` branch is never used.
- **One project references upstream.** `ScarletMeter.Engine` references `AionDpsMeter.Core` and `AionDpsMeter.Services` through `$(ScarletUpstreamRoot)` (src/Directory.Build.props). `ScarletMeter.App` references the engine only. The one other contact is build-time content: the App project copies upstream's class icons (`AionDpsMeter.UI/wwwroot/images/classes/*.png`) beside the executable (see Image hosts). That reads files from the submodule; it adds no code or assembly reference.
- **No upstream type in the engine's public surface.** The app and future modules program against Scarlet's records and interfaces.
- **Build settings do not leak into upstream.** Scarlet's settings live in `src/Directory.Build.props` (imported by `tests/`), never at the repository root. MSBuild imports only the nearest `Directory.Build.props` above a project, so the root file reaches only the upstream projects, which ship none: that file is the upstream overlay (below), nothing else.

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
| Moved or renamed class icons in upstream's UI project | The wildcard copies nothing: no class icons in the pages; `Test-PublishLayout.ps1` (CI) fails | The `None` item in `ScarletMeter.App.csproj` |

## Engine contract

`ICombatEngine` is the whole surface: `Start`, `Stop`, `Reset`, `Capture` (running, adapter name once locked), `GetSnapshot()` and `PingUpdated`. `CombatSnapshot` is immutable: the active target (name, HP, boss flag), duration, party DPS and one `PlayerRow` per player (identity, class, totals, DPS, damage share, critical rate, deaths). Players the game has not named yet are included with `IsIdentified` false; the UI hides them until they hold over one percent of the damage, as upstream's UI does.

`Start` throws `CaptureStartException` in the two cases upstream fails on: Npcap's `wpcap.dll` cannot be loaded (the message says how to install Npcap), or upstream found no eligible adapter: Npcap listed none, or only kinds upstream skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which, so the message names both and suggests running as administrator when Npcap is restricted to administrators. An adapter that is found but fails to open is only logged by upstream, so `Start` can return with nothing listening; the header then stays at "Waiting for the game's connection...".

`Reset` maps to `CombatSessionManager.Reset` only, which runs under the session manager's lock and drops every target's session, the active target and buffered buffs. Upstream's `IPacketService.Reset` is deliberately not called: it also clears the summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture thread writes them, and upstream's own UI never calls it.

Threading: upstream's opcode processors write `EntityTracker` and `CombatSessionManager` on the capture thread. `GetSnapshot` reads the session manager under its lock, but player identities come from `EntityTracker` dictionaries that the capture thread writes outside that lock; upstream's own UI reads them the same way. A racing read can return a stale value or throw; `MeterPage` logs the first failure of a run and retries on the next tick.

`EngineOptions` is host policy: an absolute `DataDirectory` (default `%LocalAppData%\ScarletMeter`, a relative path throws) holding everything the meter writes (see Storage), and `CaptureMode` (`Live` or `None`). `EngineSettings` are the runtime toggles upstream consults on every hit (boss-only capture, grouped summon damage, one-minute dummy mode, history retention); the bridge forwards their `Changed` event to upstream's `SettingsChanged`.

Deliberately unsupported through the bridge: upstream's raw packet logging (`IsPacketLoggingEnabled` is always false and no `FilePacketWriter` is registered) and its buff and skill-cooldown overlays (disabled, so the timed trackers track nothing). Upstream's update checker is not registered.

## Windows

Every window is a `ScarletWindow`: a WPF window whose content is one `BlazorWebView` with one root component. The base class sets the WebView2 profile folder and image hosts (below), remembers the window's placement, and applies the deferred close. There is no XAML window; windows are built in code.

- **Overlay** (`OverlayWindow`, root `MeterPage`): chromeless (`WindowStyle=None`, `AllowsTransparency`), transparent WebView2 background, always on top, resizable from every edge through `WindowChrome` (`ResizeBorderThickness` 6, no caption area). The page paints its own panel; only the panel's `::before` layer takes the background opacity and picture, so text and bars stay solid. A left-button press on the header calls `IMeterWindows.BeginOverlayDrag`, which releases mouse capture and *posts* `WM_NCLBUTTONDOWN`/`HTCAPTION`, so Windows' modal move loop starts after the Blazor event handler has returned instead of inside it. Aero Snap's maximize is undone. Closing it closes the tool windows first (each through its own deferred close, awaited for at most five seconds), then the app.
- **Tool windows** (`ToolWindow`): standard frame in dark mode (`DWMWA_USE_IMMERSIVE_DARK_MODE`), topmost, owned by the overlay, so they stay above it and minimize and restore with it. One of each kind; opening an open one restores and activates it. They open beside the overlay (right if it fits on the overlay's monitor's work area, else left) unless a placement is saved. This build has the settings window.
- **`MeterWindows`** (`IMeterWindows` to the pages) creates the windows and owns the global hotkey. It runs the file picker from its own dispatcher operation (a modal dialog must not run inside the Blazor event that asked for it), and while the picker is open every meter window is disabled and refuses to close: closing one would release Blazor content under the modal loop, which terminated the process (reproduced in review). Once the overlay starts closing, no tool window opens and the hotkey and minimize do nothing.
- **Hotkey** (`GlobalHotkey`, with the decisions in `HotkeyBinding`): `RegisterHotKey` on the overlay's handle with `MOD_NOREPEAT`; `WM_HOTKEY` minimizes the overlay (and so its tool windows) or restores and activates it, RATmeter's behaviour. Default `Ctrl+Shift+D`, as RATmeter. A combination another program owns is reported in settings rather than failing silently, and shortcuts Windows or most programs rely on (Alt+F4, Ctrl+C and similar) are refused. While the settings page records a new shortcut the hotkey is unregistered, so pressing the current one is recorded rather than acted on; recording ends on blur, a key, Escape, Clear, or after 15 seconds.
- **Placement** (`WindowPlacementTracker`): saved in settings 400 ms after a move or resize stops and on close, normal state only; restored before the window is shown unless it would overlap the virtual screen by less than 80×40 DIPs (a monitor was removed).

## Settings

`MeterSettings` holds four sections: `Fight` (boss-only capture, combined summon damage, one-minute dummy mode, history retention; mirrored into the engine's `EngineSettings` on load and on every change), `Appearance` (background opacity 0.1 to 1, background picture, class colours, relative bars, show deaths, hide names, row size 0.75 to 1.5), `Overlay` (the hotkey) and `Windows` (placements). Changes apply live: pages subscribe to `SettingsService.Changed`.

- **Validation** (`SettingsNormalizer`) is the single boundary for both the file and every update: missing or null sections take their defaults, numbers are clamped, the hotkey must parse and is stored canonically, unknown window keys and non-finite or absurd placements are dropped, and the background picture must be a name `BackgroundImages` writes (`background-<8 hex>.<ext>`), so the file cannot point the page at another path. Each repair is logged and shown once in settings.
- **Durability** (`SettingsStore`): `settings.json` in the data directory, at most 1 MB, written to a temporary file that replaces the old one (`File.Replace`), keeping the previous save as `settings.json.bak`. A file that cannot be parsed is renamed to `settings.unreadable-<UTC time>.json` before anything is written, then the backup is tried, then defaults; a missing file with a usable backup restores the backup. Saving is switched off for the run (`CanSave` false, shown in settings) whenever a write could destroy something: a file whose version is newer than this build, a file that cannot be opened, a damaged file or backup that cannot be renamed aside, or a backup that cannot be opened. In that mode the background picture is not changed either, so the protected file's picture stays.
- **Saving** (`SettingsService`): 500 ms after the last change (so a slider drag writes once), and on exit when the host disposes the service. A failed save is logged and shown in settings.
- **Background picture**: picked with the system file dialog, at most 8 MB, copied into `<data dir>\backgrounds` under a content-hash name (the page reloads it when it changes; the original may move), earlier pictures deleted.
- **Hide names** masks every name, yours included, with RATmeter's algorithm (`NicknameMask`), so a masked name reads the same in both meters.

### Image hosts

The pages load images not shipped in `wwwroot` through WebView2 virtual hosts (`WebAssetHosts`), each mapped to exactly one folder so the page can never read the database, logs or settings: `https://data.scarlet.example/` is `<data dir>\backgrounds`, and `https://assets.scarlet.example/` is `assets\` beside the executable, which holds the class icons copied at build time from upstream's `AionDpsMeter.UI/wwwroot/images/classes` (read from the submodule, never edited). `index.html` carries a Content-Security-Policy: scripts only from the app (`blazor.webview.js` needs neither eval nor inline script), images only from the app, those two hosts and NC's game-data CDN (for skill icons), no plugins (`object-src 'none'`), `base-uri 'self'`; inline styles are allowed because the pages set widths and colours in style attributes.

## Window lifecycle

WPF disposes a window's WebView2 control when the window closes, but BlazorWebView is never disposed automatically, and a Blazor message sent afterwards terminates the process. `ScarletWindow` therefore cancels its first close, awaits `BlazorWebView.DisposeAsync` on a later dispatcher turn (components first, so `MeterPage.DisposeAsync` stops the refresh loop and unsubscribes from settings and ping events), then closes again. `DeferredWindowClose` is ported from the fork, where the crash was reproduced and the fix is covered by unit tests and a native lifecycle check. The unit tests are ported here (`DeferredWindowCloseTests`); the native check is not yet (see HANDOFF.md).

BlazorWebView 10's `DisposeAsync` disposes the components and then the WebView2 composition control on whichever thread the component disposal completed on; the control refuses cross-thread COM access with an `InvalidCastException` (E_NOINTERFACE). `ScarletWindow.ReleaseBlazorContentAsync` catches that and disposes the control again on the UI thread, which succeeds; both steps are logged at Information. The fork's companion window logs the same failure, so the fallback is a candidate fix to offer it.

Shutdown order: tool windows close (deferred, awaited), the overlay closes, `App.OnExit` disposes the engine (stops capture, completes open fights, flushes history), then the host.

Startup failures: any exception while creating the data directory, building the host, constructing upstream's graph (game data, history database) or showing the window ends in a "could not start" dialog and exit code 1; a failed capture start gets its own dialog. Once the host exists the failure is also logged at Critical (Error for capture). Before that (bad arguments, a data directory that cannot be created) there is no log file, and the dialog is the only report. Unhandled exceptions on the UI thread or elsewhere are logged at Critical, not handled, so the process still terminates.

## Storage

Everything the meter writes lives under `EngineOptions.DataDirectory`:

- `combat-history.db`: upstream's EF Core SQLite store of completed boss fights, retention per `EngineSettings.HistoryRetentionDays`.
- `logs/scarlet-<date>.log`: Serilog, 14 days.
- `settings.json`, `settings.json.bak` (the previous save), and any `settings.unreadable-<UTC time>.json` kept from a damaged file (see Settings).
- `backgrounds/`: the imported background picture.
- `webview2/`: the WebView2 browser profile (cache, local storage, GPU cache; no meter data). `ScarletWindow` sets it through `BlazorWebViewInitializing`; otherwise BlazorWebView would use `%LocalAppData%\ScarletMeter.WebView2` and `--data-dir` would not isolate a run.

Nothing is written next to the executable or in the working directory. Upstream's `appsettings.user.json` is not used.

## Tests

- `UpstreamBoundaryTests`: the app references no upstream assembly; no Scarlet project but the engine declares an upstream project reference (and the check confirms it sees the engine's); the engine's public surface (public and protected members) exposes no upstream type, with a self-check that the surface scan sees an upstream interface.
- `EngineRegistrationTests`: the real upstream graph resolves without Npcap, for `CaptureMode.None` and the production `Live` graph (scope validation on; every opcode processor is constructed through the registry; `ValidateOnBuild` stays off because upstream also registers nested and compiler-generated types from its processor namespace that nothing resolves); a snapshot maps synthetic hits fed to upstream's session manager (identity, totals, share, critical rate, duration, party DPS); reset empties it and keeps summon ownership; the two capture-start failures get their own messages; packet logging cannot be enabled; settings and their change event reach upstream.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `SettingsTests`: defaults, every repair the normalizer makes (including null sections from a file), canonical hotkeys, value equality covering every property (a new property fails the test until `Equals` includes it), save/load round trip with the backup, an unreadable file kept aside with the backup restored, defaults when there is no backup, an oversized file, a newer-version file never overwritten, live application to `EngineSettings` with one event per real change, saved fight rules reaching the engine at load, hotkey parsing and browser key capture, background picture import and validation.
- `FormattingTests`: RATmeter-compatible name masking, ping bands, and that pages only get image URLs on the two local hosts.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by CI after the publish) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

`scripts/Smoke-Meter.ps1` drives the built meter for UI checks: it starts it with a throwaway data directory and WebView2 remote debugging on a localhost port for that run only, finds each window's page by its `data-window` attribute, evaluates JavaScript (read text, click, set inputs), captures pages, drags the overlay with the real mouse, then closes the overlay through UI Automation and reports the exit code. See DEVELOPMENT.md.

None of these validate live capture or decoding. That needs the game (HANDOFF.md, in-game acceptance).

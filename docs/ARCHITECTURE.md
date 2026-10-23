# Architecture

Scarlet Meter is a .NET 10 WPF application hosting a Blazor WebView. Capture and decoding of the game's traffic are not written here: they come from RATmeter (Kuroukihime/AIon2-Dps-Meter), included unmodified as the `upstream/` git submodule, pinned to release tag **v1.11.5.341** (commit aa3ffc6).

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
| `src/ScarletMeter.WorldEvents` | World event schedules and the overlay's alert planner (see World events). |
| `src/ScarletMeter.App/Alerts/`, `Components/AlertStrip.razor` | The overlay's world-event notices: `AlertMonitor` and the strip (see Overlay notices). |

## Upstream boundary

The boundary keeps upstream updates a one-line pin change and keeps Scarlet's own code independent of upstream's internals.

- **Never edit `upstream/`.** It is a submodule; a change there would be lost on the next pin and would fork upstream silently. A fix upstream needs goes to upstream, or to the harrynguon/AIon2-Dps-Meter fork, which carries minimal generic fixes.
- **Releases only.** The pin moves with `scripts/Update-Upstream.ps1 -Tag vX.Y.Z.N`, which refuses anything that is not a release tag. Upstream's `dev` branch is never used.
- **One project references upstream.** `ScarletMeter.Engine` references `AionDpsMeter.Core` and `AionDpsMeter.Services` through `$(ScarletUpstreamRoot)` (src/Directory.Build.props). `ScarletMeter.App` references the engine and `ScarletMeter.WorldEvents` (which references nothing), never upstream. The one other contact is build-time content: the App project copies upstream's class icons (`AionDpsMeter.UI/wwwroot/images/classes/*.png`) beside the executable (see Image hosts). That reads files from the submodule; it adds no code or assembly reference.
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
- **Durability** (`SettingsStore`): `settings.json` in the data directory, at most 1 MB (a UTF-8 byte-order mark from a hand edit is accepted), written to a temporary file that replaces the old one (`File.Replace`), keeping the previous save as `settings.json.bak`. A file that cannot be parsed is renamed to `settings.unreadable-<UTC time>.json` before anything is written, then the backup is tried, then defaults; a missing file with a usable backup restores the backup. Saving is switched off for the run (`CanSave` false, shown in settings) whenever a write could destroy something: a file whose version is newer than this build, a file that cannot be opened, a damaged file or backup that cannot be renamed aside, or a backup that cannot be opened. In that mode the background picture is not changed either, so the protected file's picture stays.
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

## World events

`src/ScarletMeter.WorldEvents` holds the schedule behind the overlay's world-event notices: the built-in rules, their validation, the occurrence calculation and the alert planner. It is ported from the World Companion of the harrynguon/AIon2-Dps-Meter fork (`AionDpsMeter.Companion/wwwroot/schedule.mjs`, `presets.mjs` and `state.mjs`, JavaScript, at 4feb72b), together with its tests; the alert planner is new. The overlay's notices use it (Overlay notices, below).

The module is pure .NET and independent: it references no project in this repository, no upstream assembly, no UI framework and no package (`WorldEventsBoundaryTests`), so it can move to another meter unchanged. Storage, the settings format and every other host policy belong to the host. Nothing in it reads the system clock or the PC's time zone: the current instant and the server offset are parameters, instants are compared in UTC and every instant it returns is UTC (offset zero).

| Type | Purpose |
| --- | --- |
| `EventRule` | Immutable rule: `Id`, `Name`, `Location`, `Kind` (`EventKind`: Boss, Rift, Event, Reset), `Basis` (`ScheduleBasis`: Utc, Server), `Days` (null = every day; 0 = Sunday to 6 = Saturday), `Hours`, `Minute`, `DurationMinutes`, `EntryMinutes?`, `ActiveMinutes?`. `Hours` and `Days` are copied, de-duplicated and sorted on construction (and by `with`) and compare by value. Ranges are not enforced on construction, so a host can build rules from untrusted settings and validate them. |
| `EventIds`, `EventCatalog` | The nine ids (`kaira`, `nahma`, `executors`, `rift`, `siege`, `invasion`, `shugo`, `daily`, `weekly`, the fork's strings) in display order; the presets `Legacy` and `Global`, with `Default` = `Global`; `DefaultServerOffset` (UTC+9); `TryApplyOverrides`. |
| `ScheduleValidation` | `ValidateRules` and `ValidateServerOffset` return `ScheduleError(RuleId, Message)` lists, empty when valid. |
| `EventSchedule` | `Occurrence(rule, now, serverOffset)` returns an `EventOccurrence`; `Timeline(rules, now, serverOffset)` returns them all, ordered. |
| `AlertPlanner` | `Plan(rules, enabledIds, now, serverOffset[, leadTime])` returns an `AlertPlan`: the `EventAlert`s to show and when they next change. |

### Occurrences

- An occurrence starts on each listed weekday at each listed hour and `Minute`, read in the rule's clock: UTC, or UTC plus the server offset. The offset is fixed, so a region with daylight saving needs it changed by hand, as in the fork. A start at exactly `now` has begun; a window or entry window that ends at exactly `now` has closed.
- The window runs from the start for `ActiveMinutes ?? DurationMinutes`. A duration of 0 is never active: resets, and the boss and siege windows of the Global preset, whose end times are unknown. The entry window is `EntryMinutes`, or a rift's `DurationMinutes` (the portal).
- `EventOccurrence`: `Phase` (`Upcoming`, `PortalOpen` for a rift's entry window, `EntryOpen`, `ActiveEntryClosed`, `ScheduledActive` for a window without an entry window), `IsActive`, `IsEntryOpen`, `Start` (the running occurrence while active, otherwise the next one), `Next` (the first start after `now`), `End`, `EntryEnd`, `TargetEnd` (the entry close while entry is open, the end while otherwise active, else the next start), `Remaining` (to `TargetEnd`, always positive) and `Progress` (0 to 100: elapsed share of the countdown while active, share of the final hour while upcoming).
- `Timeline` orders active occurrences first, then by `Remaining`, then by id, and keeps simultaneous events. The id tiebreak is ordinal; the fork's `timeline()` uses `localeCompare`, which gives the same order for the catalog's lowercase ASCII ids.
- `Occurrence`, `Timeline` and `Plan` throw `ArgumentException` for a rule that fails the value checks below and `ArgumentOutOfRangeException` for an invalid server offset: validated input is their precondition. They also throw `ArgumentOutOfRangeException` when an instant to return lies outside `DateTimeOffset`'s range: the next start for a `now` within a week of `DateTimeOffset.MaxValue`, or the start of a window still running within a day of `DateTimeOffset.MinValue` (both presets work at `MinValue` itself). No real clock gets there.

Deliberate differences from the fork: `End` and `EntryEnd` belong to the occurrence at `Start`, so they lie ahead for an upcoming event (the fork's `end` belonged to the most recent start, in the past for an upcoming event; its UI never read it). Minutes are whole numbers and the server offset is a whole number of minutes (the fork accepted any finite number; whole minutes keep the offset usable as a `DateTimeOffset` offset). Validation returns every problem rather than throwing at the first.

### Validation and saved rules

`ValidateRules` makes the checks of the fork's `validateEvents` and `validateState`: each of the nine ids exactly once (missing, duplicate and unknown entries are reported), a defined clock, hours 0–23 (at least one), days 0–6 (at least one) or null, minute 0–59, duration 0–1440, entry window 0 to the duration, activity from the duration to 1440, and an activity length for the rift. Kinds come from the catalog, never from the input. Unsorted or repeated hours and days are not errors, since rules normalise them. `ValidateServerOffset` accepts −12:00 to +14:00 in whole minutes, so +5:30 and +5:45 pass. Each error names the catalog id it concerns, or null for list-level problems and unknown ids; input text is never copied into an error. The work and the result are bounded for hostile input: a list of more than `MaximumEntries` (twice the catalog, 18) gets the single error "too many entries", and at most `MaximumReportedErrors` (50) errors are returned, the last saying that more exist. The size of the file the rules come from is the host's to bound before it deserialises.

`TryApplyOverrides(saved, out rules, out errors)` is the fork's `validateState` merge: when the saved rules pass, it returns them in catalog order with the catalog's id, name, location and kind, taking only the clock, days, hours, minute, duration and entry window from the saved rule, plus the activity length for the rift (a saved activity length on any other entry is dropped). On failure the host decides: reject the file (the fork runs on defaults and blocks writes until the player recovers) or repair the named entries, for example from `EventCatalog.Default`. File versioning stays with the host's settings format.

### Alert planner

`AlertPlanner.Plan` gives each enabled rule at most one alert:

- **Open** while its portal or entry window is open, from the start (inclusive) to the close (exclusive). `Deadline` is the close. Resets never get it.
- Otherwise **Soon** while its next start is at most the lead time away, from exactly start minus lead time (inclusive) to the start (exclusive). `Deadline` is the start.
- Nothing else: an event that is active without an open entry window (a legacy boss or siege window, or a Shugo or Invasion window after entry has closed) gets no alert, though its next start still gets Soon. While Open shows, it hides a Soon for the same rule.

Each `EventAlert` carries the rule, `Kind`, the occurrence's `Start`, the `Deadline` and `Remaining` (to the deadline). Alerts are ordered Open first, then by earliest deadline, then by id. The lead time defaults to five minutes (`DefaultLeadTime`) and must be 1–60 minutes, otherwise `ArgumentOutOfRangeException`. Rules whose ids are not in `enabledIds` are skipped without being checked.

`AlertPlan.NextChange` is the first instant after `now` at which the alerts differ: one appears, disappears or moves to another occurrence. Before it only the countdowns move, so the overlay can show the alerts, count down to each deadline itself and call `Plan` again at `NextChange` rather than every second. Per rule it is the close or the next start, whichever is first, while Open shows; otherwise the next start minus the lead time while that is ahead, else the next start. It is null when no rule is enabled. A test walks eight days minute by minute for both presets and checks that the plan is constant up to `NextChange` and different at it. Planning allocates nothing when there is no alert, and one or two small arrays otherwise.

### Global preset

Scarlet's default, as the fork's: its legacy defaults with the times and window lengths of taengu/A2Tools-Plugins' event-timers data (fork docs/TIMER-COMPARISON.md; provenance in SECURITY.md).

| Entry | Clock | Days | Starts | Window |
| --- | --- | --- | --- | --- |
| Watcher Kaira | server | every day | 01:00, then every 3 hours | unknown (duration 0) |
| Guardian Lord Nahma | server | Sun, Fri | 21:00 | unknown |
| Executors Tamasa & Argo | server | Mon, Thu, Sat | 21:30 | unknown |
| Spacetime Rift | server | every day | 02:00, then every 3 hours | 10-minute portal, no longer activity |
| Artifact Siege | server | Mon, Thu, Sat | 21:00 | unknown |
| Dimensional Invasion | server | every day | every hour at :30 | 3-minute entry, 13 minutes in all |
| Shugo Festival | server | every day | every hour at :00 | 3-minute entry, 7 minutes in all |
| Daily reset | UTC | every day | 07:00 | instant |
| Weekly reset | UTC | Wed | 07:00 | instant |

`EventCatalog.Legacy` keeps the fork's original defaults for comparison: Kaira on the UTC clock with a 30-minute window, estimated 45/30/45-minute Nahma, Executors and siege windows, rifts at 00:00 UTC every three hours with a 10-minute portal and 60 minutes of activity, 15-minute Shugo and Invasion windows without an entry window, and resets at 09:00 server.

### Overlay notices

The app shows the planner's alerts as a slim strip at the bottom of the overlay (`Components/AlertStrip.razor`): a pulsing dot, the event's name and "in 4:59" before a start, with a bar filling over the lead time; "Portal open · 7:12 left" or "Entry open · 2:10 left" while entry is possible. At most two show, the rest are summarised; hovering one shows its location and local time. With "show the next event" on, a dim line names the next alerted event when nothing is due. Screen readers hear a hidden summary ("Shugo Festival starts at 21:00") that changes only when a notice appears or changes kind, not the per-second countdown.

- `Alerts/AlertMonitor` (singleton) computes the view from `AlertSettings` and the clock (`TimeProvider`), and sleeps until the plan's `NextChange` plus 20 ms, never more than a minute, so a sleep, resume or clock change is corrected within a minute. It raises `Changed` (the overlay and the settings window's Alerts tab re-render) and `EventApproaching` once per event occurrence, when its Soon notice first appears. Announced occurrences are kept until they start, so switching an event off and on or changing the lead time does not announce one again. A failure in the computation or in a subscriber is logged and never thrown into the timer thread or the settings caller; after a failed computation the view is empty and the monitor tries again a minute later. `Start` is idempotent and does nothing after `Dispose`.
- Countdowns are the page's: `MeterPage` passes the current time to the strip on every 100 ms render.
- `MeterWindows.FollowAlerts`: on `EventApproaching`, if the meter is minimized and the player opted in (setting "Bring the meter back before an event", off by default so a hidden meter stays hidden), it is shown again with `ShowWindow(SW_SHOWNOACTIVATE)`, so the game keeps keyboard focus. That is once per occurrence, never for an Open notice. Owned tool windows come back with it.
- `AlertSettings` (settings.json `alerts`): on/off, lead time 1 to 60 minutes (default 5), the alerted event ids (default: the world events and bosses, not the resets), the server UTC offset in whole minutes (default +9), the next-event line (off) and bringing the meter back (off). The normalizer keeps only catalog ids, once each, in catalog order. The schedule itself is always the catalog's Global preset; per-event time overrides (the module's `TryApplyOverrides`) are not in the settings yet.
- The settings window's Alerts tab lists each event with its schedule ("Every 3 h from 02:00 server time · portal 10 min") and its state in the PC's local time (`EventText.Status`): "next 21:05", "next Sun 22:00" when not today, "portal open now", "entry open now", "running, entry closed", or "started 21:00" for a window without entry. It shows no countdown, since it re-renders only when the monitor wakes (at least once a minute). It repeats the accuracy warning below.

**Accuracy: not verified against Global Asia / Nezekan.** UTC+9 is an assumption. The fork's evidence (its HANDOFF.md) is consistent with the preset but proves no exact transition: a daily-completion countdown consistent with 16:00 server (07:00 UTC), the player's report of the weekly reset at Wednesday 18:00 Melbourne during AEDT (07:00 UTC), and two rift reports, rounded to the minute, at about 08:00 and 11:01 server. Entry closures (Shugo and Invasion after 3 minutes, the rift portal after 10), boss and siege times, and resets outside Australian daylight saving are unobserved. A scheduled window is not a live report: a boss may already be dead. The A2Tools reproduction cases of the fork's TIMER-COMPARISON.md are tests here; passing them shows agreement with A2Tools' arithmetic, not with the game. In-game verification is listed in HANDOFF.md.

## Updates

There is no self-updater. RATmeter's updater downloads a published release and replaces its own executable; Scarlet Meter is a private build made from this repository, with no published binaries to download, and swapping in a binary from elsewhere would skip the build, the tests and the audit-as-errors restore. What changes for the player is upstream: a game patch breaks decoding until upstream releases parser fixes, and the meter gets them when the submodule pin moves. Updating is therefore: notice the release, move the pin (`scripts/Update-Upstream.ps1`), rebuild and reinstall (`scripts/Install-Meter.ps1`). Commands are in DEVELOPMENT.md ("Install and update").

- **Build stamp.** `src/ScarletMeter.App/Updates/BuildStamp.targets` runs before the SDK generates the assembly attributes and stamps two `AssemblyMetadata` entries into the app: `ScarletUpstreamRelease`, the tag naming the submodule's commit (`git describe --tags --exact-match` in `$(ScarletUpstreamRoot)`), else the submodule's short commit, else `unknown`; and `ScarletCommit`, Scarlet's short commit, else `unknown`. A missing git, repository, submodule or tag changes the value and never fails the build. The target runs on every build; the SDK hashes the attributes, so only a changed value recompiles. `BuildInfo.Current` reads the stamp. A clone made with `--recurse-submodules` has upstream's tags; CI's shallow submodule clone lacks the pinned one, so CI fetches it first (DEVELOPMENT.md, "CI").
- **Release check.** `UpstreamReleaseChecker` (behind `IUpstreamReleaseChecker`, registered by `AddUpstreamReleaseChecker`) asks `https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest` for upstream's latest release and compares it with the stamp. It reads only `tag_name`, `html_url` and `published_at`. The tag must be a release tag, `v` and four numeric parts (ASCII digits, nothing after), compared as numbers, so v1.10.0.1 is newer than v1.9.4.277. The page must be an https page under `https://github.com/Kuroukihime/AIon2-Dps-Meter/`, with no query, fragment or percent-encoding, so the UI can open it. The result is `UpToDate`, `NewerAvailable` (tag, page, publication date) or `Unknown` (an `UpstreamReleaseCheckFailure` and a one-sentence reason: pinned release unknown, timeout, network, rate limit, 404, other HTTP status, invalid answer, cancelled by the caller or by the meter closing, unexpected). It never throws to callers; failures are logged as warnings with the reason only, never GitHub's text. The last result is cached in memory, not on disk.
- **Network footprint.** At most one unauthenticated HTTPS GET to api.github.com every six hours per process once GitHub has answered (a release, a rate limit, a 404 or an invalid answer alike), timed from the start of the previous request; after a request that got no answer (no connection, or no reply within the timeout) the next may follow ten minutes later, so a meter started offline still learns of a release that session. GitHub counts no request it never answered, so the retry cannot worsen a rate limit. Concurrent callers share one request. The request carries a fixed User-Agent, `Accept: application/vnd.github+json` and `X-GitHub-Api-Version`: no token, no cookies, nothing about the player, the game or combat. Redirects are not followed, the timeout is ten seconds and at most 1 MiB of answer is read. A build stamped with a commit or `unknown` sends nothing, and nothing is sent until the UI calls `CheckAsync`. The checker is not registered in `App.xaml.cs` yet: the UI rewrite wires it into the overlay (a quiet notice when a newer release exists) and into settings (a switch that turns the check off, so no request is made at all).
- **Install.** `scripts/Install-Meter.ps1` builds Release with the tests, publishes self-contained single-file win-x64 into `<install>.staging` beside the install (default `%LocalAppData%\Programs\ScarletMeter`), checks it with `Test-PublishLayout.ps1`, writes the install marker `.scarlet-install` (fixed text) into it and swaps folders by renames, keeping the old install as `<install>.previous` for a manual rollback. If a rename fails, each earlier one is undone on its own and the error lists where every folder is. It renames or deletes only folders that carry its marker, or are empty, and none that holds a link; it refuses paths that are not on a local drive (UNC, admin shares, `\\?\`, `\\.\`) and install folders overlapping the data directory or the checkout, compared after Windows resolves links, junctions, 8.3 names and substituted drives (`GetFinalPathNameByHandle`) in the install's parent and, when it exists and is not a link, the install folder itself. It refuses while the meter runs from the install, and never touches the data directory. Deletes remove the marker last, so a delete stopped by an open file leaves a folder the next install still recognises. The folder rules live in `scripts/InstallFolders.ps1`, which the installer dot-sources. `scripts/Test-InstallMeter.ps1` checks the refusals with `-CheckOnly`, which builds and moves nothing, and runs the delete and swap paths from that library on fake install folders: a `.retired` held open keeps its marker and blocks the swap, then goes once released, and a failed rename is undone.

## Tests

- `UpstreamBoundaryTests`: the app references no upstream assembly; no Scarlet project but the engine declares an upstream project reference (and the check confirms it sees the engine's); the engine's public surface (public and protected members) exposes no upstream type, with a self-check that the surface scan sees an upstream interface.
- `EngineRegistrationTests`: the real upstream graph resolves without Npcap, for `CaptureMode.None` and the production `Live` graph (scope validation on; every opcode processor is constructed through the registry; `ValidateOnBuild` stays off because upstream also registers nested and compiler-generated types from its processor namespace that nothing resolves); a snapshot maps synthetic hits fed to upstream's session manager (identity, totals, share, critical rate, duration, party DPS); reset empties it and keeps summon ownership; the two capture-start failures get their own messages; packet logging cannot be enabled; settings and their change event reach upstream.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `SettingsTests`: defaults, every repair the normalizer makes (including null sections from a file), canonical hotkeys, value equality covering every property (a new property fails the test until `Equals` includes it), save/load round trip with the backup, an unreadable file kept aside with the backup restored, defaults when there is no backup, an oversized file, a byte-order mark, a file from before alerts, a newer-version file never overwritten, live application to `EngineSettings` with one event per real change, saved fight rules reaching the engine at load, hotkey parsing and browser key capture, background picture import and validation.
- `AlertTests`: the overlay notices over the Global preset (Soon, Open, lead time, offset, the next-event line), alert settings normalisation and equality, wording, and `AlertMonitor` on a fake clock: one signal per occurrence, its wake-ups, recovery from a failing computation or subscriber, idempotent start.
- `FormattingTests`: RATmeter-compatible name masking, ping bands, and that pages only get image URLs on the two local hosts.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).
- `tests/ScarletMeter.Tests/WorldEvents/`: the world-events module. `EventScheduleTests` ports the fork's recurrence cases (UTC invariance across offsets, Friday-to-Sunday rollover, exact rift phase boundaries, year and week rollover, fractional offsets, simultaneous events, the Global preset's entry boundaries) and its A2Tools reproduction table, and adds weekdays at the −12 and +14 limits, an instant's offset not mattering, progress, argument checks and the `DateTimeOffset` range limits. `ScheduleValidationTests` ports the validation and `validateState` cases (each bad value against its entry, the rift's activity, missing, duplicate and unknown entries, offsets, merging keeps every editable value and takes metadata from the catalog) and checks that hostile input gets a bounded answer. `EventCatalogTests` pins both presets value for value and the rules' immutability and value equality. `AlertPlannerTests` covers the planner's boundaries (exactly at the lead time, at the start, at the entry close), resets, windows without entry, disabled rules, lead-time and offset checks, ordering, an eight-day walk proving `NextChange` exact, and no allocation without alerts. `WorldEventsBoundaryTests`: the module references only the .NET base library (no `ScarletMeter.*`, `AionDpsMeter.*`, UI or package assembly) and declares no dependency in the build graph.
- `BuildInfoTests`: the app carries a stamp, a release tag or a short commit (in CI, exactly the pinned tag CI fetched, named by `SCARLET_EXPECTED_UPSTREAM_RELEASE`); an assembly without one reads as `unknown`; upstream's assemblies and the engine carry none; which strings are release tags (no `\d` look-alikes, no trailing newline, each part fits an int).
- `UpstreamReleaseCheckerTests`: against a fake handler and `FakeTimeProvider`, never the network: a newer release with its page and date, same or older releases up to date, numeric comparison; the request is one GET with only the three headers; the six-hour cache (answered failures included), the ten-minute retry after a network failure or a timeout, and one shared request for concurrent callers; the ten-second timeout for headers and for the body; 403 and 429 rate limits, 404, redirects and other statuses; malformed JSON, invalid tags and pages off upstream's repository (other host, http, user info, port, `..`, query); answers of exactly 1 MiB read and one byte more rejected, with and without a length; a gzip answer inflated by the production handler and the cap counting inflated bytes (the production handler's settings with its proxy turned off, against a one-shot server on the loopback address, so nothing leaves the machine even with `HTTP_PROXY` or a system proxy set); disposing mid-check ends it as cancelled, without a warning; network and unexpected exceptions; no request for an unknown pinned release; caller cancellation keeps the shared request; GitHub's text never reaches the log; the registration and the production handler (no redirects, no cookies).

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by `Install-Meter.ps1` after its publish, in CI too) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

`scripts/Smoke-Meter.ps1` drives the built meter for UI checks: it starts it with a throwaway data directory and WebView2 remote debugging on a localhost port for that run only, finds each window's page by its `data-window` attribute, evaluates JavaScript (read text, click, set inputs), captures pages, drags the overlay with the real mouse, then closes the overlay through UI Automation and reports the exit code. See DEVELOPMENT.md.

None of these validate live capture or decoding. That needs the game (HANDOFF.md, in-game acceptance).

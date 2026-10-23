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
| `src/ScarletMeter.WorldEvents` | World event schedules and the overlay's alert planner (see World events). Not wired into the app yet. |

## Upstream boundary

The boundary keeps upstream updates a one-line pin change and keeps Scarlet's own code independent of upstream's internals.

- **Never edit `upstream/`.** It is a submodule; a change there would be lost on the next pin and would fork upstream silently. A fix upstream needs goes to upstream, or to the harrynguon/AIon2-Dps-Meter fork, which carries minimal generic fixes.
- **Releases only.** The pin moves with `scripts/Update-Upstream.ps1 -Tag vX.Y.Z.N`, which refuses anything that is not a release tag. Upstream's `dev` branch is never used.
- **One project references upstream.** `ScarletMeter.Engine` references `AionDpsMeter.Core` and `AionDpsMeter.Services` through `$(ScarletUpstreamRoot)` (src/Directory.Build.props). `ScarletMeter.App` references the engine only.
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

## World events

`src/ScarletMeter.WorldEvents` holds the schedule behind the overlay's world-event notices: the built-in rules, their validation, the occurrence calculation and the alert planner. It is ported from the World Companion of the harrynguon/AIon2-Dps-Meter fork (`AionDpsMeter.Companion/wwwroot/schedule.mjs`, `presets.mjs` and `state.mjs`, JavaScript, at 4feb72b), together with its tests; the alert planner is new. No UI and no app wiring yet.

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

**Accuracy: not verified against Global Asia / Nezekan.** UTC+9 is an assumption. The fork's evidence (its HANDOFF.md) is consistent with the preset but proves no exact transition: a daily-completion countdown consistent with 16:00 server (07:00 UTC), the player's report of the weekly reset at Wednesday 18:00 Melbourne during AEDT (07:00 UTC), and two rift reports, rounded to the minute, at about 08:00 and 11:01 server. Entry closures (Shugo and Invasion after 3 minutes, the rift portal after 10), boss and siege times, and resets outside Australian daylight saving are unobserved. A scheduled window is not a live report: a boss may already be dead. The A2Tools reproduction cases of the fork's TIMER-COMPARISON.md are tests here; passing them shows agreement with A2Tools' arithmetic, not with the game. In-game verification is listed in HANDOFF.md.

## Tests

- `UpstreamBoundaryTests`: the app references no upstream assembly; no Scarlet project but the engine declares an upstream project reference (and the check confirms it sees the engine's); the engine's public surface (public and protected members) exposes no upstream type, with a self-check that the surface scan sees an upstream interface.
- `EngineRegistrationTests`: the real upstream graph resolves without Npcap, for `CaptureMode.None` and the production `Live` graph (scope validation on; every opcode processor is constructed through the registry; `ValidateOnBuild` stays off because upstream also registers nested and compiler-generated types from its processor namespace that nothing resolves); a snapshot maps synthetic hits fed to upstream's session manager (identity, totals, share, critical rate, duration, party DPS); reset empties it and keeps summon ownership; the two capture-start failures get their own messages; packet logging cannot be enabled; settings and their change event reach upstream.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).
- `tests/ScarletMeter.Tests/WorldEvents/`: the world-events module. `EventScheduleTests` ports the fork's recurrence cases (UTC invariance across offsets, Friday-to-Sunday rollover, exact rift phase boundaries, year and week rollover, fractional offsets, simultaneous events, the Global preset's entry boundaries) and its A2Tools reproduction table, and adds weekdays at the −12 and +14 limits, an instant's offset not mattering, progress, argument checks and the `DateTimeOffset` range limits. `ScheduleValidationTests` ports the validation and `validateState` cases (each bad value against its entry, the rift's activity, missing, duplicate and unknown entries, offsets, merging keeps every editable value and takes metadata from the catalog) and checks that hostile input gets a bounded answer. `EventCatalogTests` pins both presets value for value and the rules' immutability and value equality. `AlertPlannerTests` covers the planner's boundaries (exactly at the lead time, at the start, at the entry close), resets, windows without entry, disabled rules, lead-time and offset checks, ordering, an eight-day walk proving `NextChange` exact, and no allocation without alerts. `WorldEventsBoundaryTests`: the module references only the .NET base library (no `ScarletMeter.*`, `AionDpsMeter.*`, UI or package assembly) and declares no dependency in the build graph.

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by CI after the publish) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

None of these validate live capture or decoding. That needs the game (HANDOFF.md, in-game acceptance).

# Architecture

Scarlet Meter is a .NET 10 WPF application hosting a Blazor WebView. Capture and decoding of the game's traffic are not written here: they come from RATmeter (Kuroukihime/AIon2-Dps-Meter), included unmodified as the `upstream/` git submodule, pinned to release tag **v1.11.4.334** (commit 57974f8).

## Data flow

```
network adapters --SharpPcap/Npcap--> upstream CaptureDevice (finds the game's port by its heartbeat)
  -> upstream TcpStreamBuffer (TCP reassembly) -> upstream PacketProcessor (framing, LZ4)
  -> upstream PacketDispatchService -> opcode processors -> upstream EntityTracker + CombatSessionManager
  -> ScarletMeter.Engine UpstreamCombatEngine: GetSnapshot, GetPlayerDetails, GetHistory, GetHistoryFight
     (UpstreamMapping turns upstream's models into Scarlet records; saved fights come from upstream's SQLite store)
  -> ScarletMeter.App MeterPage (polls ten times a second) -> BlazorWebView in the overlay window

Demo mode (--demo): ScarletMeter.Engine DemoCaptureDevice stands in for upstream's CaptureDevice and runs
  DemoCombatFeed on its own thread -> upstream EntityTracker + CombatSessionManager, called as the opcode
  processors call them; everything downstream is the same.
```

| Source | Responsibility |
| --- | --- |
| `upstream/AionDpsMeter.Core` | Game data (mobs, skills, classes as JSON) and entity models. |
| `upstream/AionDpsMeter.Services` | Capture, reassembly, decoding, fight tracking, SQLite history, upstream settings. |
| `src/ScarletMeter.Engine/Contracts` | Scarlet's contracts: `ICombatEngine`, `CombatSnapshot`, `PlayerDetails` (and its rows), the history records (`HistoryQuery`, `HistoryPage`, `HistoryEntry`, `HistoryFight`), `EngineOptions`, `EngineSettings`. |
| `src/ScarletMeter.Engine/Upstream` | The only code that touches upstream types: `UpstreamCombatEngine`, `UpstreamMapping` (pure conversions shared by live details and history), `SavedFightSanitizer` and `SavedHistoryReader` (damaged history, see "Damaged history"), `UpstreamSettingsBridge`, `NoCaptureDevice`. |
| `src/ScarletMeter.Engine/Upstream/Demo` | Demo mode: `DemoCaptureDevice` (thread lifecycle), `DemoCombatFeed` (the synthetic fights), `DemoRoster` (party, skills, buffs, bosses), `DemoRosterCheck` (the roster against upstream's game data). |
| `src/ScarletMeter.Engine/ScarletEngineServiceCollectionExtensions.cs` | `AddScarletEngine`: the upstream service graph, as upstream's own App.xaml.cs builds it, minus UI, updater and packet logger. |
| `src/ScarletMeter.App/App.xaml.cs` | Composition root: host, Serilog file log, engine start, window; startup failures end in a dialog. |
| `src/ScarletMeter.App/Windows/` | `ScarletWindow` (shared Blazor hosting and close ordering), `OverlayWindow`, `ToolWindow`, `MeterWindows` (opens windows, owns the hotkey), `GlobalHotkey`, `WindowPlacementTracker`, `WebAssetHosts`. |
| `src/ScarletMeter.App/DeferredWindowClose.cs` | The close ordering that keeps Blazor from crashing the process. |
| `src/ScarletMeter.App/Settings/` | `MeterSettings` (the model), `SettingsNormalizer` (validation), `SettingsStore` (durable file), `SettingsService` (live settings), `HotkeyGesture`, `BackgroundImages`. |
| `src/ScarletMeter.App/Pages/`, `Components/`, `wwwroot/` | The UI: `MeterPage` (overlay), `SettingsPage`, shared setting controls, one stylesheet. |
| `src/ScarletMeter.WorldEvents` | World event schedules and the overlay's alert planner (see World events). Not wired into the app yet. |

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
| Re-shaped `SkillStats`, `BuffStats`, `DpsDataPoint`, `BuffTimelineEntry`, `PlayerDamage`, `GraphDataCalculator`, the `HistorySession*` types or `ICombatHistoryStore` | Compile error | `Upstream/UpstreamMapping.cs`, `Upstream/UpstreamCombatEngine.cs` |
| Changed statistics (rates, DPS, uptime, graph smoothing, summon grouping) | `PlayerDetailsTests` or `HistoryTests` fail on a hand-worked figure | Decide whether Scarlet follows; update the test and the contract's doc comments |
| Changed history schema (table `CombatSessions`, its column names or types) or JSON field names | `HistoryTests` damaged-row tests fail | `Upstream/SavedHistoryReader.cs`, the tests' JSON |
| Game data no longer has a skill, buff, class or boss the demo uses | `DemoModeTests.RosterResolvesInUpstreamsGameDataWithSyntheticNames` fails, and `--demo` refuses to start, naming it | `Upstream/Demo/DemoRoster.cs` |
| Different summon, buff or damage calls in the opcode processors | None here; the demo would drift from what capture does | Compare `DemoCombatFeed` with upstream's processors on each update |
| New required service in upstream's graph | `EngineRegistrationTests` fail when the engine resolves | `ScarletEngineServiceCollectionExtensions.cs` |
| New packet layout for a game patch | None here; upstream's processors change | Take the next upstream release |
| Changed game-data file names or location | `EngineRegistrationTests` fail loading data | `AddScarletEngine` or the app's output layout |
| Moved or renamed class icons in upstream's UI project | The wildcard copies nothing: no class icons in the pages; `Test-PublishLayout.ps1` (CI) fails | The `None` item in `ScarletMeter.App.csproj` |

## Engine contract

`ICombatEngine` is the whole surface: `Start`, `Stop`, `Reset`, `Capture` (running, adapter name once locked), `GetSnapshot()`, `GetPlayerDetails(playerId)`, `GetHistory(query)`, `GetHistoryFight(fightId)` and `PingUpdated`. `CombatSnapshot` is immutable: the active target (name, HP, boss flag), duration, party DPS and one `PlayerRow` per player (identity, class, totals, DPS, damage share, critical rate, deaths). Players the game has not named yet are included with `IsIdentified` false; the UI hides them until they hold over one percent of the damage, as upstream's UI does.

`Start` throws `CaptureStartException` in the two cases upstream fails on: Npcap's `wpcap.dll` cannot be loaded (the message says how to install Npcap), or upstream found no eligible adapter: Npcap listed none, or only kinds upstream skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which, so the message names both and suggests running as administrator when Npcap is restricted to administrators. An adapter that is found but fails to open is only logged by upstream, so `Start` can return with nothing listening; the header then stays at "Waiting for the game's connection...".

`Reset` maps to `CombatSessionManager.Reset` only, which runs under the session manager's lock and drops every target's session, the active target and buffered buffs. Upstream's `IPacketService.Reset` is deliberately not called: it also clears the summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture thread writes them, and upstream's own UI never calls it.

Threading: upstream's opcode processors write `EntityTracker` and `CombatSessionManager` on the capture thread. `GetSnapshot` reads the session manager under its lock, but player identities come from `EntityTracker` dictionaries that the capture thread writes outside that lock; upstream's own UI reads them the same way. A racing read can return a stale value or throw; `MeterPage` logs the first failure of a run and retries on the next tick. In demo mode the feed's thread takes the capture thread's place, with the same calls.

`EngineOptions` is host policy: an absolute `DataDirectory` (default `%LocalAppData%\ScarletMeter`, a relative path throws) holding everything the meter writes (see Storage), and `CaptureMode` (`Live`, `None` or `Demo`). `EngineSettings` are the runtime toggles upstream consults on every hit (boss-only capture, grouped summon damage, one-minute dummy mode, history retention); the bridge forwards their `Changed` event to upstream's `SettingsChanged`.

Deliberately unsupported through the bridge: upstream's raw packet logging (`IsPacketLoggingEnabled` is always false and no `FilePacketWriter` is registered) and its buff and skill-cooldown overlays (disabled, so the timed trackers track nothing). Upstream's update checker is not registered.

### Player details

`GetPlayerDetails(playerId)` returns a `PlayerDetails` for a player with damage in the active fight, or null. It is composed from upstream's per-player getters on `CombatSessionManager` (`PlayerStats`, `GetCombatDuration`, `GetPlayerSkillStats`, `GetPlayerBuffStats`, `GetPlayerGraphData`, `GetPlayerCombatLog`), each of which takes the session lock on its own, so like `CombatSnapshot` it is a composition of consecutive reads: a hit landing between two of them can be counted in one part and not yet in another. The active session is compared before and after the reads; if it changed (the fight ended, or the party switched targets), the whole read is taken again, and after three reads that all straddle a change the result is null and the next poll reads afresh. The comparison is by identity, so a switch to another target and back between the two checks goes unnoticed: against several targets at once the parts can, rarely, mix two targets. `PlayerDetailsTests` drives both cases through the engine's internal `BetweenDetailsReads` test seam.

- `Player` is the same `PlayerRow` as in a snapshot; `Rates` are the player's hit rates over direct hits (upstream ignores damage-over-time ticks there). `Duration` is the fight's (first hit by anyone to last), the window over which buff uptime is measured; the row's DPS is upstream's, over the player's own first to last hit.
- `Skills`, by damage: upstream's `SkillStats`. A skill's `Hits` includes its DoT ticks while its flag counts do not, so a ticks-only skill shows 0 for every rate. With summon grouping on, a summon's attacks are folded into the owner skill (the summon's skill whose code belongs to a real class, id above 10); `IsSummonGroup` and `SummonChildren` are set only when more than one skill was folded in, as upstream's own view shows them. `MinHit` is 0 when there is no hit.
- `Buffs`, by uptime: upstream's `BuffStats` over the fight's window, overlapping applications merged; `UptimePercent` is of `Duration`. Upstream orders them by application count; Scarlet orders by uptime.
- `Graph`: upstream's `GraphDataCalculator` output. Seconds count from the player's own first hit, one point per second, `Damage` smoothed over five seconds; buff lanes are merged spans on the same axis.
- `CombatLog`: the newest 1,000 hits (`PlayerDetails.CombatLogLimit`), newest first, with `Second` counted from the fight's start (not the graph's origin), the skill id for filtering, flags, DoT and the summon's name when a summon dealt the hit (usually upstream's placeholder `Player_<entity id>`, since the game does not name summons). Only the log is capped; skills and graph cover every hit.

### History

`GetHistory(HistoryQuery)` maps `CombatSessionManager.GetHistoryPage`: boss fights only (upstream saves nothing else), the running boss fight first with `Completed` false, then saved fights, newest first by end time, paged across both. `HistoryQuery.Validate` is the boundary: page 1 to 1,000,000 (upstream computes the rows to skip as an int, which a larger page would wrap round to the first page), page size 1 to 200, the name trimmed (blank means no filter) and at most 100 characters, `From` not after `To`; anything else throws `ArgumentException` with the rule in its message. Times are local, as upstream records them (`DateTime.Now`): UTC input is converted, unspecified input is taken as local, and returned times are marked local. The name filter is case-insensitive; the date filter matches the fight's end time, both bounds inclusive. Upstream runs the page's database queries under its session lock, which holds up hit processing meanwhile, so the history is read on demand, not on a timer.

`GetHistoryFight(fightId)` returns the whole fight: its entry, every player's row and a `PlayerDetails` per player, computed from the snapshot upstream saved (`SkillStatsByPlayer`, `BuffStatsByPlayer`, `HitsByPlayer` and `BuffEventsByPlayer` through `GraphDataCalculator.Compute`, the log from `HitsByPlayer`) by the same `UpstreamMapping` code as the live view, so a saved fight shows exactly what the live view showed (`HistoryTests` compares them). Saved fights are read from `ICombatHistoryStore.GetSession` directly rather than through `CombatSessionManager.GetHistorySession`, which would decompress and deserialise every hit under the session lock and stall the capture thread for a large fight: a performance choice, which `HistoryTests` checks by loading a saved fight while a held save keeps the session lock taken. Only a running fight goes through the session manager. Loading a large saved fight still takes a while on the calling thread, so the app should call it off the UI thread. Upstream saves a completed fight on a background task, so a fight that has just ended can be missing for a moment.

#### Damaged history

The history database is a file anything can change, and upstream reads it leniently (a missing JSON field becomes its default) or not at all (a column that does not parse fails the read). Neither history call throws for damaged data; both log what they left out.

- A saved fight is cleaned by `SavedFightSanitizer` before it is mapped. Its start and end must be set, in order, at most twelve hours apart; otherwise nothing in it can be timed, and `GetHistoryFight` returns its rows with no details. Within a usable window, it drops null entries, hits with no skill or negative damage, hits more than a minute outside the window, and buff events longer than upstream's decoder accepts (an hour) or outside the window a buff can overlap. It re-sorts hits newest first. Without this, a hit with no time overflowed `GraphDataCalculator`, two hits fifty days apart made a 4.2-million-point graph, and a buff's saved uptime of 1e300 seconds overflowed `TimeSpan`. Uptime is also clamped to the fight's duration. A player whose details still cannot be built is left out of `Details` (logged), not the whole fight.
- A row `GetHistoryFight` cannot read at all (for example text in a date column) returns null, logged.
- Upstream's list query fails on any page that holds such a row (and only on those: other pages still count it). `GetHistory` then reads the list again with `SavedHistoryReader`: every list column as text, parsed in C#, damaged rows skipped and counted, filtered, ordered and paged as upstream does. The page comes back with `IsComplete` false and without the running fight, which only upstream's query lists.
- From then on every page is read that way, so the pages agree, until a read finds no damaged row and hands back to upstream's query. If even that read fails, the page is empty, with `IsComplete` false.
- `SavedHistoryReader` reads upstream's schema (table `CombatSessions`) directly; `HistoryTests` damages rows through upstream's own `CombatHistoryDbContext`, so a schema change shows there.

Upstream's store cannot delete a single fight (only its startup retention cleanup deletes), so the engine offers no delete.

### Icons

Every `Icon` (`SkillRow`, `BuffRow`, `BuffSpan`, `CombatLogEntry`) is upstream's string, unchanged; the app decides how to serve images.

- Skills and buffs: an absolute URL on NC's game-data CDN, built by upstream's `SkillIconResolver` when `skills.json` is loaded, under `https://assets.playnccdn.com/static-aion2-gamedata/resources/`. An entry of `skill_icons.json` keyed by the code's first four digits wins (`<resources>/<name>.png`, for example `ICON_EL_SKILL_010.png` or `Item/ETC/Icon_Item_Usable_Godstone_WP_r_001.png`); otherwise `ICON_<class code>_SKILL_<NNN>.png` (`_SKILL_Passive_<NNN>` for passives) from the code's digits, or `Icon_Item_Usable_Godstone_WP_r_<hex>.png` for a theostone. It is null where upstream has none (basic attacks, unknown skills). A buff's icon is its skill's. Upstream's own UI downloads these into an `IconCache` folder next to its executable; Scarlet fetches nothing.
- Classes: not exposed. Upstream's class icon is a path into its WPF UI project, from `classes.json` (`/Assets/Classes/<name>.png`), which Scarlet does not ship; the app shows the class icons it copies at build time, by `PlayerRow.ClassId` (see "Image hosts").

### Demo mode

`CaptureMode.Demo` (`--demo`) exercises the whole engine without the game or Npcap. `AddScarletEngine` registers `DemoCaptureDevice` as upstream's `IPacketCaptureDevice`, so upstream's pipeline starts and stops it exactly as it does live capture: `Start` starts the feed's thread, `Stop` and `Dispose` end it and return only once it has ended. While it runs, `Capture` reads capturing on adapter "Demo". The feed (`DemoCombatFeed`) calls only upstream's public API, as its opcode processors do: `EntityTracker.SetSessionPlayerName` for a party of four synthetic players (Demo Gladiator, the user; Demo Cleric; Demo Ranger; Demo Elementalist), `RegisterSummon` for the Elementalist's fire spirit, `CreateOrUpdateTargetEntity` and `UpdateTargetEntityHpCurrent` for the boss and its falling HP, `CombatSessionManager.ProcessDamageEvent` for hits (critical, back, front, perfect, double and parried; DoT ticks; the spirit's hits), `ProcessBuffEvent` for party and self buffs and `FirePingUpdate` every second. Skill, buff, class and boss codes are real entries of upstream's game data, so names and icons resolve; `DemoRosterCheck` verifies them when the demo starts and in the tests.

Each boss (Aultross, Fediv Wraith and Transcendent Bakarma in turn, all bosses in `mobs.json`) gets HP for 70 to 82 seconds of the party's expected damage; the fight ends when its HP runs out, or after 90 seconds with a last blow by the user. Upstream never ends a fight on a kill: only on a 45-second idle timeout (checked when another target is hit), a respawn, or `CompleteAndPersistActiveSessions`, which is what the engine's shutdown calls. The feed ends each fight through that same public call and then flushes the history store, so each fight becomes one completed history entry; the next starts ten seconds later. A stop abandons a fight in progress (upstream completes it on its idle timeout, as after a real capture stopped), and the next start begins a fresh boss.

A clock jump (sleep and resume, a time change) does not replay or freeze the demo. When the earliest scheduled event is more than five seconds behind or ahead of the step's time, every event moves by the same amount so the earliest falls due now. Before this, one step an hour late replayed 3,600 pings and dozens of fights for over ten seconds, longer than the ten-second stop timeout. The fight in progress carries on from the new time; after a long jump upstream sees a pause in the hits, and from 45 seconds ends that fight and starts another on the same boss, as it would in the game. Each step also checks the stop request between events, so `Stop` is answered within a step.

Demo history goes to `demo-history.db`, never to the player's `combat-history.db`. The feed logs a failing event and carries on, as upstream's packet dispatch does for a failing processor, logging a repeated failure once. Anything that escapes a step is logged and retried; after ten in a row the feed stops and capture reads as off. Upstream's pipeline still counts itself running then, so `Start` does nothing; `Stop` and then `Start` begins a fresh feed. Nothing escapes the thread.

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
- `demo-history.db`: the same store for `--demo` runs, so synthetic fights never mix with real ones.
- `logs/scarlet-<date>.log`: Serilog, 14 days.
- `settings.json`, `settings.json.bak` (the previous save), and any `settings.unreadable-<UTC time>.json` kept from a damaged file (see Settings).
- `backgrounds/`: the imported background picture.
- `webview2/`: the WebView2 browser profile (cache, local storage, GPU cache; no meter data). `ScarletWindow` sets it through `BlazorWebViewInitializing`; otherwise BlazorWebView would use `%LocalAppData%\ScarletMeter.WebView2` and `--data-dir` would not isolate a run.

Nothing is written next to the executable or in the working directory. Upstream's `appsettings.user.json` is not used.

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
- `PlayerDetailsTests`: details from synthetic hits fed to upstream's session manager, every figure worked out by hand: player totals, DPS and distinct hit rates (DoT ticks excluded), fight versus player duration, skill totals, shares, DPS, max, min, average and rates (ticks counted in hits, not flags), specialisation flags, buffs by uptime over the fight (overlaps merged, clipped at the last hit, another player's buffs excluded), graph points and buff lanes from the player's first hit, the combat log's order, timing, flags, summon names and 1,000-hit cap, summon grouping (a group, a single summon skill that is not one, grouping off), icons passed through, null without damage or after a reset; a read straddling a change of target is taken again and returns the new fight, and one that keeps straddling gives up with null (through the `BetweenDetailsReads` seam).
- `HistoryTests`: synthetic boss fights completed and saved through `CompleteAndPersistActiveSessions` into the real SQLite store: listing newest first with totals, party DPS and local times; name (trimmed, any case) and date filters (end time, inclusive, UTC input); paging across a running fight and saved ones; a saved fight's players and details identical to what the live view showed (compared as JSON); a running fight opened before it ends; only boss fights saved; unknown ids; invalid queries rejected at the engine, including a page whose skip would wrap upstream's int. Damaged rows, made through upstream's own database context: hits with no time or skill, fifty days off or null, left out (the graph stays one fight long); a 1e300-second uptime clamped to the fight, buffs over an hour or at the end of time left out; an unusable start or end giving rows only; text in a date column leaving the rest of the list readable (incomplete, filtered, paged consistently, the broken fight null, back to upstream's query once the row reads again); a dropped table giving an empty, incomplete page. A saved fight loads while a held save keeps upstream's session lock taken, which a read through the session manager waits for.
- `HistoryQueryTests`: the query's boundary rules (page 1 to 1,000,000, page size, name length after trimming, range order) and normalisation (trimmed or null name, UTC and unspecified times made local).
- `DemoModeTests`: the roster resolves in upstream's game data with "Demo " names, distinct classes and one user; the demo graph runs the feed device on `demo-history.db`; a whole fight on a simulated clock (three seeds) ends in a kill within 60 to 90 seconds and one completed history entry with four players, falling boss HP, crits and back attacks, specialisation flags, a summon group, DoTs, buffs, graph lanes and resolved icons, and the next boss ten seconds later; the real feed shows rows and pings within five seconds, its thread ends on Stop (numbers stay readable), resumes on a new boss after a restart, and ends on Dispose, which saves the interrupted fight; a throwing ping subscriber is logged once while the feed carries on; a step an hour late handles at most two pings, fast, and the same boss fight carries on; an hour back does not freeze it; a step asked to stop handles nothing.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `SettingsTests`: defaults, every repair the normalizer makes (including null sections from a file), canonical hotkeys, value equality covering every property (a new property fails the test until `Equals` includes it), save/load round trip with the backup, an unreadable file kept aside with the backup restored, defaults when there is no backup, an oversized file, a newer-version file never overwritten, live application to `EngineSettings` with one event per real change, saved fight rules reaching the engine at load, hotkey parsing and browser key capture, background picture import and validation.
- `FormattingTests`: RATmeter-compatible name masking, ping bands, and that pages only get image URLs on the two local hosts.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries; demo history in its own database; `--demo` parsed and refused with `--no-capture`.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).
- `tests/ScarletMeter.Tests/WorldEvents/`: the world-events module. `EventScheduleTests` ports the fork's recurrence cases (UTC invariance across offsets, Friday-to-Sunday rollover, exact rift phase boundaries, year and week rollover, fractional offsets, simultaneous events, the Global preset's entry boundaries) and its A2Tools reproduction table, and adds weekdays at the −12 and +14 limits, an instant's offset not mattering, progress, argument checks and the `DateTimeOffset` range limits. `ScheduleValidationTests` ports the validation and `validateState` cases (each bad value against its entry, the rift's activity, missing, duplicate and unknown entries, offsets, merging keeps every editable value and takes metadata from the catalog) and checks that hostile input gets a bounded answer. `EventCatalogTests` pins both presets value for value and the rules' immutability and value equality. `AlertPlannerTests` covers the planner's boundaries (exactly at the lead time, at the start, at the entry close), resets, windows without entry, disabled rules, lead-time and offset checks, ordering, an eight-day walk proving `NextChange` exact, and no allocation without alerts. `WorldEventsBoundaryTests`: the module references only the .NET base library (no `ScarletMeter.*`, `AionDpsMeter.*`, UI or package assembly) and declares no dependency in the build graph.

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by CI after the publish) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

`scripts/Smoke-Meter.ps1` drives the built meter for UI checks: it starts it with a throwaway data directory and WebView2 remote debugging on a localhost port for that run only, finds each window's page by its `data-window` attribute, evaluates JavaScript (read text, click, set inputs), captures pages, drags the overlay with the real mouse, then closes the overlay through UI Automation and reports the exit code. See DEVELOPMENT.md.

None of these validate live capture or decoding, and neither does demo mode: it drives upstream's fight tracking with synthetic calls, never its packet decoders. That needs the game (HANDOFF.md, in-game acceptance).

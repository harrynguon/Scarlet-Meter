# Architecture

Scarlet Meter is a .NET 10 WPF application hosting a Blazor WebView. Capture and decoding of the game's traffic are not written here: they come from RATmeter (Kuroukihime/AIon2-Dps-Meter), included unmodified as the `upstream/` git submodule, pinned to release tag **v1.11.4.334** (commit 57974f8).

## Data flow

```
network adapters --SharpPcap/Npcap--> upstream CaptureDevice (finds the game's port by its heartbeat)
  -> upstream TcpStreamBuffer (TCP reassembly) -> upstream PacketProcessor (framing, LZ4)
  -> upstream PacketDispatchService -> opcode processors -> upstream EntityTracker + CombatSessionManager
  -> ScarletMeter.Engine UpstreamCombatEngine: GetSnapshot, GetPlayerDetails, GetHistory, GetHistoryFight
     (UpstreamMapping turns upstream's models into Scarlet records; saved fights come from upstream's SQLite store)
  -> ScarletMeter.App MeterPage (polls ten times a second) -> BlazorWebView in MainWindow

Demo mode (--demo): ScarletMeter.Engine DemoCaptureDevice stands in for upstream's CaptureDevice and runs
  DemoCombatFeed on its own thread -> upstream EntityTracker + CombatSessionManager, called as the opcode
  processors call them; everything downstream is the same.
```

| Source | Responsibility |
| --- | --- |
| `upstream/AionDpsMeter.Core` | Game data (mobs, skills, classes as JSON) and entity models. |
| `upstream/AionDpsMeter.Services` | Capture, reassembly, decoding, fight tracking, SQLite history, upstream settings. |
| `src/ScarletMeter.Engine/Contracts` | Scarlet's contracts: `ICombatEngine`, `CombatSnapshot`, `PlayerDetails` (and its rows), the history records (`HistoryQuery`, `HistoryPage`, `HistoryEntry`, `HistoryFight`), `EngineOptions`, `EngineSettings`. |
| `src/ScarletMeter.Engine/Upstream` | The only code that touches upstream types: `UpstreamCombatEngine`, `UpstreamMapping` (pure conversions shared by live details and history), `UpstreamSettingsBridge`, `NoCaptureDevice`. |
| `src/ScarletMeter.Engine/Upstream/Demo` | Demo mode: `DemoCaptureDevice` (thread lifecycle), `DemoCombatFeed` (the synthetic fights), `DemoRoster` (party, skills, buffs, bosses), `DemoRosterCheck` (the roster against upstream's game data). |
| `src/ScarletMeter.Engine/ScarletEngineServiceCollectionExtensions.cs` | `AddScarletEngine`: the upstream service graph, as upstream's own App.xaml.cs builds it, minus UI, updater and packet logger. |
| `src/ScarletMeter.App/App.xaml.cs` | Composition root: host, Serilog file log, engine start, window; startup failures end in a dialog. |
| `src/ScarletMeter.App/MainWindow.xaml(.cs)`, `DeferredWindowClose.cs` | The window and the close ordering that keeps Blazor from crashing the process. |
| `src/ScarletMeter.App/Pages/MeterPage.razor(.cs)`, `wwwroot/` | The meter UI. |

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
| Re-shaped `SkillStats`, `BuffStats`, `DpsDataPoint`, `BuffTimelineEntry`, `PlayerDamage`, `GraphDataCalculator`, the `HistorySession*` types or `ICombatHistoryStore` | Compile error | `Upstream/UpstreamMapping.cs`, `Upstream/UpstreamCombatEngine.cs` |
| Changed statistics (rates, DPS, uptime, graph smoothing, summon grouping) | `PlayerDetailsTests` or `HistoryTests` fail on a hand-worked figure | Decide whether Scarlet follows; update the test and the contract's doc comments |
| Game data no longer has a skill, buff, class or boss the demo uses | `DemoModeTests.RosterResolvesInUpstreamsGameDataWithSyntheticNames` fails, and `--demo` refuses to start, naming it | `Upstream/Demo/DemoRoster.cs` |
| Different summon, buff or damage calls in the opcode processors | None here; the demo would drift from what capture does | Compare `DemoCombatFeed` with upstream's processors on each update |
| New required service in upstream's graph | `EngineRegistrationTests` fail when the engine resolves | `ScarletEngineServiceCollectionExtensions.cs` |
| New packet layout for a game patch | None here; upstream's processors change | Take the next upstream release |
| Changed game-data file names or location | `EngineRegistrationTests` fail loading data | `AddScarletEngine` or the app's output layout |

## Engine contract

`ICombatEngine` is the whole surface: `Start`, `Stop`, `Reset`, `Capture` (running, adapter name once locked), `GetSnapshot()`, `GetPlayerDetails(playerId)`, `GetHistory(query)`, `GetHistoryFight(fightId)` and `PingUpdated`. `CombatSnapshot` is immutable: the active target (name, HP, boss flag), duration, party DPS and one `PlayerRow` per player (identity, class, totals, DPS, damage share, critical rate, deaths). Players the game has not named yet are included with `IsIdentified` false; the UI hides them until they hold over one percent of the damage, as upstream's UI does.

`Start` throws `CaptureStartException` in the two cases upstream fails on: Npcap's `wpcap.dll` cannot be loaded (the message says how to install Npcap), or upstream found no eligible adapter: Npcap listed none, or only kinds upstream skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which, so the message names both and suggests running as administrator when Npcap is restricted to administrators. An adapter that is found but fails to open is only logged by upstream, so `Start` can return with nothing listening; the header then stays at "Waiting for the game's connection...".

`Reset` maps to `CombatSessionManager.Reset` only, which runs under the session manager's lock and drops every target's session, the active target and buffered buffs. Upstream's `IPacketService.Reset` is deliberately not called: it also clears the summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture thread writes them, and upstream's own UI never calls it.

Threading: upstream's opcode processors write `EntityTracker` and `CombatSessionManager` on the capture thread. `GetSnapshot` reads the session manager under its lock, but player identities come from `EntityTracker` dictionaries that the capture thread writes outside that lock; upstream's own UI reads them the same way. A racing read can return a stale value or throw; `MeterPage` logs the first failure of a run and retries on the next tick. In demo mode the feed's thread takes the capture thread's place, with the same calls.

`EngineOptions` is host policy: an absolute `DataDirectory` (default `%LocalAppData%\ScarletMeter`, a relative path throws) holding everything the meter writes (see Storage), and `CaptureMode` (`Live`, `None` or `Demo`). `EngineSettings` are the runtime toggles upstream consults on every hit (boss-only capture, grouped summon damage, one-minute dummy mode, history retention); the bridge forwards their `Changed` event to upstream's `SettingsChanged`.

Deliberately unsupported through the bridge: upstream's raw packet logging (`IsPacketLoggingEnabled` is always false and no `FilePacketWriter` is registered) and its buff and skill-cooldown overlays (disabled, so the timed trackers track nothing). Upstream's update checker is not registered.

### Player details

`GetPlayerDetails(playerId)` returns a `PlayerDetails` for a player with damage in the active fight, or null. It is composed from upstream's per-player getters on `CombatSessionManager` (`PlayerStats`, `GetCombatDuration`, `GetPlayerSkillStats`, `GetPlayerBuffStats`, `GetPlayerGraphData`, `GetPlayerCombatLog`), each of which takes the session lock on its own, so like `CombatSnapshot` it is a composition of consecutive reads: a hit landing between two of them can be counted in one part and not yet in another. If the active fight itself changed between the first read and the last (it ended, or the party switched targets), the whole read is taken again, up to three times, so the parts never mix two fights.

- `Player` is the same `PlayerRow` as in a snapshot; `Rates` are the player's hit rates over direct hits (upstream ignores damage-over-time ticks there). `Duration` is the fight's (first hit by anyone to last), the window over which buff uptime is measured; the row's DPS is upstream's, over the player's own first to last hit.
- `Skills`, by damage: upstream's `SkillStats`. A skill's `Hits` includes its DoT ticks while its flag counts do not, so a ticks-only skill shows 0 for every rate. With summon grouping on, a summon's attacks are folded into the owner skill (the summon's skill whose code belongs to a real class, id above 10); `IsSummonGroup` and `SummonChildren` are set only when more than one skill was folded in, as upstream's own view shows them. `MinHit` is 0 when there is no hit.
- `Buffs`, by uptime: upstream's `BuffStats` over the fight's window, overlapping applications merged; `UptimePercent` is of `Duration`. Upstream orders them by application count; Scarlet orders by uptime.
- `Graph`: upstream's `GraphDataCalculator` output. Seconds count from the player's own first hit, one point per second, `Damage` smoothed over five seconds; buff lanes are merged spans on the same axis.
- `CombatLog`: the newest 1,000 hits (`PlayerDetails.CombatLogLimit`), newest first, with `Second` counted from the fight's start (not the graph's origin), the skill id for filtering, flags, DoT and the summon's name when a summon dealt the hit (usually upstream's placeholder `Player_<entity id>`, since the game does not name summons). Only the log is capped; skills and graph cover every hit.

### History

`GetHistory(HistoryQuery)` maps `CombatSessionManager.GetHistoryPage`: boss fights only (upstream saves nothing else), the running boss fight first with `Completed` false, then saved fights, newest first by end time, paged across both. `HistoryQuery.Validate` is the boundary: page at least 1, page size 1 to 200, the name trimmed (blank means no filter) and at most 100 characters, `From` not after `To`; anything else throws `ArgumentException` with the rule in its message. Times are local, as upstream records them (`DateTime.Now`): UTC input is converted, unspecified input is taken as local, and returned times are marked local. The name filter is case-insensitive; the date filter matches the fight's end time, both bounds inclusive. Upstream runs the page's database queries under its session lock, which holds up hit processing meanwhile, so the history is read on demand, not on a timer.

`GetHistoryFight(fightId)` returns the whole fight: its entry, every player's row and a `PlayerDetails` per player, computed from the snapshot upstream saved (`SkillStatsByPlayer`, `BuffStatsByPlayer`, `HitsByPlayer` and `BuffEventsByPlayer` through `GraphDataCalculator.Compute`, the log from `HitsByPlayer`) by the same `UpstreamMapping` code as the live view, so a saved fight shows exactly what the live view showed (`HistoryTests` compares them). Saved fights are read from `ICombatHistoryStore.GetSession` directly rather than through `CombatSessionManager.GetHistorySession`, which would decompress and deserialise every hit under the session lock and stall the capture thread for a large fight; only a running fight goes through the session manager. Loading a large saved fight still takes a while on the calling thread, so the app should call it off the UI thread. Upstream saves a completed fight on a background task, so a fight that has just ended can be missing for a moment. Saved data is read null-tolerantly: a damaged list entry is skipped rather than failing the fight.

Upstream's store cannot delete a single fight (only its startup retention cleanup deletes), so the engine offers no delete.

### Icons

Every `Icon` (`SkillRow`, `BuffRow`, `BuffSpan`, `CombatLogEntry`) and `PlayerRow.ClassIcon` is upstream's string, unchanged; the app decides how to serve images.

- Skills and buffs: an absolute URL on NC's game-data CDN, built by upstream's `SkillIconResolver` when `skills.json` is loaded, under `https://assets.playnccdn.com/static-aion2-gamedata/resources/`. An entry of `skill_icons.json` keyed by the code's first four digits wins (`<resources>/<name>.png`, for example `ICON_EL_SKILL_010.png` or `Item/ETC/Icon_Item_Usable_Godstone_WP_r_001.png`); otherwise `ICON_<class code>_SKILL_<NNN>.png` (`_SKILL_Passive_<NNN>` for passives) from the code's digits, or `Icon_Item_Usable_Godstone_WP_r_<hex>.png` for a theostone. It is null where upstream has none (basic attacks, unknown skills). A buff's icon is its skill's. Upstream's own UI downloads these into an `IconCache` folder next to its executable; Scarlet fetches nothing.
- Classes: a path into upstream's WPF UI project, from `classes.json`: `/Assets/Classes/<name>.png` (for example `/Assets/Classes/gladiator.png`). The PNGs are in `upstream/AionDpsMeter.UI/Assets/Classes/`, which Scarlet does not build or ship; upstream's Blazor view uses `/images/classes/<class id>.png` from its own `wwwroot` instead.

### Demo mode

`CaptureMode.Demo` (`--demo`) exercises the whole engine without the game or Npcap. `AddScarletEngine` registers `DemoCaptureDevice` as upstream's `IPacketCaptureDevice`, so upstream's pipeline starts and stops it exactly as it does live capture: `Start` starts the feed's thread, `Stop` and `Dispose` end it and return only once it has ended. While it runs, `Capture` reads capturing on adapter "Demo". The feed (`DemoCombatFeed`) calls only upstream's public API, as its opcode processors do: `EntityTracker.SetSessionPlayerName` for a party of four synthetic players (Demo Gladiator, the user; Demo Cleric; Demo Ranger; Demo Elementalist), `RegisterSummon` for the Elementalist's fire spirit, `CreateOrUpdateTargetEntity` and `UpdateTargetEntityHpCurrent` for the boss and its falling HP, `CombatSessionManager.ProcessDamageEvent` for hits (critical, back, front, perfect, double and parried; DoT ticks; the spirit's hits), `ProcessBuffEvent` for party and self buffs and `FirePingUpdate` every second. Skill, buff, class and boss codes are real entries of upstream's game data, so names and icons resolve; `DemoRosterCheck` verifies them when the demo starts and in the tests.

Each boss (Aultross, Fediv Wraith and Transcendent Bakarma in turn, all bosses in `mobs.json`) gets HP for 70 to 82 seconds of the party's expected damage; the fight ends when its HP runs out, or after 90 seconds with a last blow by the user. Upstream never ends a fight on a kill: only on a 45-second idle timeout (checked when another target is hit), a respawn, or `CompleteAndPersistActiveSessions`, which is what the engine's shutdown calls. The feed ends each fight through that same public call and then flushes the history store, so each fight becomes one completed history entry; the next starts ten seconds later. A stop abandons a fight in progress (upstream completes it on its idle timeout, as after a real capture stopped), and the next start begins a fresh boss.

Demo history goes to `demo-history.db`, never to the player's `combat-history.db`. The feed logs a failing event and carries on, as upstream's packet dispatch does for a failing processor, logging a repeated failure once; anything that escapes a step is logged and retried, and after ten in a row the feed stops and capture reads as off. Nothing escapes the thread.

## Window lifecycle

WPF disposes a window's WebView2 control when the window closes, but BlazorWebView is never disposed automatically, and a Blazor message sent afterwards terminates the process. `MainWindow` therefore cancels its first close, awaits `BlazorWebView.DisposeAsync` on a later dispatcher turn (components first, so `MeterPage.DisposeAsync` stops the refresh loop and unsubscribes from ping events), then closes again. `DeferredWindowClose` is ported from the fork, where the crash was reproduced and the fix is covered by unit tests and a native lifecycle check. The unit tests are ported here (`DeferredWindowCloseTests`); the native check is not yet (see HANDOFF.md).

BlazorWebView 10's `DisposeAsync` disposes the components and then the WebView2 composition control on whichever thread the component disposal completed on; the control refuses cross-thread COM access with an `InvalidCastException` (E_NOINTERFACE). `MainWindow.ReleaseBlazorContentAsync` catches that and disposes the control again on the UI thread, which succeeds; both steps are logged at Information. The fork's companion window logs the same failure, so the fallback is a candidate fix to offer it.

Shutdown order: window closes, `App.OnExit` disposes the engine (stops capture, completes open fights, flushes history), then the host.

Startup failures: any exception while creating the data directory, building the host, constructing upstream's graph (game data, history database) or showing the window ends in a "could not start" dialog and exit code 1; a failed capture start gets its own dialog. Once the host exists the failure is also logged at Critical (Error for capture). Before that (bad arguments, a data directory that cannot be created) there is no log file, and the dialog is the only report. Unhandled exceptions on the UI thread or elsewhere are logged at Critical, not handled, so the process still terminates.

## Storage

Everything the meter writes lives under `EngineOptions.DataDirectory`:

- `combat-history.db`: upstream's EF Core SQLite store of completed boss fights, retention per `EngineSettings.HistoryRetentionDays`.
- `demo-history.db`: the same store for `--demo` runs, so synthetic fights never mix with real ones.
- `logs/scarlet-<date>.log`: Serilog, 14 days.
- `webview2/`: the WebView2 browser profile (cache, local storage, GPU cache; no meter data). `MainWindow` sets it through `BlazorWebViewInitializing`; otherwise BlazorWebView would use `%LocalAppData%\ScarletMeter.WebView2` and `--data-dir` would not isolate a run.

Nothing is written next to the executable or in the working directory. Upstream's `appsettings.user.json` is not used; Scarlet's own settings persistence is still to be designed.

## Tests

- `UpstreamBoundaryTests`: the app references no upstream assembly; no Scarlet project but the engine declares an upstream project reference (and the check confirms it sees the engine's); the engine's public surface (public and protected members) exposes no upstream type, with a self-check that the surface scan sees an upstream interface.
- `EngineRegistrationTests`: the real upstream graph resolves without Npcap, for `CaptureMode.None` and the production `Live` graph (scope validation on; every opcode processor is constructed through the registry; `ValidateOnBuild` stays off because upstream also registers nested and compiler-generated types from its processor namespace that nothing resolves); a snapshot maps synthetic hits fed to upstream's session manager (identity, totals, share, critical rate, duration, party DPS); reset empties it and keeps summon ownership; the two capture-start failures get their own messages; packet logging cannot be enabled; settings and their change event reach upstream.
- `PlayerDetailsTests`: details from synthetic hits fed to upstream's session manager, every figure worked out by hand: player totals, DPS and distinct hit rates (DoT ticks excluded), fight versus player duration, skill totals, shares, DPS, max, min, average and rates (ticks counted in hits, not flags), specialisation flags, buffs by uptime over the fight (overlaps merged, clipped at the last hit, another player's buffs excluded), graph points and buff lanes from the player's first hit, the combat log's order, timing, flags, summon names and 1,000-hit cap, summon grouping (a group, a single summon skill that is not one, grouping off), icons passed through, null without damage or after a reset.
- `HistoryTests`: synthetic boss fights completed and saved through `CompleteAndPersistActiveSessions` into the real SQLite store: listing newest first with totals, party DPS and local times; name (trimmed, any case) and date filters (end time, inclusive, UTC input); paging across a running fight and saved ones; a saved fight's players and details identical to what the live view showed (compared as JSON); a running fight opened before it ends; only boss fights saved; unknown ids; invalid queries rejected at the engine.
- `HistoryQueryTests`: the query's boundary rules (page, page size, name length after trimming, range order) and normalisation (trimmed or null name, UTC and unspecified times made local).
- `DemoModeTests`: the roster resolves in upstream's game data with "Demo " names, distinct classes and one user; the demo graph runs the feed device on `demo-history.db`; a whole fight on a simulated clock (three seeds) ends in a kill within 60 to 90 seconds and one completed history entry with four players, falling boss HP, crits and back attacks, specialisation flags, a summon group, DoTs, buffs, graph lanes and resolved icons, and the next boss ten seconds later; the real feed shows rows and pings within five seconds, its thread ends on Stop (numbers stay readable), resumes on a new boss after a restart, and ends on Dispose, which saves the interrupted fight; a throwing ping subscriber is logged once while the feed carries on.
- `DeferredWindowCloseTests`: the Blazor window's close ordering, on a WPF dispatcher thread as in a window (first close cancelled and the window closed once, release never inside the `Close()` caller, a release failure is reported and still closes, a forced close skips release and close). Ported from the fork.
- `EngineOptionsTests`, `LaunchOptionsTests`: absolute-path rules at the engine and command-line boundaries; demo history in its own database; `--demo` parsed and refused with `--no-capture`.
- `SqliteRuntimeTests`: the native SQLite that actually loads carries the CVE-2025-6965 fix (the upstream overlay works).

`TestEngineHost` gives each engine test its own graph and data directory. Upstream's `CombatHistoryStore` starts an expired-session cleanup (DELETE, VACUUM, ANALYZE) fire-and-forget in its constructor; it outlives the provider and re-creates the database if the directory is deleted under it. The host waits for the store's "startup cleanup" log entry before deleting (only when the database exists, since the store creates it before starting the cleanup), and `TestDataDirectory` fails the test if the directory still cannot be deleted after five seconds, so leaked handles surface.

`scripts/Test-PublishLayout.ps1` (run by CI after the publish) checks that a single-file publish holds `wwwroot` (host page, stylesheet, `blazor.webview.js`) and `GameData\Assets\*.json` beside `ScarletMeter.exe`, with the app's assembly bundled. Upstream's `GameDataProvider` reads `GameData\Assets` and falls back to a legacy `Data\` folder, so a missing data folder is reported as `Data\classes.json`.

None of these validate live capture or decoding, and neither does demo mode: it drives upstream's fight tracking with synthetic calls, never its packet decoders. That needs the game (HANDOFF.md, in-game acceptance).

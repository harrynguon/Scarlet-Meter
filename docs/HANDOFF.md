# Handoff — 2026-10-24

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

The scaffold described in ARCHITECTURE.md exists: `upstream/` submodule at release v1.11.4.334, the engine over upstream's pipeline, a WPF window with a Blazor WebView meter page, boundary, mapping and close-ordering tests, build, upstream-update and publish-layout scripts, CI. CI has run: the bootstrap commit passed (run 39920164346), and the review fixes of 2026-10-23 (WORKLOG.md) go through a PR. Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

The world-events module (`src/ScarletMeter.WorldEvents`, ARCHITECTURE.md "World events") holds the world event schedules and the alert planner for the overlay's notices, ported from the fork's World Companion. It is not wired into the app yet, and its times are not verified in the game.

## Next: in-game acceptance

Record the tested commit, observation date and Windows configuration. Pending:

- [ ] Normal launch with Npcap finds the game's connection ("[CAPTURE] !!! LOCKED !!!" in the log) and the header leaves "Waiting for the game's connection...".
- [ ] Hits on a mob populate rows; party DPS, duration and HP bar move; ping shows.
- [ ] Reset clears the fight; a new pull starts fresh. With a summon out across the reset, its damage still lands on its owner's row, not on an unnamed player.
- [ ] Without Npcap (or with it uninstalled), a normal launch shows the "Install Npcap" dialog and exits.
- [ ] Close the window during a fight: no crash, a completed boss fight appears in `combat-history.db`.
- [ ] CPU and memory are comparable to RATmeter on the same fight.

## Next: world-event notices

The overlay that shows the notices is a separate change. Wiring notes for it:

- Call `AlertPlanner.Plan` when the overlay opens, at `AlertPlan.NextChange`, and whenever the enabled set, lead time, server offset or rules change; between calls, count down to each alert's `Deadline` locally. Pass the current instant in; the module never reads the clock.
- The app will then reference this module as well as the engine: update AGENTS.md ("Upstream boundary", which says the app references the engine only) and ARCHITECTURE.md in that change. The boundary tests forbid upstream references, not Scarlet modules, so they keep passing.
- Persisting the enabled ids, the lead time, the server offset and any rule overrides is the app's: validate with `ScheduleValidation` and merge with `EventCatalog.TryApplyOverrides` on load, and preserve a player's saved rules when the presets change.

## Next: world-event times in game

The Global preset is not verified against Global Asia / Nezekan; the fork's HANDOFF.md has the evidence so far. For each observation record the tested commit, the date, the exact server time and UTC, the local time zone, the phase seen, and whether it was a calendar countdown or an observed transition. Never record account identifiers; screenshots are not committed. Pending:

- [ ] The server clock is UTC+9 (`EventCatalog.DefaultServerOffset`).
- [ ] Spacetime Rift: two consecutive portal openings (02:00 server and every three hours, so 17:00, 20:00, 23:00, 02:00 … UTC) and one portal close, ten minutes after an opening.
- [ ] Shugo Festival (on the hour, server) and Dimensional Invasion (at half past): the start, the entry closing three minutes later, and the end after seven and thirteen minutes.
- [ ] Daily reset at 07:00 UTC (16:00 server) and the Wednesday weekly reset at the same time, each as an observed transition, and once outside Australian daylight saving.
- [ ] Start times of Watcher Kaira (01:00 server and every three hours), Guardian Lord Nahma (Sun and Fri 21:00), the Executors (Mon, Thu and Sat 21:30) and the Artifact Siege (Mon, Thu and Sat 21:00); their end times are unknown and are not shown.
- [ ] With the overlay: a Soon notice appears five minutes before each enabled event's start, an Open notice runs from a rift's, Shugo's or Invasion's start until its entry closes, and nothing shows for a reset after its instant or for a boss after its start.

Once a time is verified, correct the Global preset and its regression tests (`EventCatalogTests`, `EventScheduleTests`, `AlertPlannerTests`), record the evidence in WORKLOG.md, and keep players' saved overrides.

## After acceptance

- Settings persistence for `EngineSettings` and window bounds (Scarlet's own format, under the data directory).
- Per-player details (skills, buffs, graph): upstream's `CombatSessionManager` already computes them; extend `ICombatEngine` with Scarlet records, never upstream types.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck). The close-ordering unit tests are ported; the native check would also cover the real WebView2 close, the profile location under the data directory and a launch of the published executable, none of which CI exercises.
- Quieten the log: EF Core writes every SQL command at Information (schema creation, the startup cleanup), which buries "[CAPTURE]" lines. A Serilog override to Warning for `Microsoft.EntityFrameworkCore` in `App.xaml.cs` would do.
- Decide whether to host the fork's full World Companion page (AionDpsMeter.Companion, a Razor class library with no upstream references) in this window. Its schedule engine is already ported as `ScarletMeter.WorldEvents`.
- Chromeless, translucent overlay window; the current window keeps the standard title bar.
- Offer the fork the UI-thread WebView2 disposal fallback from `MainWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the quirks noted in WORKLOG.md (processor registration, failed start, unlocked `IPacketService.Reset`, adapter open failures swallowed, the history cleanup that outlives disposal).

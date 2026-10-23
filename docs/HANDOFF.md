# Handoff — 2026-10-24

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

What ARCHITECTURE.md describes exists: `upstream/` submodule at release v1.11.4.334, the engine over upstream's pipeline, a chromeless translucent overlay with a settings window (appearance, fight options, hotkey, remembered positions, all saved in `settings.json`), boundary, mapping, settings and close-ordering tests, build, upstream-update, publish-layout and UI smoke scripts, CI. Player details, history and world event alerts are being added in separate PRs (WORKLOG.md). Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

The world-events module (`src/ScarletMeter.WorldEvents`, ARCHITECTURE.md "World events") holds the world event schedules and the alert planner for the overlay's notices, ported from the fork's World Companion. It is not wired into the app yet, and its times are not verified in the game.

Updates (2026-10-24, ARCHITECTURE.md "Updates"): builds stamp the pinned upstream release; `UpstreamReleaseChecker` tells whether upstream has a newer one but is not wired into the UI yet; `scripts/Install-Meter.ps1` builds and installs for the current user. Upstream published v1.11.5.341 on 2026-10-23 (a live run of the checker reported it); the pin is still v1.11.4.334.

- [ ] Wire the checker into the UI: register it with `AddUpstreamReleaseChecker` in `App.xaml.cs`, show a quiet overlay notice for `NewerAvailable`, and add a settings switch that stops calling `CheckAsync`.
- [ ] Move the pin to v1.11.5.341 with `Update-Upstream.ps1`, then reinstall.
- [ ] Install once to the default location with `Install-Meter.ps1 -Shortcut` (tested so far only against a temporary directory).

## Next: in-game acceptance

Record the tested commit, observation date and Windows configuration. Pending:

- [ ] Normal launch with Npcap finds the game's connection ("[CAPTURE] !!! LOCKED !!!" in the log) and the header leaves "Waiting for the game's connection...".
- [ ] Hits on a mob populate rows; party DPS, duration and HP bar move; ping shows.
- [ ] Reset clears the fight; a new pull starts fresh. With a summon out across the reset, its damage still lands on its owner's row, not on an unnamed player.
- [ ] Without Npcap (or with it uninstalled), a normal launch shows the "Install Npcap" dialog and exits.
- [ ] Close the window during a fight: no crash, a completed boss fight appears in `combat-history.db`.
- [ ] CPU and memory are comparable to RATmeter on the same fight.
- [ ] The overlay shows over the game (borderless or windowed mode), stays on top, drags by its header and resizes from its edges; the background opacity and a background picture look right over the game.
- [ ] `Ctrl+Shift+D` (or the chosen shortcut) minimizes and restores the meter while the game has focus, and does not trigger a game action.
- [ ] Class colours, relative bars, deaths, hidden names and row size behave as in RATmeter.
- [ ] Settings and window positions survive a restart; fight options (boss-only, combined summons, one-minute dummy) change new hits.

## Next: world-event notices

The overlay that shows the notices is a separate change. Wiring notes for it:

- Call `AlertPlanner.Plan` when the overlay opens, at `AlertPlan.NextChange`, and whenever the enabled set, lead time, server offset or rules change; between calls, count down to each alert's `Deadline` locally. Pass the current instant in; the module never reads the clock.
- A timer set for `NextChange` does not notice the PC resuming from sleep or its clock being changed. Cap the wait (for example at one minute) and re-plan on resume and on a system time change, so a missed change is corrected within the cap.
- The app will then reference this module as well as the engine: update AGENTS.md ("Upstream boundary", which says the app references the engine only) and ARCHITECTURE.md in that change. The boundary tests forbid upstream references, not Scarlet modules, so they keep passing.
- Persisting the enabled ids, the lead time, the server offset and any rule overrides is the app's: validate with `ScheduleValidation` and merge with `EventCatalog.TryApplyOverrides` on load, and preserve a player's saved rules when the presets change.
- Bound the settings file's size before deserialising it: validation bounds its own work (at most 18 entries examined, 50 errors returned), but not the cost of parsing a huge file. `SettingsStore` already refuses a `settings.json` over 1 MB (`MaxFileBytes`) before reading it.

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

- Per-player details (skills, buffs, graph): upstream's `CombatSessionManager` already computes them; extend `ICombatEngine` with Scarlet records, never upstream types.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck). The close-ordering unit tests are ported; the native check would also cover the real WebView2 close, the profile location under the data directory and a launch of the published executable, none of which CI exercises.
- Decide whether to host the fork's full World Companion page (AionDpsMeter.Companion, a Razor class library with no upstream references) in this window. Its schedule engine is already ported as `ScarletMeter.WorldEvents`.
- Offer the fork the UI-thread WebView2 disposal fallback from `ScarletWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the quirks noted in WORKLOG.md (processor registration, failed start, unlocked `IPacketService.Reset`, adapter open failures swallowed, the history cleanup that outlives disposal).

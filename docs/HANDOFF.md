# Handoff — 2026-10-24

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

What ARCHITECTURE.md describes exists: `upstream/` submodule at release v1.11.5.341, the engine over upstream's pipeline, a chromeless translucent overlay with a settings window (appearance, fight options, hotkey, remembered positions, all saved in `settings.json`), boundary, mapping, settings and close-ordering tests, build, upstream-update, publish-layout and UI smoke scripts, CI. Player details and history are being added in separate PRs (WORKLOG.md). Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

World-event notices are in the overlay: `src/ScarletMeter.WorldEvents` (ARCHITECTURE.md "World events") holds the schedules and the alert planner, ported from the fork's World Companion, and the overlay shows them ("Overlay notices"), five minutes ahead by default, with an Alerts tab in settings. The times are not verified in the game.

The engine also offers per-player details (`GetPlayerDetails`), boss-fight history (`GetHistory`, `GetHistoryFight`) and a demo mode (`--demo`: synthetic fights through upstream's fight tracking, for UI work without the game); see WORKLOG.md, 2026-10-24. The app's pages for them are being built separately.
Updates (2026-10-24, ARCHITECTURE.md "Updates"): builds stamp the pinned upstream release; `UpstreamReleaseChecker` tells whether upstream has a newer one but is not wired into the UI yet; `scripts/Install-Meter.ps1` builds and installs for the current user. Upstream published v1.11.5.341 on 2026-10-23 (a live run of the checker reported it); the pin moved to it on 2026-10-24 (WORKLOG.md).

- [ ] Wire the checker into the UI: register it with `AddUpstreamReleaseChecker` in `App.xaml.cs`, show a quiet overlay notice for `NewerAvailable`, and add a settings switch that stops calling `CheckAsync`.
- [ ] Install once to the default location with `Install-Meter.ps1 -Shortcut`, which also brings in v1.11.5.341 (every install so far went to a temporary directory).

## Next: in-game acceptance

Record the tested commit, observation date and Windows configuration. Pending:

- [ ] Normal launch with Npcap finds the game's connection ("[CAPTURE] !!! LOCKED !!!" in the log) and the header leaves "Waiting for the game's connection...".
- [ ] Hits on a mob populate rows; party DPS, duration and HP bar move; ping shows.
- [ ] A boss's HP bar starts full and reaches zero at its kill (upstream v1.11.5.341 fixed the max HP of some bosses); note any boss whose bar does not.
- [ ] A summon's damage (an Elementalist's spirit, for example) lands on its owner's row, not on an unnamed player: v1.11.5.341 rewrote how a summon's owner is read.
- [ ] After a session, `logs/scarlet-*.log` holds no stream of `PacketParseException` errors from mob packets.
- [ ] Reset clears the fight; a new pull starts fresh. With a summon out across the reset, its damage still lands on its owner's row, not on an unnamed player.
- [ ] Without Npcap (or with it uninstalled), a normal launch shows the "Install Npcap" dialog and exits.
- [ ] Close the window during a fight: no crash, a completed boss fight appears in `combat-history.db`.
- [ ] CPU and memory are comparable to RATmeter on the same fight.
- [ ] Player details for a real fight match RATmeter's details window on the same fight: skill names, icons and rates, buffs and uptime, the graph, and an Elementalist's or other summon's damage grouped under its owner skill.
- [ ] A boss fight appears in history after upstream's 45-second idle timeout (or on close) and reopens with the details the live view showed; browsing history during a fight does not drop hits.
- [ ] The overlay shows over the game (borderless or windowed mode), stays on top, drags by its header and resizes from its edges; the background opacity and a background picture look right over the game.
- [ ] `Ctrl+Shift+D` (or the chosen shortcut) minimizes and restores the meter while the game has focus, and does not trigger a game action.
- [ ] Class colours, relative bars, deaths, hidden names and row size behave as in RATmeter.
- [ ] Settings and window positions survive a restart; fight options (boss-only, combined summons, one-minute dummy) change new hits.

## Next: world-event notices

Wired as ARCHITECTURE.md "Overlay notices" describes. In the game:

- [ ] A notice appears at the bottom of the overlay the chosen lead time before an enabled event, counts down, turns into "Portal open" or "Entry open" at the start and disappears when entry closes; it is readable over the game at the chosen opacity.
- [ ] With "Bring the meter back before an event" on and the meter minimized by the shortcut, an event's warning brings it back once, without taking keyboard focus from the game; with the setting off (the default) it stays hidden.

Still open: per-event time overrides in settings, for when an in-game check finds a time wrong. The module's `ScheduleValidation` and `EventCatalog.TryApplyOverrides` are the boundary for them; keep a player's saved rules when the presets change, and `SettingsStore` already caps `settings.json` at 1 MB before parsing.

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

- Icons: skill and buff icons are URLs on NC's CDN (ARCHITECTURE.md, "Icons"); class icons are served from the copies beside the executable. Decide whether the app caches skill icons under the data directory; until then they load from the CDN as in RATmeter.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck). The close-ordering unit tests are ported; the native check would also cover the real WebView2 close, the profile location under the data directory and a launch of the published executable, none of which CI exercises.
- Decide whether to host the fork's full World Companion page (AionDpsMeter.Companion, a Razor class library with no upstream references) in this window. Its schedule engine is already ported as `ScarletMeter.WorldEvents`.
- Offer the fork the UI-thread WebView2 disposal fallback from `ScarletWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the quirks noted in WORKLOG.md (processor registration, failed start, unlocked `IPacketService.Reset`, adapter open failures swallowed, the history cleanup that outlives disposal, history reads under the session lock).

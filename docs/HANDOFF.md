# Handoff — 2026-10-23

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

The scaffold described in ARCHITECTURE.md exists: `upstream/` submodule at release v1.11.4.334, the engine over upstream's pipeline, a WPF window with a Blazor WebView meter page, boundary, mapping and close-ordering tests, build, upstream-update and publish-layout scripts, CI. CI has run: the bootstrap commit passed (run 39920164346), and the review fixes of 2026-10-23 (WORKLOG.md) go through a PR. Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

The engine also offers per-player details (`GetPlayerDetails`), boss-fight history (`GetHistory`, `GetHistoryFight`) and a demo mode (`--demo`: synthetic fights through upstream's fight tracking, for UI work without the game); see WORKLOG.md, 2026-10-24. The app's pages for them are being built separately.

## Next: in-game acceptance

Record the tested commit, observation date and Windows configuration. Pending:

- [ ] Normal launch with Npcap finds the game's connection ("[CAPTURE] !!! LOCKED !!!" in the log) and the header leaves "Waiting for the game's connection...".
- [ ] Hits on a mob populate rows; party DPS, duration and HP bar move; ping shows.
- [ ] Reset clears the fight; a new pull starts fresh. With a summon out across the reset, its damage still lands on its owner's row, not on an unnamed player.
- [ ] Without Npcap (or with it uninstalled), a normal launch shows the "Install Npcap" dialog and exits.
- [ ] Close the window during a fight: no crash, a completed boss fight appears in `combat-history.db`.
- [ ] CPU and memory are comparable to RATmeter on the same fight.
- [ ] Player details for a real fight match RATmeter's details window on the same fight: skill names, icons and rates, buffs and uptime, the graph, and an Elementalist's or other summon's damage grouped under its owner skill.
- [ ] A boss fight appears in history after upstream's 45-second idle timeout (or on close) and reopens with the details the live view showed; browsing history during a fight does not drop hits.

## After acceptance

- Settings persistence for `EngineSettings` and window bounds (Scarlet's own format, under the data directory).
- Icons: skill and buff icons are URLs on NC's CDN and class icons are paths into upstream's WPF project (ARCHITECTURE.md, "Icons"); decide whether the app fetches and caches them (under the data directory), bundles them, or shows none.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck). The close-ordering unit tests are ported; the native check would also cover the real WebView2 close, the profile location under the data directory and a launch of the published executable, none of which CI exercises.
- Quieten the log: EF Core writes every SQL command at Information (schema creation, the startup cleanup), which buries "[CAPTURE]" lines. A Serilog override to Warning for `Microsoft.EntityFrameworkCore` in `App.xaml.cs` would do.
- Decide whether to host the World Companion module (AionDpsMeter.Companion in the fork, a Razor class library with no upstream references) in this window.
- Chromeless, translucent overlay window; the current window keeps the standard title bar.
- Offer the fork the UI-thread WebView2 disposal fallback from `MainWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the quirks noted in WORKLOG.md (processor registration, failed start, unlocked `IPacketService.Reset`, adapter open failures swallowed, the history cleanup that outlives disposal, history reads under the session lock).

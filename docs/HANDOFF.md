# Handoff — 2026-10-23

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

The scaffold described in ARCHITECTURE.md exists: `upstream/` submodule at release v1.11.4.334, the engine over upstream's pipeline, a WPF window with a Blazor WebView meter page, boundary and mapping tests, build and upstream-update scripts, CI. Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

## Next: in-game acceptance

Record the tested commit, observation date and Windows configuration. Pending:

- [ ] Normal launch with Npcap finds the game's connection ("[CAPTURE] !!! LOCKED !!!" in the log) and the header leaves "Waiting for the game's connection...".
- [ ] Hits on a mob populate rows; party DPS, duration and HP bar move; ping shows.
- [ ] Reset clears the fight; a new pull starts fresh.
- [ ] Close the window during a fight: no crash, a completed boss fight appears in `combat-history.db`.
- [ ] CPU and memory are comparable to RATmeter on the same fight.

## After acceptance

- Settings persistence for `EngineSettings` and window bounds (Scarlet's own format, under the data directory).
- Per-player details (skills, buffs, graph): upstream's `CombatSessionManager` already computes them; extend `ICombatEngine` with Scarlet records, never upstream types.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck) so the close ordering is guarded here too.
- Decide whether to host the World Companion module (AionDpsMeter.Companion in the fork, a Razor class library with no upstream references) in this window.
- Chromeless, translucent overlay window; the current window keeps the standard title bar.
- Offer the fork the UI-thread WebView2 disposal fallback from `MainWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the processor-registration and failed-start quirks noted in WORKLOG.md.

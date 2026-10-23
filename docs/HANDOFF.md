# Handoff — 2026-10-23

Repository: https://github.com/harrynguon/ScarletMeter (private, default branch `main`). Sibling fork of upstream with the World Companion feature: https://github.com/harrynguon/AIon2-Dps-Meter.

## State

What ARCHITECTURE.md describes exists: `upstream/` submodule at release v1.11.4.334, the engine over upstream's pipeline, a chromeless translucent overlay with a settings window (appearance, fight options, hotkey, remembered positions, all saved in `settings.json`), boundary, mapping, settings and close-ordering tests, build, upstream-update, publish-layout and UI smoke scripts, CI. Player details, history, world event alerts and the upstream release check are being added in separate PRs (WORKLOG.md). Checks that have actually run are listed in WORKLOG.md; nothing has been tried in the game.

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

## After acceptance

- Per-player details (skills, buffs, graph): upstream's `CombatSessionManager` already computes them; extend `ICombatEngine` with Scarlet records, never upstream types.
- Port the fork's native window lifecycle check (AionDpsMeter.LifecycleCheck). The close-ordering unit tests are ported; the native check would also cover the real WebView2 close, the profile location under the data directory and a launch of the published executable, none of which CI exercises.
- Decide whether to host the World Companion module (AionDpsMeter.Companion in the fork, a Razor class library with no upstream references) in this window.
- Offer the fork the UI-thread WebView2 disposal fallback from `MainWindow.ReleaseBlazorContentAsync` (its companion logs show the same off-thread failure) and offer upstream the quirks noted in WORKLOG.md (processor registration, failed start, unlocked `IPacketService.Reset`, adapter open failures swallowed, the history cleanup that outlives disposal).

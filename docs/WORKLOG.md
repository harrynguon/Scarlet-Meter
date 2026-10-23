# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-24 — Engine: player details, history, demo mode

**What:** The engine side of the per-player details and history browser, plus a demo mode for UI work. The pages are being built separately; the app gains only the `--demo` switch and its one-line mapping in `App.xaml.cs`.

- `ICombatEngine.GetPlayerDetails(playerId)` returns a `PlayerDetails` (player row, fight duration, hit rates, skills by damage with summon groups, buffs by uptime, damage graph with buff lanes, the newest 1,000 hits), composed from `CombatSessionManager`'s per-player getters. A read that straddles a change of fight is taken again, and after three such reads gives up with null (see the audit below).
- `GetHistory(HistoryQuery)` maps `GetHistoryPage`. `HistoryQuery.Validate` enforces page 1 to 1,000,000, page size 1 to 200, a trimmed name of at most 100 characters and `From` ≤ `To`, and makes times local. `GetHistoryFight(id)` returns every player's row and details, built by the same `UpstreamMapping` code as the live view. Saved fights are read from the history store directly, outside the session lock. Damaged saved data is cleaned or left out, never thrown (see the audit below).
- Upstream's store cannot delete a single fight, so there is no `DeleteHistoryFight`.
- Every skill and buff icon is upstream's string, unchanged; ARCHITECTURE.md ("Icons") says what they look like. Class icons are not exposed (see the audit below).
- `CaptureMode.Demo` and `--demo`, which excludes `--no-capture`. `DemoCaptureDevice` stands in for upstream's capture device and runs `DemoCombatFeed` on its own thread. Four "Demo …" players of different classes (the Gladiator is the user) and an Elementalist's fire spirit fight Aultross, Fediv Wraith and Transcendent Bakarma in turn. Their skill, buff, class and mob codes are real upstream game data. The feed calls only upstream's public API, as its processors do. Each fight ends in a kill, then `CompleteAndPersistActiveSessions` and a flush, and so becomes one history entry; the next starts 10 s later. Demo history goes to `demo-history.db`.
- Tests: 24 to 58 in the first commit. New: `PlayerDetailsTests`, `HistoryTests`, `HistoryQueryTests`, `DemoModeTests` and a shared `SyntheticCombat` helper; `EngineOptionsTests` and `LaunchOptionsTests` were extended. With main merged in the suite counted 192; the audit fixes add 14 (206), with a shared `CapturingLoggerProvider`.
- Docs: ARCHITECTURE (data flow, contract, player details, history, icons, demo mode, storage, tests, update table), DEVELOPMENT and README (`--demo`), HANDOFF (state, two acceptance items, icon decision).

**Why:** Harry asked for per-player details and a history browser. Upstream already computes both, so the engine maps them into Scarlet records and keeps upstream types out of the app. Demo mode lets the UI be built and tested without the game or Npcap. It drives upstream's own fight tracking rather than fake records, so the UI sees what capture would produce.

Design decisions:

- Upstream never ends a fight on a kill, only on a 45-second idle timeout, a respawn or `CompleteAndPersistActiveSessions`. The demo uses the last: it is the public call that persists, and the one the engine's shutdown already uses.
- Demo history has its own database so synthetic fights never enter the player's history.
- Saved fights bypass `GetHistorySession`, which deserialises every hit under the session lock. That would stall the capture thread while someone browses history mid-fight.
- Buffs are ordered by uptime; upstream orders them by application count.
- The combat log's `Second` counts from the fight's start. The graph keeps upstream's origin, the player's first hit. Both are documented on the records.
- `CombatLogEntry` gains `SkillId`, which the spec did not have, so the log can be filtered by skill: names repeat across skills.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug) and `scripts/Build.ps1 -Configuration Release`: restore with audit as errors, build with 0 errors and no diagnostics from Scarlet's projects (upstream's own 6 warnings unchanged), 58/58 tests passed in each configuration.
- The Release test executable, three more full runs: 58/58 each.
- `%TEMP%` held 20 `scarlet-tests-*` directories before and after every run: the auditor's leftovers of 2026-10-23, still there. These runs added none.
- Six mutations to the new code; each failed at least one test:
  - buffs ordered by applications;
  - the log cap removed;
  - back and front rates swapped;
  - unspecified database times left unmarked;
  - summon children shown without a group (caught only after a single-skill summon case was added);
  - the demo's `CompleteAndPersistActiveSessions` removed (all three seeds).
- Demo fight length: 60 simulated fights (seed 7) lasted 69.0 to 84.3 s with HP for 70 to 82 s of expected damage. The earlier 65 to 82 s window gave a 63 s fight in the first smoke run, so it was narrowed.
- First smoke run: the Debug `ScarletMeter.exe --demo --data-dir <scratch>`, closed after 100 s with `CloseMainWindow`.
  - Fight 1 against Aultross (135,477,000 HP) ended after 63 s and was saved with exactly that damage.
  - Fight 2 (Fediv Wraith) started 10 s later.
  - Exit code 0. Disposal saved the interrupted fight 2.
  - The data directory held `demo-history.db`, `logs` and `webview2`, and no `combat-history.db`.
  - The log showed the known WebView2 off-thread disposal fallback.
- Second smoke run, on the final fight-length window and otherwise the same:
  - Fight 1 against Aultross (147,442,000 HP) ended after 73 s and was saved.
  - Fight 2 started 10 s later.
  - Exit code 0. The interrupted fight was saved.
  - The data directory held only `demo-history.db`, `logs` and `webview2`.
- The smoke runs' data directories and a throwaway game-data probe lived in a session scratch folder, outside the repository, and were deleted afterwards.

**Not checked:**

- The window was not looked at. `MeterPage` shows only the snapshot, and the new pages do not exist yet.
- Live capture and the game. Demo mode does not exercise upstream's packet decoders.
- Whether real summons, buff descriptions and icons arrive as the demo models them. The demo follows upstream's processors as read in v1.11.4.334 (HANDOFF.md, acceptance).
- `GetHistory` under concurrent capture load (the database query under the session lock).

**CI:** PR 5 at 9cf4835 passed: push run 40002328567 in 2m22s and pull_request run 40002336279 in 2m16s, including the published-layout check.

**Upstream quirks, new candidates to offer upstream:**

- `GetHistoryPage` and `GetHistorySession` query, decompress and deserialise SQLite under the session lock that every hit takes.
- No fight ends on a boss kill, so a fight reaches history only 45 s after its last hit, and only once another target is hit (or the meter closes).
- `BuffStatisticsCalculator` orders buffs by application count.
- The buff decoder always leaves `Description` empty.
- `GetHistoryPage` computes its skip as `(PageNumber - 1) * PageSize` in an int, so a huge page wraps round to the first.

**Audit (deep pass, PR 5, head 903a523): request changes.** The branch was first brought up to date with main: the overlay and settings PR (#2) and the world-events PR (#3) were merged in as df7af76, with the docs conflicts resolved and 192/192 tests. The findings, and what changed:

- **Major: saved rows were not sanitised.** The auditor inserted synthetic rows and broke both history calls:
  - a hit `{}` (no time) made `GraphDataCalculator` throw `OverflowException` in `GetHistoryFight`;
  - two hits 50 days apart made a 4,233,661-point, 474 MB graph;
  - a saved uptime of 1e300 seconds made `TimeSpan` overflow;
  - text in `SessionEnd` made `GetHistory` throw, so the whole list was unusable.

  Changes:
  - `SavedFightSanitizer` cleans every saved snapshot before it is mapped. It checks the start and end are usable (set, in order, at most 12 h apart; otherwise rows only, no details). It drops null entries, hits with no skill or negative damage, hits more than a minute outside the fight, and buff events over an hour or outside the window. It re-sorts hits newest first.
  - Buff uptime is clamped to the fight; a non-finite figure counts as 0.
  - The fight total saturates instead of overflowing.
  - A player whose details still fail is left out of `Details`, logged.
  - `GetHistoryFight` logs and returns null for an unreadable row.
  - When upstream's list query fails, `GetHistory` reads the list again with `SavedHistoryReader`: every list column as text, damaged rows skipped and counted. The page comes back with the new `HistoryPage.IsComplete` false and without the running fight.
  - A probe showed upstream's query fails only on the pages that hold the damaged row, while counting it on the others. So the engine keeps reading that way until a read finds no damaged row, and every page agrees. If even that read fails, the page is empty and incomplete.
- **Huge pages (3).** `Page` is capped at `HistoryQuery.MaxPage` (1,000,000), so upstream's int skip cannot wrap round to the first page.
- **Retry overstated (4).** A read that still straddles a change of fight after three attempts now returns null instead of mixed parts. The docs (ICombatEngine, ARCHITECTURE) now say the check is by identity, so a switch away and back between the two checks goes unnoticed. An internal `BetweenDetailsReads` seam lets two tests force a straddle once (the second read wins) and every time (null).
- **Clock jumps (5).** One step an hour late took 10.4 s: 3,601 pings and 42 fights, longer than the 10 s stop timeout.
  - When the earliest event is more than `MaxClockJump` (5 s) behind or ahead of the step's time, `DemoCombatFeed` moves the whole schedule so the earliest falls due now. A jump back no longer freezes the demo either.
  - `Step` takes the stop token and checks it between events.
- **Class icons (6).** `PlayerRow.ClassIcon` is removed, with its test and docs. It exposed a path into upstream's WPF project, which Scarlet does not ship; the app shows its own copies by `ClassId`.
- **Nits:**
  - (7) The docs now say that after the feed gives up (ten failures in a row), `Start` does nothing until `Stop`.
  - (8) Comments now cite `DemoModeTests.RosterResolvesInUpstreamsGameDataWithSyntheticNames`, not a nonexistent `DemoRosterTests`.
  - (9) A new test pins the store-first lookup: a saved fight loads while a held save keeps upstream's session lock taken, which a read through the session manager waits for. The docs now call it a performance choice.

Audit checks (SDK 10.0.402):

- New tests in `HistoryTests` damage rows through upstream's own `CombatHistoryDbContext` (JSON fields, `SessionStart` and `SessionEnd`, text in a date column, a dropped table). `PlayerDetailsTests`, `HistoryQueryTests` and `DemoModeTests` gained tests for the straddle, page bound, clock jump and stop request. 206 tests pass in Debug on df7af76 plus these changes.
- Twelve mutations to the fixes, each failing at least one test (a mutant that only failed to compile was reworded until it built):
  - sanitizer: hit filter off; buff-event filter off; window check off;
  - mapping: uptime clamp off;
  - history: the list fallback off; damaged mode off; the fight read throwing; the session manager asked first;
  - details: mixed parts returned on the last attempt;
  - demo: realignment off; the stop request ignored;
  - query: the page bound off.

**Next:** CI on the PR and Harry's go-ahead; the UI pages on top of these contracts (treat a missing `Details` entry and `IsComplete` false); the icon-serving decision for skill icons (HANDOFF.md); the new in-game acceptance items.

## 2026-10-24 — World event schedules

**What:** New module `src/ScarletMeter.WorldEvents`, a net10.0 class library with no project, upstream or package references, added to the solution and referenced by the tests. It ports the schedule engine of the harrynguon/AIon2-Dps-Meter fork's World Companion (JavaScript, at 4feb72b) and adds the alert planner the overlay will use. No UI and no app wiring.

- `EventRule`, `EventKind`, `ScheduleBasis`, `EventIds`: immutable rules whose hours and days are copied, de-duplicated and sorted, and compare by value.
- `EventCatalog`: `Legacy` (the fork's `defaults`), `Global` (its Global preset, Scarlet's `Default`), `Ids`, `DefaultServerOffset` (UTC+9) and `TryApplyOverrides` (the fork's `validateState` merge).
- `ScheduleValidation`: `ValidateRules` and `ValidateServerOffset` return lists of `ScheduleError` (a catalog id or null, a constant message).
- `EventSchedule`: `Occurrence` and `Timeline`, in UTC throughout, with the current instant and the server offset as parameters.
- `AlertPlanner.Plan`: Soon within the lead time (default 5, valid 1–60 minutes), Open while a portal or entry window is open, ordered by urgency, plus `NextChange`, the next instant at which the alerts change.
- Tests: 88 new, in `tests/ScarletMeter.Tests/WorldEvents/`. `UpstreamBoundaryTests.ScarletProjectDependencies` is now internal so `WorldEventsBoundaryTests` reads the same build graph.
- Docs: ARCHITECTURE.md ("World events", the sources table, tests), SECURITY.md ("Provenance"), HANDOFF.md (wiring notes, in-game verification list), README layout.

**Why:** The player wants a subtle overlay notice about five minutes before world events such as the Spacetime Rift and the Shugo Festival. The overlay, built separately, needs a pure and tested schedule. Porting the fork's engine, rather than re-deriving it, keeps Scarlet and the fork on the same instants, so evidence gathered with either applies to both. `NextChange` lets the overlay re-plan only when something changes, not every second.

**Deliberate differences from the fork:**

- `End` and `EntryEnd` belong to the occurrence at `Start`. The fork's `end` belonged to the most recent start, which is in the past for an upcoming event; its UI never read it.
- Durations are whole minutes and the server offset is a whole number of minutes. The fork accepted any finite number; whole minutes keep the offset usable as a `DateTimeOffset` offset, and +5:30 or +5:45 still pass.
- Validation lists every problem instead of throwing at the first, and never copies input text into an error. An unknown id is reported with a null id.
- Rules normalise hours and days when constructed; the fork did so only in `validateState`.
- Not ported: the fork's file-version checks, which belong to the host's settings format, and its non-finite and string values, which the types cannot hold.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug): restore with audit as errors, build 0 warnings and 0 errors, 112 tests passed (24 existing, 88 new).
- `scripts/Build.ps1 -Configuration Release`: 0 errors, 112 tests passed. Its 6 warnings are all in `upstream/` (CS8600, CS0067 twice, CS8601, CS9113, CS0168), which `TreatWarningsAsErrors` does not reach; Scarlet's projects have none.
- Mutations: a scratch script (not committed) applied 23 single-line changes to the module, one at a time, and ran the suite. Every one failed at least one test. They covered:
  - the inclusive and exclusive boundaries (lead time, start, window end, entry end);
  - Open for resets, both halves of the Open next-change and the Soon next-change;
  - the urgency and timeline order;
  - a weekday off by one, an ignored server shift, the rift's entry default and a shortened day scan;
  - the rift activity requirement, the entry limit, and the offset range and whole-minute rule;
  - merge metadata and activity, one Global preset value, de-duplication and value equality.
  Sources were restored and rebuilt.
- Independence guard: adding a ProjectReference to the engine, and then a PackageReference (Microsoft.Extensions.Logging.Abstractions), to the module each failed `ModuleDeclaresNoDependencyInTheBuild`. With the file restored, the suite passes again.
- The fork's A2Tools reproduction table (TIMER-COMPARISON.md: Asia, UTC+9, nine cases) passes against the Global preset.
- The eight-day `NextChange` walk passes for both presets, with lead times of 1, 5 and 60 minutes.
- Planning with no alert allocated 0 bytes over 100 calls (`GC.GetAllocatedBytesForCurrentThread`).
- CI on 20029bc (PR 3): push run 40000400784 and pull_request run 40000450148 passed.

**Not checked:**

- The game: the times are not verified against Global Asia / Nezekan (HANDOFF.md, "Next: world-event times in game").
- The overlay, which is not built here.
- Instants next to `DateTimeOffset`'s limits. Correction (audit): this was not checked when the entry was first written. The test added during the audit (below) shows which limits throw.
- Regions with daylight saving: the offset is fixed, as in the fork.

**Audit (PR 3, standard pass with a deeper look at validation):** approve with nits. Applied:

- M1, merge coverage: mutating the merge to take the catalog's days or minute left every test green. `CustomScheduleValuesAreKept` now changes every editable field (basis, days added and removed, hours, minute, duration, entry minutes, and the rift's active minutes) and asserts each survives. With the test extended, both mutations fail it; they were then reverted.
- M2, bounded validation: a list of 1,000,000 entries used to return 1,000,009 errors. `ValidateRules` now returns a single "too many entries" error for more than `MaximumEntries` (18, twice the catalog), and at most `MaximumReportedErrors` (50) errors, the last saying more exist. `HostileInputGetsABoundedAnswer` covers both bounds, and removing either cap fails it. HANDOFF.md now says the host must bound the settings file before deserialising; the `overlay-settings` branch's `SettingsStore` refuses a `settings.json` over 1 MB.
- N1, range limits: the XML docs and ARCHITECTURE.md now list `ArgumentOutOfRangeException` for instants outside `DateTimeOffset`'s range. The new test `InstantsOutsideDateTimeOffsetsRangeThrow` shows when it happens:
  - a `now` within a week of `MaxValue` throws, because the next start cannot be represented;
  - both presets work at `MinValue`;
  - a custom window that opened before `MinValue` and is still running throws as well.
  This last case is slightly wider than the audit's note that `MinValue` does not throw.
- N2: `Timeline`'s id tiebreak is documented as ordinal. The fork uses `localeCompare`, which orders the catalog's lowercase ASCII ids the same way.
- N3: HANDOFF.md now says an overlay waiting for `NextChange` must cap the wait (for example at a minute) and re-plan after resume from sleep or a clock change.

Re-run after the fixes:

- `scripts/Build.ps1` Debug: 0 warnings, 0 errors, 114 tests passed (90 new).
- `scripts/Build.ps1 -Configuration Release`: 0 warnings, 0 errors, 114 passed. The build was incremental, so upstream was not recompiled and its 6 known warnings did not reappear.

**Next:** CI on the audit fixes, the player's go-ahead and a squash merge. The overlay change then wires `AlertPlanner` into the app and updates AGENTS.md, where the app references the engine only (HANDOFF.md, "Next: world-event notices"). Then settings persistence for the enabled ids, lead time, offset and overrides, and in-game verification.
## 2026-10-24 — Overlay, settings and hotkey

**What:** The first of the feature PRs Harry asked for (details, history, settings, fight options, an updater and world event alerts).

- **Window layer:** a shared `ScarletWindow` replaces the XAML `MainWindow`.
- **Overlay:** chromeless and transparent. It is dragged by its header (posted `WM_NCLBUTTONDOWN`), resized from its edges (`WindowChrome`), and has reset, settings, minimize and close buttons and a ping indicator.
- **Tool windows:** dark-framed and owned by the overlay; the settings window is the first.
- **Hotkey:** global, `Ctrl+Shift+D` by default as in RATmeter; it minimizes or restores the meter.
- **Window positions** are remembered.
- **`settings.json`:**
  - validation in `SettingsNormalizer`
  - atomic save with a `.bak` of the previous save
  - an unreadable file is kept aside, never deleted
  - a file from a newer build is never overwritten
  - live application, with fight options mirrored into `EngineSettings`
- **Appearance:**
  - background opacity (background only, so text stays solid)
  - background picture (imported into the data directory, served through a WebView2 virtual host)
  - RATmeter's class gradients and class icons (copied from upstream's UI at build time, never edited)
  - relative bars, deaths, RATmeter-compatible name masking, row size
- **Security:** a Content-Security-Policy on the page; the EF Core SQL log lowered to Warning.
- **`scripts/Smoke-Meter.ps1`:** drives the built meter's pages through WebView2 remote debugging, for UI checks without the game.
- **Docs:** ARCHITECTURE "Windows", "Settings", "Image hosts".

**Why:** Harry asked for RATmeter's settings screen, fight switches and window handling in Scarlet, with heavy inspiration from RATmeter. An Explore survey of upstream's UI supplied its exact behaviour: gradients, masking, hotkey default and toggle semantics, ping bands. Two deliberate differences from RATmeter:

- Opacity fades only the background. RATmeter fades the whole content.
- The background picture is actually displayed. RATmeter's setting is stored but never rendered.

The overlay is the base the alerts and other windows build on.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 0 warnings in Scarlet's projects (6 in upstream's, as before), 61 tests passed (37 new settings and formatting tests).
- Smoke runs through `Smoke-Meter.ps1` on the Debug build:
  - The overlay rendered "Capture is off"; clicking ⚙ opened the settings window with its four tabs.
  - A real mouse drag on the header moved the overlay by exactly (120, 60) and (−150, 40).
  - Clicking the Class colours row and moving the opacity slider to 40%:
    - the overlay's `--panel-alpha` became 0.4 live
    - `settings.json` held `classColors: false` and `backgroundOpacity: 0.4` within a second
  - A second run on the same data directory restored the overlay at the saved position and size (1370, 606, 400×260) and opacity 0.4.
  - Real `Ctrl+Shift+D` key presses minimized and then restored the overlay (UI Automation window state).
  - Closing through the page's ✕ with the settings window open:
    - exit code 0
    - no warning or error in the log
    - the UI-thread WebView2 disposal fallback logged
  - A screen capture at 50% opacity showed the desktop through the panel (not committed).
- Release self-contained single-file publish:
  - `Test-PublishLayout.ps1` passed; class icons are now checked.
  - In the published exe, `https://assets.scarlet.example/classes/11.png` and a background picture placed in `<data dir>\backgrounds` loaded (40 px wide).
  - `--panel-image` pointed at the data host.
  - An image from `https://example.com` was blocked by the policy.

**Not checked:**

- The file picker dialog itself (`BackgroundImages.Import` is unit-tested; the dialog was not driven).
- The game: the overlay over a borderless game window, the hotkey while the game has focus, and DPI scaling or multiple monitors.
- Rows with real players (the demo mode comes with the engine PR).

**Audit (deep pass, PR 2, head 3acbde5):** request changes, with two major defects reproduced in the built meter. Both are fixed:

- **Recording a shortcut did not unregister the current one.** `SuspendHotkey` reset its "registered" marker to the same empty string it then wanted, so nothing changed; pressing Ctrl+Shift+D while recording minimized the meter.
  - Fix: the decisions moved into `HotkeyBinding` (a null marker forces the first registration), with `HotkeyBindingTests` on a fake target. Recording also ends after 15 seconds as a backstop for a lost blur.
- **Closing the overlay while the picture dialog was open killed the process** (0xE0434352, `ObjectDisposedException` inside the dispatcher's handler). This is the crash the deferred close exists to prevent.
  - Fix: while the picker is open every meter window is disabled (`EnableWindow`) and refuses to close (`ScarletWindow.CloseBlocked`), and the hotkey and minimize do nothing. The settings page ignores a picker result after it has been disposed.

The minor findings and nits, all applied:

- **Settings store:** a damaged file or backup that cannot be renamed aside, or a backup that cannot be opened, now switches saving off. Without that, two saves destroyed the damaged file. A missing file with a usable backup restores it. Unreadable files keep unique names.
- **Picture names:** the stored-name regex uses `\z`, so a trailing newline is rejected.
- **CSP:** tightened to `script-src 'self'`, `object-src 'none'`, `base-uri 'self'`, without `data:`.
- **Boundary docs:** ARCHITECTURE "Upstream boundary" names the class-icon copy as the one build-time contact with upstream's UI files; the "What breaks" table has a row for it.
- **Closing guard:** `MeterWindows` refuses to open windows once the overlay is closing.
- **Placement:** tool windows use the overlay's monitor's work area.
- **Comments:** fixed for hidden names and for tool-window placement.
- **Hotkeys:** shortcuts Windows or most programs use (Alt+F4, Ctrl+C and similar) are refused.
- **Settings page:**
  - save errors re-render it at once (`SaveStatusChanged`)
  - the retention field shows the clamped value
  - in read-only mode the picture is neither imported nor deleted
- **Smoke script:** picks a free DevTools port and drives only pages on BlazorWebView's origin, `https://0.0.0.1/`. The earlier fixed port could drive another running meter.
- **Masking test:** checks upstream's exact masked value ("Ab**3**").

**Checks after the fixes:**

- `Build.ps1`, Debug and Release: 68/68 tests (7 new).
- The auditor's hotkey reproduction (`keybd_event` while the field records): the overlay stays Normal.
- A full real-mouse and real-key flow:
  - opened settings, the Overlay tab and the shortcut field with real clicks
  - pressing the current Ctrl+Shift+D was recorded, and the overlay stayed Normal
  - recording Ctrl+Alt+K saved it
  - Ctrl+Alt+K then minimized and restored the overlay
  - the old Ctrl+Shift+D did nothing
- The auditor's dialog reproduction: with the picture dialog open, clicking ✕ did not close the meter (it was still running after 3 s). After the dialog was cancelled, ✕ closed it with exit code 0 and only the usual WebView2 disposal lines in the log.
- Under the tightened CSP, a class icon loaded from the assets host and an image from another host was blocked.
- No `%TEMP%\scarlet-*` leftovers.

**Next:** CI on the fixes and a squash merge, then the engine, events and update-check PRs, then the details, history, alerts and update-notice UI on top of this.

## 2026-10-23 — Review fixes

**What:** Applied the bootstrap review: 15 findings, 8 confirmed by independent verifiers and 7 more judged real when verification was cut short by a usage limit.

- Engine: `Reset` calls `CombatSessionManager.Reset` only. `Start` tells "no eligible adapter" apart from "Npcap missing". The contract docs say adapter open failures are only logged by upstream.
- App: the WebView2 profile lives in `<data dir>\webview2`. Every startup failure ends in a dialog and exit code 1, and is logged once the host exists. Unhandled exceptions are logged at Critical. `MeterPage`'s refresh loop survives a failed tick, logging the first failure of a run.
- Scripts and CI: `scripts/Test-PublishLayout.ps1` checks a publish, and CI runs it after the publish step. `Update-Upstream.ps1` passes `--quiet` to `git fetch` and `git checkout`.
- Tests, 14 to 24: the fork's six `DeferredWindowCloseTests`; the `Live` graph resolves without starting; reset keeps summon ownership; both capture-start messages. The boundary guard covers every Scarlet project in the dependency graph, plus base types, interfaces and generic constraints, with self-checks. `TestEngineHost` waits for upstream's history cleanup; `TestDataDirectory` retries deletion, then fails.
- Docs and licence: `LICENSE` ends with Scarlet Meter's own notice naming upstream's; SECURITY.md attributes the SQLite override to Scarlet's overlay; README and DEVELOPMENT commands use absolute `<checkout>` paths; ARCHITECTURE covers storage, threading, startup failures and the tests.

**Why:** Upstream's `IPacketService.Reset`, which upstream's own UI never calls, cleared the summon-to-owner map and the TCP reassembly buffer from the UI thread while the capture thread wrote them. After a reset, a pet's hits landed on an unnamed player, and frames were dropped until the stream re-synchronised. The bootstrap smoke run's WebView2 profile had gone to `%LocalAppData%\ScarletMeter.WebView2`, so `--data-dir` did not isolate a run. A startup exception outside the two guarded calls crashed with no dialog and no log. The CI publish step proved only that publish exited 0. The remaining findings were documentation and test gaps.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug): restore with audit as errors, build 0 warnings and 0 errors, 24 tests passed.
- With the old `Reset` restored, `ResetKeepsSummonOwnership` failed at the owner assertion (it got the pet's id). The fix was then restored.
- Temp-directory leaks: before `TestEngineHost`, whole-class and whole-suite runs left `scarlet-tests-*` directories behind (21 in 10 runs), each holding a 0-byte or 8 KB `combat-history.db` written after deletion. No test leaked when run alone. Waiting for the store's cleanup log entry removed them: 0 leaked in 10 full runs, all passing. This confirms the history store's fire-and-forget cleanup as the cause.
- Release, self-contained single-file publish to a scratch directory: exit 0. `Test-PublishLayout.ps1` passed; only `.pdb`, `.xml` and `ScarletMeter.staticwebassets.endpoints.json` sit beside the exe. The script rejects a relative path.
- Published exe with `--no-capture --data-dir <scratch>`:
  - Rendered "Capture is off", 00:00, 0/s, -- ms and Reset (screenshot, not committed).
  - Closed through the window's close (UI Automation), exit code 0.
  - The data directory held `combat-history.db`, `logs/` and `webview2/`. Neither `%LocalAppData%\ScarletMeter.WebView2` nor `%LocalAppData%\ScarletMeter` was created.
  - The log showed the UI-thread disposal fallback succeeding.
- Failure dialogs, each exit code 1:
  - `--data-dir` under an existing file: "Cannot create ...". No log, by design.
  - A publish copy with `GameData` renamed: "Classes data file not found: ...\Data\classes.json", and `[FTL] Startup failed.` in the log.
  - `--capture`: "Unknown argument".
- CI: the bootstrap commit 1de352f passed (run 39920164346, 1m57s).
- Cleanup: removed 31 `scarlet-tests-*` temp directories (7 from the bootstrap, the rest from diagnosing the leak) and the bootstrap smoke's `%LocalAppData%\ScarletMeter.WebView2`.

**Not checked:**

- `Update-Upstream.ps1`: not run, since there is no newer release; `--quiet` is untested in ISE or VS Code.
- The unhandled-exception handlers and `MeterPage`'s refresh-failure path were not triggered (no fault injection).
- Live capture and the game.
- CI on this branch (pending the PR).

**Upstream quirks, new candidates to offer upstream:**

- `IPacketService.Reset` clears `EntityTracker` summons and `TcpStreamBuffer` without a lock.
- `CaptureDevice.StartCapture` logs adapter open failures and then reports capturing with no adapter open.
- `CombatHistoryStore` starts its cleanup fire-and-forget in its constructor, and the task can re-create the database after its host is disposed.
- `GameDataProvider` falls back silently from `GameData\Assets` to `Data\`, so a missing data folder is reported as `Data\classes.json`.

**Audit (deep pass, PR 1, head c73b555):** approve with nits. CI passed on that head (pull_request run 39953445631, push run 39953403158; "Verify published layout" passed, 24/24 tests). The auditor ran mutations; the guards caught an upstream ProjectReference in the app, a public `NoCaptureDevice`, the old `Reset`, the removed adapter catch and a broken cleanup-log match. Its nits, all applied:

- The adapter message no longer points to log details upstream never writes, and now covers Npcap listing no adapters (for example, Npcap restricted to administrators).
- `MainWindow.OnClosing` tolerates a constructor that threw after WPF registered the window, so that failure ends in the dialog rather than a crash (traced, not run).
- `TestEngineHost` waits only when the history database exists and reports a timeout together with any disposal failure. `TestDataDirectory` deletes even when `ClearAllPools` throws.
- The surface guard also scans protected members. A probe type with a protected upstream-typed property failed it, then was removed.
- `MeterPage` logs and retries a cancellation unrelated to disposal.
- Wording fixes in `MeterPage`'s summary and DEVELOPMENT.md.

The auditor's own failed runs (a long clone path stopped `e_sqlite3.dll` loading) left 20 empty `scarlet-tests-*` directories in the user temp directory; they are left for Harry to remove. After the fixes: `scripts/Build.ps1` Debug and Release, 24/24.

**Next:** CI on the fixes, Harry's go-ahead and a squash merge; then in-game acceptance (HANDOFF.md).

## 2026-10-23 — Bootstrap

**What:** New private repository for a meter with its own UI on upstream RATmeter's capture and decoding. Upstream is a git submodule pinned to release v1.11.4.334 (the same commit the harrynguon/AIon2-Dps-Meter fork tracks). `ScarletMeter.Engine` wraps upstream behind `ICombatEngine`/`CombatSnapshot`; `ScarletMeter.App` is a WPF window hosting a Blazor WebView page that polls the engine. Tests guard the boundary and the mapping. `scripts/Build.ps1`, `scripts/Update-Upstream.ps1` and CI are in place. The root `Directory.Build.props` overlays upstream's Services project with the fixed SQLite bundle (ARCHITECTURE.md, "Upstream overlay"), because the pristine release pulls the vulnerable native package and the audit-as-error restore rejects it; that override is a fork-only edit upstream never took.

**Why:** Keep upstream updates a pin change, keep Scarlet's code free of upstream internals, and give the UI full freedom. Submodule rather than subtree so upstream code is never edited and its provenance stays explicit; rather than a copy so it keeps receiving upstream's parser fixes.

**Checks (SDK 10.0.402 through `scripts/Build.ps1` with `SCARLET_DOTNET`):** restore with NuGet audit as errors passed once the overlay was in place; build 0 warnings, 0 errors with `TreatWarningsAsErrors`; `dotnet test --solution` 14 passed (3 boundary, 5 engine registration and mapping, 2 options, 3 launch switches, 1 SQLite runtime). Smoke: the built `ScarletMeter.exe --no-capture --data-dir <temp>` rendered the waiting state (header, 00:00, 0/s, -- ms, Reset), closed through the window's close with exit code 0, and left `combat-history.db` and `logs/scarlet-<date>.log` in the temp directory (correction, see the review fixes above: it also created the WebView2 profile `%LocalAppData%\ScarletMeter.WebView2`). The log showed BlazorWebView's off-thread WebView2 disposal failing (`InvalidCastException`) and the UI-thread fallback in `MainWindow` succeeding. The screenshot is not committed.

**Not checked:** live capture with Npcap and the game (HANDOFF.md); CI (first run after the push); the Release single-file publish (CI's smoke step); DPI scaling and multiple monitors.

**Upstream quirks found, candidates to offer upstream or the fork:** `AddPacketProcessingRouting` registers every type in its processor namespace, including nested records and compiler-generated iterators, so `ValidateOnBuild` fails (the tests leave it off). `PacketPipelineService.Start` sets `isRunning` before discovering adapters, so a failed start cannot be retried in-process (the app exits instead). BlazorWebView 10 disposes the WebView2 composition control off the UI thread; the fork's companion logs show the same warning, and `MainWindow.ReleaseBlazorContentAsync` holds the fix.

**Next:** push, watch the first CI run, then in-game acceptance.

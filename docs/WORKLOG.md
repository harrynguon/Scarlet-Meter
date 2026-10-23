# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-24 — Engine: player details, history, demo mode

**What:** The engine side of the per-player details and history browser, plus a demo mode for UI work. The pages are being built separately; the app gains only the `--demo` switch and its one-line mapping in `App.xaml.cs`.

- `ICombatEngine.GetPlayerDetails(playerId)` returns a `PlayerDetails` (player row, fight duration, hit rates, skills by damage with summon groups, buffs by uptime, damage graph with buff lanes, the newest 1,000 hits), composed from `CombatSessionManager`'s per-player getters. A read that straddles a change of fight is retried up to three times, so the parts never mix two fights.
- `GetHistory(HistoryQuery)` maps `GetHistoryPage`. `HistoryQuery.Validate` enforces page ≥ 1, page size 1 to 200, a trimmed name of at most 100 characters and `From` ≤ `To`, and makes times local. `GetHistoryFight(id)` returns every player's row and details, built by the same `UpstreamMapping` code as the live view. Saved fights are read from the history store directly, outside the session lock.
- Upstream's store cannot delete a single fight, so there is no `DeleteHistoryFight`.
- `PlayerRow` gains an optional `ClassIcon`. Every icon is upstream's string, unchanged; ARCHITECTURE.md ("Icons") says what they look like.
- `CaptureMode.Demo` and `--demo`, which excludes `--no-capture`. `DemoCaptureDevice` stands in for upstream's capture device and runs `DemoCombatFeed` on its own thread. Four "Demo …" players of different classes (the Gladiator is the user) and an Elementalist's fire spirit fight Aultross, Fediv Wraith and Transcendent Bakarma in turn. Their skill, buff, class and mob codes are real upstream game data. The feed calls only upstream's public API, as its processors do. Each fight ends in a kill, then `CompleteAndPersistActiveSessions` and a flush, and so becomes one history entry; the next starts 10 s later. Demo history goes to `demo-history.db`.
- Tests: 24 to 58. New: `PlayerDetailsTests`, `HistoryTests`, `HistoryQueryTests`, `DemoModeTests` and a shared `SyntheticCombat` helper; `EngineOptionsTests` and `LaunchOptionsTests` were extended.
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
- No independent audit has been run on this PR.

**CI:** PR 5 at 9cf4835 passed: push run 40002328567 in 2m22s and pull_request run 40002336279 in 2m16s, including the published-layout check.

**Upstream quirks, new candidates to offer upstream:**

- `GetHistoryPage` and `GetHistorySession` query, decompress and deserialise SQLite under the session lock that every hit takes.
- No fight ends on a boss kill, so a fight reaches history only 45 s after its last hit, and only once another target is hit (or the meter closes).
- `BuffStatisticsCalculator` orders buffs by application count.
- The buff decoder always leaves `Description` empty.

**Next:** CI on the PR and Harry's go-ahead; the UI pages on top of these contracts; the icon-serving decision (HANDOFF.md); the new in-game acceptance items.

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

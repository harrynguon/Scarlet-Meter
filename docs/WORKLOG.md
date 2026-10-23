# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

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

**Not checked:**

- The game: the times are not verified against Global Asia / Nezekan (HANDOFF.md, "Next: world-event times in game").
- The overlay, which is not built here.
- CI on this branch (pending the PR).
- Instants within a week of `DateTimeOffset`'s limits, which throw.
- Regions with daylight saving: the offset is fixed, as in the fork.

**Next:** review, CI, the player's go-ahead and a squash merge. The overlay change then wires `AlertPlanner` into the app and updates AGENTS.md, where the app references the engine only (HANDOFF.md, "Next: world-event notices"). Then settings persistence for the enabled ids, lead time, offset and overrides, and in-game verification.

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

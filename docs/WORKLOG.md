# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-24 — Upstream release check and installer

**What:**

- Build stamp: `src/ScarletMeter.App/Updates/BuildStamp.targets`, imported by the app's project only, stamps `AssemblyMetadata("ScarletUpstreamRelease")` (the submodule's exact tag, else its short commit, else `unknown`) and `AssemblyMetadata("ScarletCommit")`. `BuildInfo` reads them.
- `UpstreamReleaseChecker` (`ScarletMeter.App.Updates`): one anonymous GET to GitHub's latest-release API for upstream. It validates the answer, compares versions numerically, caches the result, requests at most once per six hours and never throws. `AddUpstreamReleaseChecker` registers it; it is deliberately not registered in `App.xaml.cs`, because the UI rewrite in progress wires it into the overlay and settings.
- `scripts/Install-Meter.ps1`: Release build with tests, self-contained single-file publish into `<install>.staging`, layout check, then a reversible folder swap that keeps `<install>.previous`. `-Shortcut` adds a Start menu shortcut.
- CI fetches the pinned upstream release tag, which its shallow submodule lacks, and fails if the pin is not a release. It now runs `Install-Meter.ps1 -SkipTests` under pwsh in place of the bare publish and layout steps.
- Tests, 24 to 108 (84 new cases): `BuildInfoTests`, `UpstreamReleaseCheckerTests`. The test project gains Microsoft.Extensions.TimeProvider.Testing 10.10.0.
- Docs: ARCHITECTURE "Updates", DEVELOPMENT "Install and update" and CI, README, SECURITY "Network", HANDOFF.

**Why:** Harry asked for an automatic updater, if possible and reasonable, for a meter only he uses. A RATmeter-style self-updater (download a release, replace the exe) does not fit: Scarlet has no published binaries, and swapping one in would skip the build and its checks. What matters is knowing when upstream releases, since game-patch parser fixes arrive that way, plus one command to rebuild and reinstall.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set, Windows PowerShell 5.1):**

- `scripts/Build.ps1` Debug and Release: 0 warnings, 0 errors, 108/108 passed.
- Mutations, each caught by the new tests, then reverted:
  - no cache: 4 failures;
  - `$` instead of `\z` in the tag pattern: 2;
  - no page-path check: 1;
  - text instead of numeric comparison: 3;
  - no timeout: 2 timeout tests failed by their wait limit;
  - no read cap: 1.
- Stamp:
  - Generated assembly info carried `v1.11.4.334` and `9495922`, and both values are in the bundled exe.
  - A temporary local commit, undone afterwards, was restamped by an incremental build, and the reset stamped the old value back.
  - With the pinned tag deleted from this worktree's submodule clone, the stamp fell back to `57974f8`. The tag was then re-created at the same commit.
  - With git removed from PATH: `unknown` for both, build exit 0.
- CI checkout simulated in the scratch directory (the same fetch, then `git submodule update --init --force --depth=1`):
  - The submodule held only `v1.11.5.341`, the tag at upstream master's tip. `describe --exact-match` failed, so CI would have stamped the commit.
  - The CI step's script, run locally, fetched `v1.11.4.334` at depth 1; after that `describe` matched and the env file held the tag.
- One manual live check, outside the tests, through `AddUpstreamReleaseChecker` and the production handler: `NewerAvailable`, v1.11.5.341, published 2026-10-23T17:19:58Z, page on upstream's repository. The second call was served from the cache.
- `Install-Meter.ps1` against a temporary install directory in the scratch directory, never the default location:
  - Fresh install with `-Shortcut` and tests: layout passed, the shortcut pointed at the installed exe.
  - The installed exe started with `--no-capture --data-dir <temp>` and showed its window.
  - While it ran, the script refused before building. It then closed with exit 0.
  - A second install moved the first to `.previous`.
  - With a file held open in the install, the rename failed and was undone: install and `.previous` unchanged, the new build left in staging, no `.retired`.
  - A rerun replaced `.previous` and cleared staging.
  - With a wrapper failing only `dotnet publish`, staging was removed and the install left untouched.
  - Refusals before any build: relative path, drive root, the data directory, a folder inside it, its parent, a folder in the checkout, a foreign folder, a file, a foreign `.previous`. The foreign files were kept.
  - `%LocalAppData%\ScarletMeter` and `%LocalAppData%\Programs\ScarletMeter` do not exist afterwards.
  - Cleanup: the shortcut, the temporary install and the scratch projects were removed.

**Not checked:**

- pwsh: it is not installed here. CI runs `Install-Meter.ps1` and the tag step under pwsh.
- CI itself: pending the PR.
- An elevated meter: refused by design as "location unreadable", but not run.
- The UI wiring: notice, settings switch and registration are left to the UI rewrite.
- The default install location: never installed there.

**Next:** CI on the PR, Harry's go-ahead and a squash merge. Then wire the checker into the UI and move the pin to v1.11.5.341 (HANDOFF.md).

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

# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

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

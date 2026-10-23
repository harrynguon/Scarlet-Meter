# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-24 — Update notice in the overlay

**What:** The upstream release check reaches the player (ARCHITECTURE.md "Updates", "Notice").

- `UpdateNotice` asks `UpstreamReleaseChecker` 30 s after start-up and whenever its cached answer expires.
- A newer release shows one quiet line above the alert strip ("RATmeter v… is out", "Release notes", a cross that hides it until the next release).
- Settings, About: a switch (on by default), the release the build is on, the last result, and the update commands when a release is out.
- `UpdateSettings` (`settings.json` `updates`): the switch and the dismissed release, validated as a release tag.
- `MeterWindows.OpenReleasePage` opens only an https page under upstream's releases on github.com.

**Why:** Harry asked for an automatic updater "if it's even possible, and reasonable"; the meter is for his own use and has no published binaries, so the reasonable form is a notice plus a one-command reinstall (PR #4). This is the notice.

**Checks (SDK 10.0.402, on the update-check branch at fb24e0a):**

- `scripts/Build.ps1`: 284/284, 16 of them new (`UpdateNoticeTests`), and `SettingsTests.EqualityCoversEveryProperty` extended for the new section.
- Smoke run (`Smoke-Meter.ps1`, `--no-capture`, throwaway data directory). This branch was still pinned to v1.11.4.334, so the real check, one GET to api.github.com, found v1.11.5.341:
  - no line at start; about 30 s later "RATmeter v1.11.5.341 is out | Release notes", its tooltip naming v1.11.4.334;
  - Settings, About read "v1.11.5.341 is out (published 24 Oct); this build is on v1.11.4.334 (checked 06:24)" and gave `scripts\Update-Upstream.ps1 -Tag v1.11.5.341`, then `scripts\Install-Meter.ps1`;
  - the cross removed the line and saved `"dismissedRelease": "v1.11.5.341"`; the checker logged its result once; exit code 0.

**Not checked:** "Release notes" opening the browser (not clicked in the smoke run, so no browser was launched); the line over the game.

**Next:** merge with PR #4's final head and main, PR, audit, CI and merge.

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

**Audit (deep pass, PR 4): request changes.** Merged origin/main first (#2 overlay and settings, #3 world events), keeping both sides in README, ARCHITECTURE, WORKLOG and the app project. CI keeps its steps plus the tag step, and the layout check still runs on the published output inside `Install-Meter.ps1`. Then:

- Major: `Install-Meter.ps1` accepted any folder holding a `ScarletMeter.exe`. The auditor's Downloads-like folder (a dummy exe, `important.txt` and a junction) was renamed to `.previous` by one install and deleted by the next. Fixed:
  - A checked publish now gets an install marker, `.scarlet-install` with fixed text, which stays in the install.
  - The install, `.previous` and `.retired` are renamed or deleted only when they carry that marker or are empty. Staging is accepted with the staging marker or the install marker.
  - Any folder that is or holds a link or junction is refused; the tree is walked without following links.
  - New `-CheckOnly` runs every check and stops before building.
  - New `scripts/Test-InstallMeter.ps1` runs the refusals against throwaway folders, and CI runs it. It checks the auditor's folder stays byte for byte as it was.
  - The "a mistyped path cannot move other files" claims are corrected.
- The overlap guards compared strings. Now:
  - Only local drive paths are accepted (no UNC, admin shares, `\\?\` or `\\.\`), and no names Windows would change (trailing dot or space) or cannot create.
  - The parent of the install, the data directory and the checkout are compared by their final paths from `GetFinalPathNameByHandle`, which resolves links, junctions, 8.3 names and substituted drives. A path resolving to a network share is refused.
  - The process check resolves the running exe's folder the same way.
- Rollback: each undo rename has its own try/catch; failures are collected, and the error reports every folder's state. A leftover `.retired` is deleted only while an install exists, otherwise refused up front, since it may be the only copy.
- Checker: Network and Timeout results stand for `RetryInterval` (10 minutes); answered failures (rate limit, 404, invalid) stay six hours. Disposing mid-check, or calling after disposal, gives `Unknown(Cancelled)` with no warning.
- Nits:
  - Tests for an answer of exactly 1 MiB (read) and 1 MiB + 1 (rejected), with and without a length.
  - A gzip answer inflated by the production handler, plus a gzip answer over the cap once inflated, both through a one-shot server on 127.0.0.1.
  - Dispose mid-check.
  - CI retries the tag fetch like `ls-remote` and no longer sleeps after the last attempt.
  - `Get-GitLine` requires the `.git` entry, as BuildStamp.targets does.

Checks after the fixes (SDK 10.0.402, Windows PowerShell 5.1):

- `scripts/Build.ps1` Debug and Release: Scarlet's projects had 0 warnings (the six are upstream's), and 252/252 tests passed.
- `Test-InstallMeter.ps1`: every case passed. The short-name case was skipped because this volume has no 8.3 names. No temp folder was left.
- Five installer mutations, each failing the named cases:
  - any folder counted as owned;
  - no final-path resolution;
  - no link walk;
  - UNC allowed (the admin share was still refused, by the final-path layer);
  - no `.retired` guard.
- Five checker mutations, each caught:
  - `>=` in the read cap;
  - `>=` in the length cap;
  - no retry window;
  - the retry for every failure;
  - dispose reported as Unexpected.
  - (A first form of the dispose mutation, `when (false)`, did not compile under warnings-as-errors and was replaced.)
- Install-Meter end to end, in a uniquely named scratch folder:
  - Two installs left the install and `.previous`, both marked, with no staging or `.retired` left.
  - With a file held open, the swap rolled back and the error listed the folders: install and `.previous` intact, the checked build left in staging (marked).
  - The running installed meter was refused under `-CheckOnly`; it then closed with exit 0.
  - A rerun recovered.
  - The folder was removed afterwards. `%LocalAppData%\ScarletMeter` and `%LocalAppData%\Programs\ScarletMeter` do not exist.

Not checked:

- A failing undo (no way to inject one).
- pwsh locally; CI runs both scripts under pwsh.

A local dry-run of CI's tag step made this worktree's own submodule clone shallow; `git fetch --unshallow --tags` restored it (334 commits, 38 tags, still v1.11.4.334), and the CI comment now warns about it.
**Second merge of main (45ecab3, #6 event notices):** conflicts in README, HANDOFF and WORKLOG were resolved keeping both sides. Main's `App.xaml.cs` registers `TimeProvider.System` itself; `AddUpstreamReleaseChecker` only try-adds it, so the two agree. Checks: `Build.ps1` Debug and Release 268/268, `Test-InstallMeter.ps1` passed, and one `-SkipTests` install of the merged tree into a scratch folder passed the layout check (now including `alerts.css`) and carried the marker; the folder was removed.

**Second audit (deep pass, head fb24e0a): approve with nits.** Merged origin/main first (40206be, #7: upstream v1.11.5.341, aa3ffc6). HANDOFF and WORKLOG were resolved by keeping both sides. `git add -A` had restaged the old submodule commit over the merge, so main's gitlink was put back with `update-index` before committing. HANDOFF's pin mentions now say v1.11.5.341; ARCHITECTURE and SECURITY already did. Fixes:

- Deletes could strip the marker. `[IO.Directory]::Delete(x, true)` goes in directory order, where `.scarlet-install` sorts first, so a delete stopped by an open file left an unmarked folder the next install refused as foreign. A new `Remove-OwnedFolder` deletes every entry but the markers, then the markers, then the folder. It is used for the leftover `.retired` before the swap (whose failure now reports the folder state and swaps nothing), for `.retired` after the swap, and for stale and failed staging.
- The folder rules moved to the dot-sourced library `scripts/InstallFolders.ps1`: markers, final paths, ownership and link checks, the delete, the swap. That lets `Test-InstallMeter.ps1` run the delete and swap paths on fake installs without building. New cases:
  - a `.retired` with a file held open in a subfolder: the swap is blocked with nothing moved, the marker survives, `-CheckOnly` still accepts the folder, and once the file is released a rerun completes the swap;
  - a failed rename is undone;
  - a foreign `.staging`, and an install with a foreign `.retired`: both refused, all foreign folders byte for byte unchanged.
- The loopback gzip tests used the production `SocketsHttpHandler`, which honours `HTTP_PROXY` and the system proxy. They now use its settings with `UseProxy = false`, set in the tests, so the public API is unchanged. ARCHITECTURE's "nothing leaves the machine" now holds.
- An existing install folder that is not a link is resolved through `GetFinalPathNameByHandle` too, not only its parent, so an 8.3 name for it matches the running-meter check and gives the siblings real names. Docs say so.
- The ownership checks and the `.retired` guard run again just before the swap, like the running-meter check.
- DEVELOPMENT notes that the trailing dot or space check is defence in depth behind `GetFullPath`, and that subst'd drives are resolved but untested.

Checks:

- `Build.ps1` Debug and Release, with upstream at v1.11.5.341: Scarlet's projects had 0 warnings, and 269/269 tests passed.
- `Test-InstallMeter.ps1` under Windows PowerShell 5.1: all 36 cases passed (the short-name case is skipped on this volume), no temp leftovers.
- Library mutations, each caught:
  - a plain recursive delete: the marker was lost and the next install refused the folder;
  - no staging ownership check;
  - no `.retired` ownership check;
  - no undo.
- With `HTTP_PROXY` and `HTTPS_PROXY` at `http://127.0.0.1:9`, the checker tests passed 71/71. With the proxy line removed they failed 2 (both loopback tests).
- Two `-SkipTests` installs of the merged tree into a uniquely named scratch folder passed the layout check and left the install and `.previous`, both marked. The folder was removed afterwards.

Not checked:

- A subst'd drive.
- pwsh locally; CI runs both scripts under it.
- The checks repeated just before the swap run after the build, so no test reaches them; code reading only.
- The install folder's own 8.3 resolution: under Windows PowerShell 5.1, .NET Framework's `GetFullPath` already expands short names, so removing it fails nothing there; only pwsh needs it, and no case covers it.

**Third audit (deep pass on the refactor, head b5ea0b2): approve with nits.** The merge 2c3e237 brought exactly main's change and kept the submodule at aa3ffc6; the library preserves every refusal and its order, `-CheckOnly` still touches nothing, dot-sourcing uses `$PSScriptRoot`, and `Remove-OwnedFolder` deletes links inside an owned folder without following them (probed with junctions). Applied: the undo case now fails, rather than printing "ok" and crashing, when a rename is not undone (`Test-Unchanged` treats a missing folder as changed); the two untested paths above are listed.

**Next:** CI on the PR, Harry's go-ahead and a squash merge. The overlay notice and settings switch for the checker follow in their own PR (#10).

## 2026-10-24 — Upstream v1.11.5.341

**What:** `upstream/` moves from release v1.11.4.334 (57974f8) to release v1.11.5.341 (aa3ffc6), published 2026-10-23, with `scripts/Update-Upstream.ps1`. Upstream's changes that reach Scarlet:

- Boss data: "Fixed incorrect reading of some bosses max hp". `MobPacketProcessor` replaces its byte-scanning heuristic with a structured parse: HP and max HP are 64-bit varints (the new `PacketReader.ReadVarInt64`), still handed on as `long`, which the engine maps whole (`TargetInfo.HpCurrent`/`HpTotal`, a new test above the `int` range). Taken as is.
- The same rewrite changes how a summon's owner is read (a `u32` from the structured parse instead of a scanned `ushort`, feeding `EntityTracker.RegisterSummon`), no longer requires current HP to be at least max HP before a mob is created, and stops creating target entries for player summons. A layout it does not expect now throws `PacketParseException`, which upstream's dispatcher logs at Error level and drops that packet only.
- "My Style" colours for the player's own name and bar: eight new `IAppSettingsService` members used only by upstream's UI. The interface change stopped the engine compiling, as designed; `UpstreamSettingsBridge` implements them as inert in-memory values with upstream's defaults.
- Not used here: upstream's UI changes, and the `appsettings.user.json` it removed from its repository.

**Why:** a release with a parsing fix; Scarlet takes upstream releases only, never `dev` (AGENTS.md).

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 174/174 before the audit, 175/175 after (`BossHpBeyondTheIntRangeIsMappedWhole`). Release shows five compiler warnings, all in upstream's own code (`ServerMap`, `FilePacketCaptureDevice`, `DotDamagePacketProcessor`, `PartyProcessor`), none in Scarlet's.
- A self-contained single-file publish (the CI command) and `Test-PublishLayout.ps1`: complete.
- The published `ScarletMeter.exe` with `--no-capture` and a throwaway data directory (`Smoke-Meter.ps1`): the overlay opened ("Capture is off"), upstream registered its 15 opcode processor mappings, no warnings in the log, exit code 0.

**Not checked:** the boss HP fix and the rewritten summon-owner parsing, which need the game (HANDOFF.md, in-game acceptance). After the first session, look for `PacketParseException` errors in `logs/scarlet-*.log`: each unexpected mob packet logs one.

**Audit (pr-auditor, deep pass): approve with nits.** Pin, release and an unmodified submodule confirmed; the HP path has no narrowing; no new registration, game data, package or capture-to-disk change reaches Scarlet. Applied: the summon parsing change above and an in-game item for it, a test for boss HP above the `int` range, and the example tag in `Update-Upstream.ps1`. Left as a follow-up: upstream now starts WebView2 with background and occlusion throttling turned off (`ConfigureWebView2Arguments` in its UI); whether Scarlet's overlay needs the same is untested.

**Next:** CI and merge.

## 2026-10-24 — World event notices in the overlay

**What:**

- **Notices:** `ScarletMeter.WorldEvents` is wired into the overlay. A slim strip at the bottom of the meter (`AlertStrip`) shows:
  - "Soon" before an enabled event starts, with a countdown and a bar filling over the lead time.
  - "Portal open" or "Entry open" while it can be entered.
  - At most two notices; optionally, the next event as one dim line when nothing is due.
- **`AlertMonitor`** sleeps until the plan's next change, at most a minute, and signals each event occurrence once, when its Soon notice appears. Failures are logged and retried a minute later, never thrown into the timer thread.
- **`MeterWindows`**, if the player opts in, brings a minimized meter back once per occurrence without activating it (`SW_SHOWNOACTIVATE`), so the game keeps focus.
- **Settings:** `AlertSettings` covers on/off, lead time 1–60 minutes (default 5), events (default: world events and bosses, not resets), server UTC offset (+9), the next-event line, and bringing the meter back (off). The new Alerts tab lists each event with its schedule and its state in local time ("next Sun 22:00", "entry open now", "running, entry closed"), and carries the accuracy warning.
- **Settings file:** `SettingsStore` accepts a UTF-8 byte-order mark. Found in the smoke run: a `settings.json` written by Windows PowerShell (or saved as "UTF-8 with BOM") was treated as unreadable and set aside, so a hand edit lost every setting.
- **Rules:** AGENTS.md and ARCHITECTURE now say the app references the engine and the dependency-free world-events module, never upstream.

**Why:** Harry asked to be told about five minutes before a Spacetime Rift, Shugo Festival or another world event, "sleek and subtle in the overlay, doesn't have to be a popup". The schedule engine came first in its own PR.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 174/174 tests, 16 of them new:
  - `AlertTests`: Soon at the lead time, Open until entry closes, the lead time from settings; nothing when alerts are off or nothing is chosen; the next-event line (and none while a notice is due), the server offset; normalizing alert settings and comparing them by value; wording for countdowns, schedules and each phase of the settings list, in two time zones.
  - `AlertTests`, the monitor on a fake clock: one signal per occurrence (none for Open, none again after switching the event off and on or changing the lead time, one for the next occurrence), the wait capped at a minute and otherwise the next change plus 20 ms; a throwing subscriber and a failed computation stay inside the monitor, which empties the view and retries a minute later; starting twice subscribes once, starting after disposal does nothing.
  - `SettingsTests`: equality varies every `AlertSettings` member; a file from before alerts loads their defaults with no repair notice and restoring off; `"events": null` takes the defaults; a file with a byte-order mark loads.
- `EqualityCoversEveryProperty` failed until `MeterSettings.Equals` and its test included the new section, as intended.
- Smoke run after the audit fixes (`Smoke-Meter.ps1`, Debug, a seeded file written with a byte-order mark: lead 60 minutes, offset 9.0166, Nahma, rift, Invasion and Shugo):
  - The file loaded (before the store fix it was set aside as unreadable and defaults were used).
  - The strip showed Shugo and Invasion counting down; the only live region was the hidden summary "Shugo Festival starts at 05:59; Dimensional Invasion starts at 06:29" (1 by 1 px, clipped, not drawn).
  - The Alerts tab read "next 05:59", "next Sun 22:59" for Nahma and "next Wed 18:00" for the weekly reset; the offset field showed "9.02". No warnings in the log; exit code 0.
- First smoke run, before the audit, with a seeded lead time of 60 minutes and Shugo only:
  - The strip read "Shugo Festival · in 27:08" and ticked down second by second.
  - The Alerts tab rendered all events with schedules and next local starts (screenshots, not committed).
  - With the meter minimized by the real shortcut, switching on Dimensional Invasion (its entry was open at that moment) brought the overlay back to Normal on its own; the strip then read "Dimensional Invasion · Entry open · 0:03 left" above Shugo.
  - The saved events were `invasion,shugo`; no warning in the log; exit code 0.

**Not checked:**

- Real event times in the game (HANDOFF.md).
- Whether the restored meter really leaves the game's focus alone in a fullscreen or borderless game.
- Readability over the game.
- A single-file publish of this branch, which CI runs with the layout check.

**Not done:** per-event time overrides in settings (HANDOFF.md).

**Audit (pr-auditor, standard pass with a closer look at threading and windows): approve with nits.** Addressed:

- Major: by default a minimized meter came back about four times an hour (Soon and Open of Shugo and Invasion each restored it), and editing the event list restored it again. Decision: restore once per event occurrence, on its Soon notice only, kept until the occurrence starts; and default "Bring the meter back" to off, so hiding the meter keeps it hidden unless the player opts in. Existing files have no `alerts` key, so nobody is opted in by upgrading.
- Minor: only `ArgumentException` was caught on the timer thread; now every failure is caught and logged, and handlers are invoked inside the guard. `Start` is idempotent and does nothing after `Dispose`.
- Minor: the Alerts tab's countdowns never ticked and "on now" showed after entry closed; it now shows local times and phase labels only, and re-renders when the monitor wakes.
- Tests for the gaps the auditor's mutation runs left (listed under Checks). Nits: the `RefreshAt` doc, the live region on the per-second countdown, the raw offset double in the field, and `alerts.css` in `Test-PublishLayout.ps1`.
- Downgrade: no version bump, as the section is additive. An older build ignores `alerts` and drops it on its next save.

A second pass over the fixes (66dc773, including the byte-order-mark change, which the first pass had not suggested) approved with nits, now applied: the hidden status region is always rendered, so the first notice is announced; a settings-list case where the local and UTC dates differ; the `Changed` doc says it fires on every refresh.

**Next:** CI and merge; then the details and history UI on the engine PR.

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

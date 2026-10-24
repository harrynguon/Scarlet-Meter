# Lessons

Mistakes found in this repository's work, why they got through, and what now catches them. Read this before starting a change; add an entry with every fix for a mistake (AGENTS.md, "Lessons"). Prefer a guard (a test, a check, a rule) to a note. Newest first within each section.

## In the product

**Fitting the panel to its content squeezed the expanded strip, and the first fix clipped it** (audit and Codex on #26, fixed before merge).
- What: changing the rows from `flex: 1` (they gave up their space) to `flex: 0 1 auto` (they keep their own height) made the expanded notice strip, a scroll area that may shrink, give up space in proportion to the rows: with a full party it opened about one notice tall, the same squeeze #20's lesson describes. The first fix, `flex-shrink: 0` on the strip, then let it run past the bottom of a short window's panel, clipping its "Show less" so it could not be collapsed (Codex). The demo check could show neither, since the demo has four players and the default window.
- Why: each change was judged by the case in front of it (four rows with the notices held; then a full party), not by the page's extremes in both directions (many rows, and a short window), and a flex rule on one child changes how its siblings shrink.
- Guard: the rows give up their space first (`flex: 0 1000 auto`, a heavy shrink weight), and the strip can still shrink to what is left; `RazorMarkupTests.TheOverlayPanelFitsItsContent` asserts both (a weight of 100 or more, no `flex-shrink: 0` or `flex: none` on the strip) and pins what the panel's height depends on (`#app` and `html, body` at 100%, no other rule sizing the panel). A headless Edge measurement with the real styles and synthetic markup (no window) compared main, the first fix and the final rule across row counts, window heights and strip states (WORKLOG). When a layout rule changes, measure both extremes, not only the demo.

**Boss-fight hiding trusted the current target** (audit on #24, fixed before merge).
- What: the first "Hide during boss fights" hid the notices while a boss was the current target and its fight live or settled. Settled has no end and upstream changes its target only on a hit, so after a wipe the notices would have stayed hidden until the next hit; and the target flips to adds and back, so the strip would have come and gone on every swap. Unmounting the strip also broke its own live-region rule, and "Bring the meter back before an event" could restore a minimized meter mid-fight with its notice hidden.
- Why: the same assumption #21's lesson names, one steady target, came back in a new feature: the rule was read off `FightState`'s phases without asking how long a phase can last or what the target does between hits; the strip's own comment, which said why it is never unmounted, was not reread.
- Guard: `BossFightHold` follows the boss across adds and lets go after 60 seconds of still HP, with `BossFightHoldTests` (each rule shown to fail when removed); the strip is held, not unmounted; `AlertRestoreGate` with its test. For any rule fed by upstream's target, write the test for adds between hits and for a target that never goes.

**Switches that were never saved went unnoticed** (audits on #22 and #24, fixed before merge).
- What: twice, a `[JsonIgnore]` on a new setting (`PictureOpacity`, then `HideDuringBossFights`) passed every test, so the player's choice would have been lost at the next start without a failing test.
- Why: each new setting got a default test and an equality entry, but the round trip through the file was tested per setting, by hand, and forgotten for the new one.
- Guard: `SettingsTests.EveryOnOffSettingSurvivesASave` flips every bool in every section, saves, loads and checks each (shown to fail with a `[JsonIgnore]` on two switches), so a new switch is covered without a new test; numeric settings keep their own round-trip tests.

**"Kill in", the glide and the effects assumed one steady target** (audit on #21, fixed before merge).
- What: "Kill in" forgot the boss whenever the current target (upstream's: the one last hit) flipped to an add, so its estimate kept restarting; the glide measured row positions from the panel and kept them across a row-size change, so a later reorder glided and flashed rows that had not overtaken anyone; the effects ran on every row and would have run between pulls; two test guards (scripts, styles) matched too loosely to fail when broken.
- Why: the features were built and checked against the demo, whose single boss is the only target and whose rows go when it dies; the tests were written alongside the code and checked for passing, not each broken on purpose until the audit did it.
- Guard: per-target samples, `FightState` pausing the effects, an eight-bar cap, rank-based flashes and re-measured positions, each with a test or a visual check at other row sizes; whole-selector and every-tag guards, each shown to fail. Ask how a value behaves in a real fight (targets flipping, a full party, the time after a kill), not only in the demo, and break every new guard once before trusting it.

**The expanded alert strip would have squashed its notices instead of scrolling** (audit on #20, fixed before merge).
- What: the expanded strip was capped at 45% of the meter with `overflow-y: auto`, but each notice, a flex item with `overflow: hidden`, could shrink to nothing, so five due notices would have been squeezed into the cap with their text clipped and no scrollbar. Once drawn, the strip showed two more problems no reading had found: "Show less" scrolled out of sight, and the strip had no edge against the rows above.
- Why: the layout was written and reasoned about but never drawn, since drawing it needs a window and the player may be in game; the tests cover the helper's lists, not the CSS.
- Guard: `.alert { flex: none }`, a sticky "Show less" and a top line on the expanded strip, each with a comment saying why. A visual change is not done until it has been seen once, at its fullest (here, more notices than fit): ask the player for a moment when a window can open (LESSONS, "UI checks opened windows"), rather than skipping the look.

**The in-place update guarded "data", not the player's files** (deep audit and Codex on #19, fixed before merge).
- What: the first version of the one-copy update moved only a top-level `data` folder and deleted everything else in the old copy, so a `data - Copy` backup or notes beside the exe would have gone on the next update. When an undo failed it still said "your copy and its data are as they were" while the data sat in `.staging`. Its deletes removed the portable markers in directory order, so a delete stopped by an open file left an unmarked folder every later run refused. A drive root as output root became `C:`, which Windows reads as that drive's current directory. Two runs at once could delete each other's staging folder. After those fixes, Codex and a second audit found the recovery itself was not resumable: it dropped the staging marker before the rest of the player's files had followed from `.old`, so a recovery stopped there left a runnable copy without its data that every later run refused; and the earlier-build clean-up deleted through an inner folder that was a junction.
- Why: "the player's data" was equated with one folder name instead of "everything the build did not put there"; the failure paths were tested for the first failure only, not for an undo or a recovery failing too; the marker-last delete existed for the installer but was reused without its markers; root paths, links one level down and concurrency were not considered.
- Guard: a manifest of the build's files, so everything else is carried over and only listed files are deleted; an undo that fails leaves the copy's name free for the next run to finish and says where everything is; the copy keeps its staging marker until a recovery has finished, so the next run resumes it; `Remove-OwnedFolder` takes the markers to keep until last; `Remove-PortableFolder` refuses a folder that is a link; `Resolve-PortableRoot` keeps `C:\`; a named mutex per output root. `Test-PortableFolders.ps1` covers each, with a stand-in for the move that makes chosen moves fail and real held-open files, and each case was shown to fail with its guard removed. When code deletes near the player's files, define what is ours by listing it, and test each recovery step failing as well as the first.

**Share bars were hard to see** (Harry, from the screenshots, 2026-10-24).
- What: the details and saved-fight bars used a flat 32% red, which on the near-black panel came out as dark maroon.
- Why: the colour was picked as a token, not looked at on the real background; smoke runs checked the bars existed, not that they could be seen.
- Guard: bars use the overlay's gradients on a visible track. Look at a screenshot of every new visual element before calling it done.

**Settings backup overwritten when the main file was missing** (Codex on #2, fixed 2026-10-24).
- What: with `settings.json` missing and `settings.json.bak` damaged, from a newer build or locked, saving stayed on, and the second save's `File.Replace` replaced the only recoverable settings. Now a damaged backup is kept aside, a newer one is used read-only, and a locked or unmovable one switches saving off.
- Why: the keep-aside and read-only protections were written for a damaged main file only; the missing-file branch was not walked through the same cases.
- Guard: four `SettingsTests.WithoutTheMainFile...` tests (damaged, newer, locked and unmovable backups), shown to fail with the old branch. When adding a protection, list every branch that can reach the same write.

**Saved window position restored off every monitor** (Codex on #2, fixed 2026-10-24).
- What: placement was checked against the virtual screen's bounding box, which includes the empty corners of an L-shaped or offset layout.
- Why: a single-monitor machine never shows the difference.
- Guard: `ScreenPlacementTests` on a synthetic L-shaped layout. Test geometry against layouts this machine does not have.

**Update commands shown relative to the shell's directory** (Codex on #10, fixed in #12).
- What: Settings, About showed `scripts\Update-Upstream.ps1`, which only works from inside the checkout.
- Why: the paths rule was applied to code, scripts and docs, but not to text the UI shows.
- Guard: `RazorMarkupTests.ScriptCommandsShownToThePlayerAreRootedAtTheCheckout`. Commands shown to the player follow the same paths rule as docs.

**Next-event line skipped an event still running** (Codex on #6, fixed in #12).
- What: the line kept only occurrences in phase Upcoming, so Shugo vanished for four minutes after its entry closed (Invasion for ten).
- Why: the tests used an event that was not running at the instant tested.
- Guard: `AlertTests.TheNextEventLineKeepsAnEventWhoseCurrentOccurrenceIsStillRunning`. Test schedule code at the awkward instants (just after an entry closes, during a running occurrence), not only before a start.

**Buff counts rendered as Razor source text** (found in the #8 smoke).
- What: `x@buff.Applications` rendered literally: Razor reads a letter followed by `@` as an e-mail address.
- Why: the earlier smoke counted buff rows instead of reading them.
- Guard: `RazorMarkupTests.NoExpressionFollowsALetterOrDigitWithoutParentheses`; the scarlet-smoke skill says to read the text, not count elements.

**"Skill icons" off still loaded graph icons from the CDN** (audit and Codex on #8).
- What: `DamageChart` called `SkillIcons.Url(icon)`, whose `enabled` parameter defaulted to true.
- Why: an optional parameter let one caller forget the setting.
- Guard: `SkillIcons.Url` has no default, so a caller that leaves the setting out does not compile. A privacy or security switch is never an optional parameter.

**Late results overwrote newer state** (audits and Codex on #8, #10).
- What: a slower saved-fight load relabelled the details window; an older history search overwrote a newer page; a queued timer callback could call GitHub after the check was switched off.
- Why: results were assigned before checking they were still wanted; `ITimer.Dispose` does not stop a callback already on its way.
- Guard: load into a local, then apply only if the request is still current (a counter or the selection); check the switch and disposal at the top of every timer callback; tests with a check held in flight (`UpdateNoticeTests`).

**A hidden meter popped back four times an hour** (audit on #6).
- What: "bring the meter back for an alert" was on by default and fired for both the Soon and the Open notice of hourly events.
- Why: the default was chosen without counting how often it would fire.
- Guard: once per occurrence, Soon only, off by default; `AlertTests`. Count how often a default fires in a real hour before turning it on.

**A hand-edited settings file with a byte-order mark lost every setting** (found in the #6 smoke).
- What: the UTF-8 JSON reader rejects a BOM, so the file counted as unreadable and defaults were used.
- Why: tests wrote files without a BOM; only a smoke run that seeded a file from Windows PowerShell showed it.
- Guard: `SettingsTests.AFileSavedWithAByteOrderMarkLoads`. Seed test files the way people actually write them.

## In the process

**A library "nobody used" was needed at run time, and only a launch showed it** (re-audit on #23, caught before merge).
- What: the re-audit found the exe bundles Microsoft's Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, from the versioned `net10.0-windows10.0.20348.0` target) under a licence known only by a URL, said nothing in the meter uses it, and suggested plain `net10.0-windows`. That build passed all 405 tests and its publish passed the layout check, but the published exe crashed on start: BlazorWebView draws the see-through overlay with WebView2's composition control, which loads the projection. The retarget was reverted.
- Why: "unused" was judged by searching our own code, not what our dependencies load; and no check before the visual one runs the published program, so a build that cannot start looked green. The first licence review had also listed only what the NuGet restore names, which is how the projection went unnoticed until the re-audit.
- Guard: `ThirdPartyNoticesTests.TheProgramKeepsTheWindowsSdkProjectionAndNamesIt` fails if the app loses its Windows 10 SDK target or the notices stop naming the projection and its licence; the target framework carries a comment saying why. Smoke-run the published exe (not only the Debug build) after any change to the target framework, packages or publish settings, and for a licence review list what the build output holds, not only the package graph.

**The first release named its licences but did not carry them** (deep audit on #23, fixed before any release).
- What: the planned v0.1.0 zip listed each component's licence by name, but binary copies must carry the texts and notices themselves (WebView2's BSD-style licence says so in as many words; MIT and Apache-2.0 too); .NET's notices were linked on `main`, not the shipped version; and "the source is the tag" left RATmeter's half of the source in someone else's repository, since GitHub's source archives drop submodules. The release job also ran restores, builds and tests with a write token kept in git's config, took the tag name into a script's text, and accepted a tag on any commit.
- Why: the review asked whether each licence is compatible with the GPL, not what each one requires of a binary copy; and the workflow was written as a build script, without asking what its token could do in a compromised build.
- Guard: a `licenses` folder in the zip (texts, every package's own files byte for byte, the runtime packs' notices from the packs used), checked by `ThirdPartyNoticesTests`, `Test-PublishLayout.ps1` and the release's zip check; `ScarletMeter-<version>-source.zip` from `Write-SourceArchive.ps1`, run in CI and attached to every release; a read-only build job and a write-only draft job, pinned actions, the tag through the environment and on `main`. For anything distributed, read each licence's conditions for binary copies, and give a workflow's write token to the smallest job that needs it.

**Guards were weaker than the words describing them** (deep audit on #23).
- What: the WORKLOG said the licence test fails "when `LICENSE` stops being the plain text", but it checked only the first and last lines, so a changed sentence in the middle passed; the package test matched id and version only, so a wrong licence passed; and the docs said the repository "has been public since 2026-10-25" while it was still private.
- Why: each guard was broken once, in the way it was written for (appended text, a dropped row), and the docs were written for the state the work was heading to.
- Guard: the GPL's SHA-256, licence cells compared with each package's .nuspec, an exact package set, and each guard broken in several places before it is described (WORKLOG lists them); planned states are written as planned ("to be made public once #23 merges") with a HANDOFF checklist, and updated when they happen.

**Every build was a new copy to unzip** (Harry asked, 2026-10-24).
- What: `Publish-Portable.ps1` wrote each build to a new stamped folder and zip and never replaced one, so the player had to unzip every update by hand and ended up with several folders, each with its own `ScarletMeter.exe`, not knowing which to click.
- Why: "never overwrite anything" was chosen as the safe default from the developer's side; the player's question, which exe to run, was not asked. Safety belonged around the player's data, not around program files.
- Guard: one live copy, `portable\ScarletMeter`, updated in place by renames that keep its `data` folder; earlier builds are removed; CI checks that one `ScarletMeter.exe` is left and a planted settings file survives. When a safety rule makes the player do more work, ask what it protects and protect only that.

**Remove-Item on a junction waits for a confirmation** (Test-PortableFolders.ps1, 2026-10-24).
- What: in Windows PowerShell 5.1, `Remove-Item -Force` on a junction asks to confirm, and a non-interactive run fails there.
- Guard: delete a junction itself with `[IO.Directory]::Delete(<path>, $false)`, which never follows it.

**UI checks opened windows over the player's game** (2026-10-24).
- What: back-to-back smoke runs (a screenshot tour, the share-bar checks) started the meter on Harry's desktop; its always-on-top overlay and the details, history and settings windows popped up and took focus while Harry was in AION 2, which got Harry killed in game.
- Why: smoke runs were treated as local and harmless, without asking whether the PC was in use.
- Guard: AGENTS.md "Review and merge" and the scarlet-smoke skill: ask before any check that opens windows or uses the mouse. Builds and tests without windows need no ask.

**A reused branch would have built old code** (Codex and the audit on #16).
- What: Update-Meter.ps1 reused an existing `update/upstream-<tag>` branch as it was, so a run after new commits on main would have zipped the branch's older source; and it read the current release from `upstream/`'s files, which a `git pull` leaves behind, then claimed a branch it had just deleted.
- Why: the first run was tested from a fresh state only; reuse and an out-of-date submodule were not.
- Guard: a reused branch must contain HEAD (`merge-base --is-ancestor`), the current release comes from `HEAD:upstream`, and the branch line is printed only when the branch exists. Test a script's second run and a `git pull`-ed checkout, not just the first run.

**A test harness cut a script short** (testing Update-Meter.ps1, 2026-10-24).
- What: piping the script's output through `Select-Object -First 20` stopped the pipeline after 20 matching lines, which stopped the script itself partway through the build, so the run looked like a script failure.
- Guard: never truncate a long-running command's pipeline; write its output to a log file (`*>&1 | Out-File`) and filter the log afterwards.

**Distribution was designed from the developer's side** (Harry asked for a portable build, 2026-10-24).
- What: the only way to get the meter was an installer into `%LocalAppData%\Programs`, though RATmeter, which players know, is a zip to unzip and run with its data beside it.
- Why: the update flow (build from source, keep a rollback) was designed first, and how a player expects to run a meter was not asked.
- Guard: `Publish-Portable.ps1` and the portable layout, checked by CI. Ask how the player wants to run a feature, starting from the tools they already use.

**A shareable zip carried the builder's account path** (audit on #15).
- What: the first portable zip had debug symbols (whose paths name the Windows account and checkout) and API documentation files.
- Why: the publish settings were copied from the local install, which is never handed on.
- Guard: the portable publish uses `DebugType=none` and no documentation files; before sharing any build, search its files for the account name.

**The README did not say what running needs or where the exe is** (Harry asked, 2026-10-24).
- What: one sentence mixed build and run requirements (it listed the .NET 10 SDK, which only building needs), and nothing said where the installed exe lives or how to start it.
- Why: the docs were written from the developer's side (build, test, publish) and never read as a player installing it.
- Guard: README "Requirements" (run versus build) and "Run it". Read user-facing docs as the player before calling a feature done.

**Codex's review comments were not read before merging** (PRs #4 to #10).
- What: Codex left eight findings; nobody read them, and two were still bugs on `main` after the merges.
- Why: the merge checklist had CI and the auditor, not the PR's comments.
- Guard: AGENTS.md "Review and merge" and the scarlet-ship skill: wait for Codex's review, read every comment, fix or answer each, record them in the WORKLOG.

**A review stuck at "Running" was taken as silence** (PRs #1 to #3).
- What: Codex never finished reviewing three PRs; their three findings surfaced only when the review was requested again.
- Guard: the same rule: a summary that never says Completed means no review happened; comment `@codex review`.

**A check that proved nothing** (this session, twice).
- What: a "fails with the bug put back" run passed because the test edit had not been applied (the editor refused an unread file), and a mutation did not compile.
- Guard: a fail-first check counts only when the output names the new test as failed. Read the output, not the exit code.

**A wait loop matched the wrong commit** (#12).
- What: it waited for `b62ae8f` while the commit was `b62ae8b`, so it ran to its time limit.
- Guard: match on the full SHA from `git rev-parse`, never a retyped prefix.

**A merge re-staged an old submodule commit** (#4).
- What: `git add -A` after resolving a merge staged the worktree's stale `upstream` checkout over main's newer pin.
- Guard: after every merge, `git submodule update` before staging, and check `git ls-tree HEAD upstream` against `main`.

**An upstream release changed more than its notes said** (#7).
- What: v1.11.5.341's boss HP fix also rewrote how a summon's owner is read.
- Guard: the scarlet-upstream-update skill: read the diff, record every parsing change, add an in-game item for each.

**Shell quoting on this machine** (throughout).
- What: Windows PowerShell 5.1 splits native arguments at embedded double quotes (a commit message was lost, `gh --jq` filters broke); its double-quoted here-strings eat backticks; Bash heredocs above about 10 KB fail.
- Guard: commit with `git commit -F -` and a quoted Bash heredoc; run `gh --jq` from Bash; build Markdown-carrying scripts from single-quoted here-strings saved as UTF-8 with a BOM; write large files with the editor. All in the scarlet-ship and scarlet-smoke skills.

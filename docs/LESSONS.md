# Lessons

Mistakes found in this repository's work, why they got through, and what now catches them. Read this before starting a change; add an entry with every fix for a mistake (AGENTS.md, "Lessons"). Prefer a guard (a test, a check, a rule) to a note. Newest first within each section.

## In the product

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

**Every build was a new copy to unzip** (Harry asked, 2026-10-24).
- What: `Publish-Portable.ps1` wrote each build to a new stamped folder and zip and never replaced one, so the player had to unzip every update by hand and ended up with several folders, each with its own `ScarletMeter.exe`, not knowing which to click.
- Why: "never overwrite anything" was chosen as the safe default from the developer's side; the player's question, which exe to run, was not asked. Safety belonged around the player's data, not around program files.
- Guard: one live copy, `portable\ScarletMeter`, updated in place by renames that keep its `data` folder; earlier builds are removed; CI checks that one `ScarletMeter.exe` is left and a planted settings file survives. When a safety rule makes the player do more work, ask what it protects and protect only that.

**Remove-Item on a junction waits for a confirmation** (Test-PortableFolders.ps1, 2026-10-24).
- What: in Windows PowerShell 5.1, `Remove-Item -Force` on a junction asks to confirm, and a non-interactive run fails there.
- Guard: delete a junction itself with `[IO.Directory]::Delete(<path>, $false)`, which never follows it.

**UI checks opened windows over the player's game** (2026-10-24).
- What: back-to-back smoke runs (a screenshot tour, the share-bar checks) started the meter on Harry's desktop; its always-on-top overlay and the details, history and settings windows popped up and took focus while he was in AION 2, and he died in game.
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

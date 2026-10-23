---
name: scarlet-ship
description: Take a Scarlet Meter change from a branch to main the way the repository requires - its own worktree and branch, both builds and tests, a UI smoke when pages change, docs and a WORKLOG entry with the checks actually run, a pull request, an independent audit, green CI, and a squash merge pinned to the tested commit once the player has said go ahead. Use whenever a change to Scarlet Meter is ready to share.
---

# Shipping a Scarlet Meter change

Rules live in `AGENTS.md`; this is the sequence. Use absolute paths in every command.

## 1. Branch in a worktree

```powershell
git -C '<checkout>' fetch origin
git -C '<checkout>' worktree add '<short absolute path>' -b <branch> origin/main
git -C '<short absolute path>' submodule update --init
```

Keep the path short (native SQLite fails under long paths). Parallel work goes in separate worktrees; never two writers in one.

## 2. Check

- `scripts\Build.ps1` and `scripts\Build.ps1 -Configuration Release` (set `SCARLET_DOTNET`/`DOTNET_ROOT` first; see the scarlet-smoke skill). Scarlet's projects build with warnings as errors; upstream's own warnings are expected.
- Pages, styles, windows or startup changed: run the scarlet-smoke skill.
- Web assets or project files changed: a Release self-contained single-file publish, then `scripts\Test-PublishLayout.ps1 -PublishDirectory <absolute path>`.
- New behaviour has tests; a new guard is shown to fail when the protected code is wrong (then reverted).
- No `%TEMP%\scarlet-*` leftovers.

## 3. Document

Update the docs the change touches (ARCHITECTURE for structure, DEVELOPMENT for commands, SECURITY for data or network, HANDOFF for acceptance items) and add a `docs/WORKLOG.md` entry at the top: what, why, checks actually run with results, not checked, next. Never describe an unchecked acceptance item as done.

## 4. Pull request

Commit (message ends with the Co-Authored-By line the session's attribution asks for), push, and `gh pr create -R harrynguon/ScarletMeter --base main --head <branch>` with a body listing the checks actually run and ending with the session's PR attribution line.

- Write multi-line messages with `git commit -F -` and a quoted heredoc from Bash. Windows PowerShell 5.1 splits a native argument at embedded double quotes, so `-m` with a here-string that holds `"` commits nothing.
- A PR that needs another open PR's code is stacked: `--base <that branch>`, so its diff shows only its own change. GitHub retargets it to `main` when the base branch is deleted after its merge; then merge `origin/main` into it, rerun the checks and let CI pass before merging.

## 5. Audit and review comments

Docs-only changes (Markdown only) need no audit. Otherwise run the `scarlet-auditor` agent: standard by default, deep at high effort for window lifecycle or WebView2 hosting, threading, saved data, validation or security, capture or the engine's upstream mapping, or package versions. Fix its findings; fixes that apply its own suggestions need no second audit. Record the audit in the WORKLOG entry.

Every PR, docs-only ones included, also gets the Codex bot's review (chatgpt-codex-connector) when it is opened. Before merging:

```bash
gh api repos/harrynguon/ScarletMeter/issues/<n>/comments --jq '.[] | select(.user.login == "chatgpt-codex-connector[bot]") | .body'   # summary: wait for "Completed"
gh api repos/harrynguon/ScarletMeter/pulls/<n>/comments --jq '.[] | "\(.path):\(.line // .original_line) \(.body)"'                   # inline findings
gh api repos/harrynguon/ScarletMeter/pulls/<n>/reviews --jq '.[] | "\(.user.login) \(.state) \(.body)"'
```

(Bash, as the session's shell runs it; Windows PowerShell 5.1 mangles the quotes in `--jq` filters.) Check each finding against the current head (a later commit may already fix it), fix the valid ones with a test where it can fail, answer the rest, and list every finding and its outcome in the WORKLOG entry. After pushing fixes, comment `@codex review` and check the new review the same way. A summary stuck at "Running" means no review happened: comment `@codex review` again rather than merging on silence.

## 6. Merge

Only with the player's go-ahead, green CI on the head commit, the audit's findings addressed and every Codex comment checked:

```powershell
gh run list -R harrynguon/ScarletMeter --branch <branch> --limit 2
gh pr merge <number> -R harrynguon/ScarletMeter --squash --match-head-commit <full 40-character sha>
```

Then sync the main checkout (`git switch main`, `git pull --ff-only`), check the merged tree equals the tested commit's tree (`git rev-parse HEAD^{tree}` vs `<sha>^{tree}`), and remove the worktree and branch (deleting the remote branch also needs the player's go-ahead). When later branches were built on this one, merge the new `origin/main` into them (or `git rebase --onto origin/main <old base> <branch>` for unpushed ones) before their own PRs merge.

Conflicts between parallel PRs are usually only in `README.md` and `docs/` (WORKLOG entries, HANDOFF state lines). Keep both sides' content, WORKLOG newest first with a blank line between entries, then fix any state line that one side made stale (for example the pinned release). Run `git submodule update` after a merge that moved `upstream`.

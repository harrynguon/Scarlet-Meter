# Development

Building needs Windows, the .NET 10 SDK and Git. Running needs the WebView2 runtime (ships with Windows 11) and, for live capture, [Npcap](https://npcap.com) installed with "Install Npcap in WinPcap API-compatible Mode" (and its "Restrict ... to Administrators only" option unticked, unless the meter runs as administrator). Tests, `--no-capture` and `--demo` do not need Npcap. A Debug build or `dotnet run` needs the .NET 10 runtime (the SDK has it); the installed build is self-contained and needs no .NET install. README.md ("Requirements", "Run it") is the player's version.

## Clone and build

`<checkout>` in the commands below is the absolute path of the clone.

```powershell
git clone --recurse-submodules https://github.com/harrynguon/Scarlet-Meter.git "<checkout>"
```

If the clone was made without `--recurse-submodules`, `upstream/` is empty: run `git -C "<checkout>" submodule update --init`. `scripts/Build.ps1` reports this.

```powershell
# From any directory; the script derives every path from its own location.
& "<checkout>\scripts\Build.ps1"                 # restore (NuGet audit as errors), build, test
& "<checkout>\scripts\Build.ps1" -Configuration Release -SkipTests
```

The script uses the `dotnet` on PATH and insists on a .NET 10 SDK. When another SDK is first on PATH, point `SCARLET_DOTNET` at the right `dotnet.exe` (an absolute path) for the session instead of editing PATH.

## Run

```powershell
dotnet run --project "<checkout>\src\ScarletMeter.App\ScarletMeter.App.csproj" -- --no-capture
dotnet run --project "<checkout>\src\ScarletMeter.App\ScarletMeter.App.csproj" -- --demo
```

Switches:

- `--no-capture` opens the window without Npcap (status reads "Capture is off").
- `--demo` opens it without Npcap on synthetic fights, for UI work: a party of four ("Demo Gladiator", the user, "Demo Cleric", "Demo Ranger", "Demo Spiritmaster" with a fire spirit) fights one boss after another, each fight lasting about 60 to 90 seconds with crits, back attacks, DoTs, summon damage, buffs, ping and falling boss HP, then saved to history; the next starts ten seconds later. Capture reads as running on adapter "Demo". Demo fights go to `demo-history.db`, never to the real `combat-history.db`. It cannot be combined with `--no-capture`. It exercises fight tracking, details and history, not packet decoding (docs/ARCHITECTURE.md, "Demo mode").
- `--data-dir <absolute path>` moves everything the meter writes (history database, logs, WebView2 profile) for a session, for example to keep a test run away from real data.

Anything else is rejected with a dialog.

Data and logs: `%LocalAppData%\ScarletMeter` (`combat-history.db`, `demo-history.db` after a demo run, `settings.json` and its `.bak`, `logs\scarlet-<date>.log`, `backgrounds\`, `webview2\`). The log is where "[CAPTURE] !!! LOCKED !!!" appears once upstream's capture finds the game's connection.

A startup failure (a data directory that cannot be created, missing game data, an unusable history database, capture that cannot start) shows a dialog and exits with code 1. Once the host (and with it the log) exists it is also logged, at Critical (Error for capture).

The apphost needs a .NET 10 runtime it can find: an installed .NET 10 Desktop Runtime or SDK, or `DOTNET_ROOT` set to one (an absolute path). Without it `ScarletMeter.exe` shows the .NET install prompt and exits with 0x80008096. A self-contained publish carries its own runtime.

## Smoke-testing the UI

`scripts/Smoke-Meter.ps1` starts the built meter (Debug by default; `-Exe` for another absolute path) with a throwaway data directory, runs a script block against its pages and closes it the way a player would, then prints the exit code. The meter is started with WebView2 remote debugging on a free localhost port (or `-DevToolsPort`) for that run only, and only pages served from `https://0.0.0.1/` (BlazorWebView's app origin) are driven. Set `DOTNET_ROOT` as above for a framework-dependent build.

```powershell
& "<checkout>\scripts\Smoke-Meter.ps1" -Steps {
    param($m)
    $m.WaitFor('overlay', "document.querySelector('.target-name')?.textContent", 15)
    $m.Eval('overlay', "document.querySelector('button[title=Settings]').click(); true")
    $m.WaitFor('settings', "document.querySelectorAll('.tab').length", 15)
    $m.Capture('settings', "<absolute path>\settings.png")
    $m.Drag(120, 60)   # real mouse: moves the cursor
}
```

Windows are named by their page's `data-window` attribute (`overlay`, `settings`). `-DataDirectory <absolute path> -KeepData` reuses and keeps a data directory, for example to check that settings and window positions survive a restart. Close other programs' windows away from the overlay before `Drag`, which uses the real mouse.

`-BrowserArguments` adds Chromium switches for the run's WebView2 browsers. An automated check of the boss-timers window must never load aion2timers.com, which bars AI agents and crawlers from its API: pass `-BrowserArguments '--no-proxy-server --host-resolver-rules="MAP *.aion2timers.com ~NOTFOUND, MAP aion2timers.com ~NOTFOUND"'` (without `--no-proxy-server` a proxy would resolve the host itself), so the window shows the browser's own error page and nothing is requested from the site. Its page is not a meter page, so the script does not drive it; use UI Automation for its toolbar, and never invoke "Open in browser", which hands the site to the default browser, which has none of these switches.

## Checking the animation frame cap

`scripts/Test-FrameCap.ps1` checks what `wwwroot/js/frame-cap.js` does, in headless Edge in real time (no window; a throwaway profile with sync off, in a new folder in the user's temp directory, deleted after), reading two synthetic pages over the DevTools protocol on 127.0.0.1: driven animations keep their timing, one the page paused stays put, transitions and one-off animations end and fire their events, a delayed one is picked up, and on a screen faster than 70 Hz a looping animation and bars whose width transitions restart ten times a second change at most 70 times a second (skipped, saying so, on a slower screen). It exits 1 when a check fails. `RazorMarkupTests` pins the script's text only; run this after changing it. CI does not run it.

```powershell
& "<checkout>\scripts\Test-FrameCap.ps1"
```

## Publish

```powershell
dotnet publish "<checkout>\src\ScarletMeter.App\ScarletMeter.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "<absolute output directory>"
```

Keep the whole published folder: the game-data JSON files (`GameData\Assets`) and `wwwroot` sit beside `ScarletMeter.exe`. Check a publish with:

```powershell
& "<checkout>\scripts\Test-PublishLayout.ps1" -PublishDirectory "<absolute output directory>"
```

It fails when a file the meter reads at runtime is missing or the app's assembly was left outside the bundle; it does not launch the meter. `Install-Meter.ps1` (below) runs the same publish and check, and so does CI. No release process exists yet; see docs/SECURITY.md before publishing a binary.

## Portable build

```powershell
& "<checkout>\scripts\Publish-Portable.ps1"                         # build, test, update portable\ScarletMeter
& "<checkout>\scripts\Publish-Portable.ps1" -Zip                    # also ScarletMeter-win-x64.zip, for handing on
& "<checkout>\scripts\Publish-Portable.ps1" -OutputRoot "<absolute folder>"
```

There is one portable copy, `<output root>\ScarletMeter`, already unzipped, and the player always runs the same `ScarletMeter.exe` in it. The script runs `Build.ps1 -Configuration Release` (with the tests, unless `-SkipTests`), publishes the self-contained single-file win-x64 build into `<output root>\ScarletMeter.staging` (marked with `.scarlet-portable-staging`), checks it with `Test-PublishLayout.ps1` and for links, lists its top-level files in a manifest (`.scarlet-portable-files`) and writes the `ScarletMeter.portable` marker. Everything in the copy that the manifest does not list is the player's: the `data` folder, and anything they put beside it (a `data - Copy` backup, notes). An update replaces the build's files and carries the player's over:

1. `ScarletMeter` is renamed to `ScarletMeter.old`;
2. each of the player's entries moves from `ScarletMeter.old` into the new build;
3. `ScarletMeter.staging` is renamed to `ScarletMeter`;
4. `ScarletMeter.old`, now only the previous build's files, is deleted.

All are renames or deletes within the output root, so the player's files are moved, never copied, and only files a manifest lists are deleted. Before step 1 it refuses, changing nothing, when one of the player's entries has a name the new build uses. If step 1 fails (a file in the copy held open by Explorer, an editor or a leftover process), nothing changed and the error says what to close. If step 2 or 3 fails, what moved goes back and step 1 is undone, and the error says the copy is as it was; if something cannot move back, the copy's name is left free, the error says where everything is and not to start the meter, and the next run finishes the update; if only the last undo rename fails, the error says the copy is in `.old` and the next run puts it back. A run cut short between steps (a crash, a power cut) is resolved at the start of the next run: with no live copy, a `.staging` holding any of the player's files becomes the live copy and the rest of them follow from `.old` (finished), else a marked `.old` becomes the live copy (undone). The new copy keeps its staging marker until the rest has followed, so a restore that is itself cut short (a file in `.old` held open, another crash) is resumed by the next run; until then it stops saying not to start the meter, and it refuses with what to do when a file in `.old` clashes with one the copy already has. Deletes remove the markers and manifest last, so a delete stopped by an open file leaves a folder the next run still recognises and finishes; a delete also refuses, at that moment, a folder holding anything of the player's. Builds an earlier version of this script left under stamped names (`ScarletMeter-<commit>-upstream-<release>\ScarletMeter`, without a manifest) are removed when they hold nothing beyond the current build's files or are links, and kept with a warning otherwise (move such a build's `data` folder into the copy before starting it, then run again to remove the old build); their zips, and a `ScarletMeter-win-x64.zip` from an earlier run when `-Zip` is not given, are removed only when they are zips this script wrote (they hold `ScarletMeter/ScarletMeter.portable` with its text).

With `-Zip` it writes the new copy's build, what its manifest lists plus the manifest and the marker and never the player's files, to `<output root>\ScarletMeter-win-x64.zip` after the swap (entries under `ScarletMeter/` with forward slashes, also from Windows PowerShell 5.1), written beside the old zip first and then moved over it.

The script renames or deletes only folders it made: the live and `.old` folders carry `ScarletMeter.portable`, the staging folder its own marker, and an empty folder is accepted. It refuses, changing nothing: another folder at any of the three names; a `.staging` or `.old` still holding the player's files (an interruption it could not resolve; move them into the copy and delete the folder); a folder that is or holds a link or junction; an output root that is relative, a device path (`\\?\`, `\\.\`), a file, not on a local drive, inside a portable copy, or elsewhere inside the checkout than the git-ignored default `<checkout>\portable` (so the player's data cannot end up where git sees it). A drive root as output root stays `C:\` (never `C:`, which Windows reads as that drive's current directory). It refuses while `ScarletMeter.exe` runs from the copy, `.old` or `.staging`, before building and again before the swap (the build takes minutes), and while another run updates the same output root (a named mutex per root, held for the whole run). It warns when the checkout has uncommitted changes, which are in the build though its stamped commit does not show them. The build has no PDBs or XML documentation files (`DebugType=none`, `ContinuousIntegrationBuild`), so nothing in it names the builder's folders or account. The folder rules live in `scripts/PortableFolders.ps1`; `scripts/Test-PortableFolders.ps1` checks them against throwaway folders without building or opening a window (the first copy; an update replacing the build's files and keeping the player's; a clash; the refusals and output roots; both cut-short states; a delete stopped by an open file; earlier builds and zips; the zip's contents; a running meter; a second run at once; failed swaps, including undos that fail, by making chosen moves fail), and CI runs it.

The marker makes the meter keep its data in `data` beside the exe (`PortableLayout`); delete it to use `%LocalAppData%\ScarletMeter` instead. `--data-dir` overrides both. A portable folder where the meter cannot create `data` (Program Files, a read-only drive) stops at start with a message saying so. The live copy's data is in `<checkout>\portable\ScarletMeter\data`, which git ignores: never run `git clean -x` (or `-X`) in the checkout, which deletes ignored files. Never point another tool's output into the copy with a name the build uses, and never edit `.scarlet-portable-files`: an entry it lists is replaced by the next update. To keep a copy elsewhere, unzip a `-Zip` build anywhere you can write to; that copy is updated by extracting a newer zip into the folder that holds its `ScarletMeter` folder, replacing files. `Install-Meter.ps1` refuses an install folder (or its `.previous` or `.retired`) holding `ScarletMeter.portable` or `data`, since an install would move that data aside and later delete it. Besides `data`, single-file self-extraction unpacks native libraries to `%TEMP%\.net\ScarletMeter\<hash>`, a new folder per build, for the portable and installed builds alike. CI builds the portable copy twice on every run: the first with `-Zip`, checking the zip's contents and the live copy; the second over a planted `data\settings.json` and a `notes.txt` beside the exe, which must both survive, with no `.staging`, `.old` or older zip left and one `ScarletMeter.exe`.

## Install and update

The meter is built from this checkout: there are no published Scarlet binaries and no self-updater (ARCHITECTURE.md, "Updates"). To install or reinstall:

```powershell
& "<checkout>\scripts\Install-Meter.ps1" -Shortcut
```

It runs `Build.ps1 -Configuration Release` (with the tests), publishes the self-contained single-file build into `<install>.staging`, checks it with `Test-PublishLayout.ps1`, marks it as an install (a `.scarlet-install` file) and swaps it in. The install is `%LocalAppData%\Programs\ScarletMeter`; `-InstallDirectory <absolute path on a local drive>` picks another. `-Shortcut` adds a "Scarlet Meter" Start menu shortcut; once is enough, since the install path stays the same. `-SkipTests` skips the tests. `-CheckOnly` runs the path checks below and stops, building and changing nothing.

- Close the meter first: the script refuses while `ScarletMeter.exe` runs from the install, its `.previous` or its `.retired` folder (checked before building and again before the swap).
- The old install stays in `<install>.previous` until the next install. To roll back, close the meter, rename the install folder away and rename `.previous` to the install's name.
- A failed build, publish or layout check leaves the install as it was. The swap is a series of renames; if one fails (a file held open, for example by Explorer or a virus scan), each earlier one is undone on its own, the new build stays in `<install>.staging`, and the error lists which folders exist. If an undo fails too, the error says so and which renames to make by hand.
- The data directory (`%LocalAppData%\ScarletMeter`: history, settings, logs, backgrounds, WebView2 profile) is never touched.
- The script renames or deletes only folders it made, recognised by their marker (`.scarlet-install`, or `.scarlet-install-staging` while building), and empty folders. It refuses, leaving everything as it was: any other existing folder at the install, `.previous`, `.retired` or `.staging` names, even one holding a `ScarletMeter.exe`; a folder that is, or holds, a link or junction; a `.retired` folder without an install (it may be the only copy); a path that is not on a local drive (UNC, admin shares such as `\\localhost\C$`, `\\?\` and `\\.\` forms) or has a folder name Windows would change; and an install directory that overlaps the data directory or this checkout, compared after Windows resolves links, junctions, 8.3 names and substituted drives in the install's parent and, when it exists, the install folder itself. A mistyped path therefore fails rather than moves files, unless it names a folder an earlier install made. The trailing dot or space check is defence in depth: `GetFullPath` usually strips those first, and the overlap checks then see the normalised name. A subst'd drive is resolved like a junction, but no test covers it.
- Deletes remove the marker last, so a delete stopped by an open file (in a subfolder, say) leaves a folder the next install still recognises and removes. The ownership and running-meter checks run again just before the swap, since the build takes minutes.
- `scripts/Test-InstallMeter.ps1` checks those refusals (and that owned folders still pass) against throwaway folders in the temp directory, with `-CheckOnly`. It also runs the delete and swap paths from `scripts/InstallFolders.ps1` (the installer's folder rules) on fake installs: a `.retired` with a file held open keeps its marker and blocks the swap, then goes once released, and a failed rename is undone. CI runs it.
- A warning says when the checkout has uncommitted changes: they are in the installed build, but its stamped commit does not show them.

When upstream publishes a release (a quiet line in the overlay says so, and Settings, About gives these two commands with the tag filled in), move the pin, then reinstall:

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag vX.Y.Z.N
# Skim the release notes, record the update and commit (Updating upstream, below), then:
& "<checkout>\scripts\Install-Meter.ps1"
```

## Update the portable copy in one command

```powershell
& "<checkout>\scripts\Update-Meter.ps1"                    # RATmeter's latest release
& "<checkout>\scripts\Update-Meter.ps1" -Tag v1.11.6.350   # a particular release
```

`Update-Meter.ps1` asks GitHub for RATmeter's latest published release (the tag is validated as the app validates it; `-Tag` skips the lookup). The current release is read from the pin HEAD records (`HEAD:upstream`), not from `upstream/`'s files, which a plain `git pull` leaves behind; it stops when that is already the wanted release, refuses an older one, and warns and goes ahead when the pinned commit is not a tag this checkout knows. Otherwise it refuses while the meter runs from `<checkout>\portable\ScarletMeter` (close it first; `Publish-Portable.ps1` checks again before the swap), then makes a temporary worktree of `HEAD` under the temp folder on a local branch `update/upstream-<release>`, runs that worktree's `Update-Upstream.ps1 -Tag <release>` (pin, Debug build, tests), commits the pin move on the branch, and runs its `Publish-Portable.ps1` with `<checkout>\portable` as the output (Release build, tests, then the in-place update of `portable\ScarletMeter`, keeping its data; Portable build, above). It then removes the temporary worktree; a branch it created but never committed to (a failed run) is removed too. The checkout's files and the copy's data are not touched (on a checkout whose `upstream/` was never initialised, git writes the submodule's settings to `.git/config`), and uncommitted changes in the checkout are not in the build (it warns). An existing `update/upstream-<release>` branch is reused only when it contains HEAD; otherwise the script stops and says how to recover, so a build never misses commits made since an earlier run. The commit needs git's `user.name` and `user.email`.

The pin move stays on the local branch until it reaches `main` through a pull request with the docs (ARCHITECTURE's pinned release, a WORKLOG entry, any in-game items), following the scarlet-upstream-update skill. When the build or tests fail against the new release, the portable copy stays as it was: the release changed something the engine maps, which needs a code change in `src/ScarletMeter.Engine/Upstream`.

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.5.341
```

The script targets Windows PowerShell 5.1 and pwsh; its git calls use `--quiet` so git's success messages on stderr cannot abort it in a host without a console (ISE, VS Code). It fetches upstream's tags, refuses anything that is not a release tag, moves the submodule, stages the new pin and runs the build and tests. Then skim upstream's release notes, record the update in docs/WORKLOG.md and the pinned release in docs/ARCHITECTURE.md, commit, and reinstall with `Install-Meter.ps1`. A compile error in `src/ScarletMeter.Engine/Upstream` means upstream changed an API the engine maps; the table in ARCHITECTURE.md says where each kind of change lands.

Checking what the latest release is: `gh release list -R Kuroukihime/AIon2-Dps-Meter --limit 5`, or upstream's Releases page. The meter's own release check also says so, in the overlay and in Settings, About (ARCHITECTURE.md, "Updates").

## CI

`.github/workflows/ci.yml` runs on every push and pull request on `windows-latest`:

1. Checkout with submodules. actions/checkout clones the submodule with `--depth=1`, which brings the pinned commit and only the tags that follow upstream's default-branch tip, not the pinned release's tag.
2. Fetch the pinned release tag: `git ls-remote` finds the release tag naming the pinned commit, and that tag alone is fetched (depth 1); each git call is tried up to three times. Finding none fails the run, since the pin must be a release. The tag goes into `SCARLET_EXPECTED_UPSTREAM_RELEASE`, and `BuildInfoTests` checks that the build stamped exactly it rather than the commit fallback.
3. The changelog has the app's version (`src/Directory.Build.props`): `scripts/Get-ChangelogSection.ps1`, the script the release workflow uses, must find its section in `CHANGELOG.md`; then `scripts/Test-ChangelogSection.ps1` checks the script's refusals and section bounds on throwaway changelogs.
4. .NET 10, then `scripts/Build.ps1 -Configuration Release`.
5. `scripts/Test-InstallMeter.ps1`: the installer's refusals, building and moving nothing.
6. `scripts/Install-Meter.ps1 -SkipTests` twice into the runner's temp directory, under pwsh: the self-contained single-file publish, `Test-PublishLayout.ps1` on the published output, the install marker and the folder swap. The second install must accept the first by its marker and keep it as `.previous`; the step checks both carry the marker and that no staging or `.retired` folder is left.

CI does not exercise capture, WebView rendering or the window lifecycle, and does not launch the meter.

## Releases

Releases are built on GitHub's runners, never on a PC, so a zip can hold nothing of a developer's paths, account or data.

1. On a branch: set `<Version>` in `src/Directory.Build.props` (0.x releases are pre-releases); in `CHANGELOG.md` turn `## [Unreleased]` into `## [<version>] - <yyyy-mm-dd>` (the day it will be published, the maintainer's local day as the WORKLOG dates it) above a new, empty `## [Unreleased]`, and at the end point `[Unreleased]` at `compare/v<version>...HEAD` and add `[<version>]: .../compare/v<previous>...v<version>` (`ChangelogTests` checks both, and CI refuses a version without its section); write `docs/release-notes/v<version>.md` (a few highlights, the Npcap step, the risk note: the release workflow adds the version's changelog section after it, under "What's changed in <version>", so it need not repeat the list), and, after any package change, run `scripts/Write-ThirdPartyNotices.ps1`, which rewrites the package table and the packages' own licence files in `licenses/packages` (`ThirdPartyNoticesTests` fails until both are current). Merge through a PR as usual.
2. Tag the merged commit on `main` and push the tag: `git -C "<checkout>" tag v<version> <commit>`, then `git -C "<checkout>" push origin v<version>`.
3. `.github/workflows/release.yml`, job `build` (read-only token): checks the tag is `v<major>.<minor>.<patch>`, its commit is on `main`, it equals `v` + the version, its notes exist and `CHANGELOG.md` has its section (`scripts/Get-ChangelogSection.ps1`); runs `Build.ps1` (restore, build, tests; not CI's installer, portable-folder or source-archive steps, which ran on the merged commit); runs `Publish-Portable.ps1 -Zip` and `Write-SourceArchive.ps1` into the runner's temp directory; checks the program zip (the program, its marker, `README.txt`, `LICENSE.txt`, `NOTICE.txt`, `THIRD-PARTY-NOTICES.md`, the `licenses` folder with the runtime's notices, no data, no staging marker) and the source zip (the checkout and `upstream/`); writes each zip's SHA-256. The release's notes are its notes file followed by its changelog section. Job `draft` (the only one with write access, no project code) creates a **draft** release with `ScarletMeter-<version>-win-x64.zip`, `ScarletMeter-<version>-source.zip` and their `.sha256` files.
4. Download the draft's zips, check their checksums, unzip them, and search their files for anything that should not be there (an account name, a home path, an e-mail address); then publish the draft (`gh release edit v<version> --draft=false`).

A tag pushed by mistake: delete the draft and the tag (`gh release delete v<version> --cleanup-tag`) before publishing.

# Development

Requires Windows, the .NET 10 SDK, the WebView2 runtime (ships with Windows 11), Git, and for live capture [Npcap](https://npcap.com) installed with "Install Npcap in WinPcap API-compatible Mode". Tests, `--no-capture` and `--demo` do not need Npcap.

## Clone and build

`<checkout>` in the commands below is the absolute path of the clone.

```powershell
git clone --recurse-submodules https://github.com/harrynguon/ScarletMeter.git "<checkout>"
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
- `--demo` opens it without Npcap on synthetic fights, for UI work: a party of four ("Demo Gladiator", the user, "Demo Cleric", "Demo Ranger", "Demo Elementalist" with a fire spirit) fights one boss after another, each fight lasting about 60 to 90 seconds with crits, back attacks, DoTs, summon damage, buffs, ping and falling boss HP, then saved to history; the next starts ten seconds later. Capture reads as running on adapter "Demo". Demo fights go to `demo-history.db`, never to the real `combat-history.db`. It cannot be combined with `--no-capture`. It exercises fight tracking, details and history, not packet decoding (docs/ARCHITECTURE.md, "Demo mode").
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

When upstream publishes a release (the meter will show a notice once the UI wires `UpstreamReleaseChecker` in; until then, see below), move the pin, then reinstall:

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag vX.Y.Z.N
# Skim the release notes, record the update and commit (Updating upstream, below), then:
& "<checkout>\scripts\Install-Meter.ps1"
```

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.5.341
```

The script targets Windows PowerShell 5.1 and pwsh; its git calls use `--quiet` so git's success messages on stderr cannot abort it in a host without a console (ISE, VS Code). It fetches upstream's tags, refuses anything that is not a release tag, moves the submodule, stages the new pin and runs the build and tests. Then skim upstream's release notes, record the update in docs/WORKLOG.md and the pinned release in docs/ARCHITECTURE.md, commit, and reinstall with `Install-Meter.ps1`. A compile error in `src/ScarletMeter.Engine/Upstream` means upstream changed an API the engine maps; the table in ARCHITECTURE.md says where each kind of change lands.

Checking what the latest release is: `gh release list -R Kuroukihime/AIon2-Dps-Meter --limit 5`, or upstream's Releases page. Once the UI wires it in, the meter's own release check says so (ARCHITECTURE.md, "Updates").

## CI

`.github/workflows/ci.yml` runs on every push and pull request on `windows-latest`:

1. Checkout with submodules. actions/checkout clones the submodule with `--depth=1`, which brings the pinned commit and only the tags that follow upstream's default-branch tip, not the pinned release's tag.
2. Fetch the pinned release tag: `git ls-remote` finds the release tag naming the pinned commit, and that tag alone is fetched (depth 1); each git call is tried up to three times. Finding none fails the run, since the pin must be a release. The tag goes into `SCARLET_EXPECTED_UPSTREAM_RELEASE`, and `BuildInfoTests` checks that the build stamped exactly it rather than the commit fallback.
3. .NET 10, then `scripts/Build.ps1 -Configuration Release`.
4. `scripts/Test-InstallMeter.ps1`: the installer's refusals, building and moving nothing.
5. `scripts/Install-Meter.ps1 -SkipTests` twice into the runner's temp directory, under pwsh: the self-contained single-file publish, `Test-PublishLayout.ps1` on the published output, the install marker and the folder swap. The second install must accept the first by its marker and keep it as `.previous`; the step checks both carry the marker and that no staging or `.retired` folder is left.

CI does not exercise capture, WebView rendering or the window lifecycle, and does not launch the meter.

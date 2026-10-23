# Development

Requires Windows, the .NET 10 SDK, the WebView2 runtime (ships with Windows 11), Git, and for live capture [Npcap](https://npcap.com) installed with "Install Npcap in WinPcap API-compatible Mode". Tests and `--no-capture` do not need Npcap.

## Clone and build

```powershell
git clone --recurse-submodules https://github.com/harrynguon/ScarletMeter.git
```

If the clone was made without `--recurse-submodules`, `upstream/` is empty: run `git submodule update --init` from the checkout. `scripts/Build.ps1` reports this.

```powershell
# From any directory; the script derives every path from its own location.
& "<checkout>\scripts\Build.ps1"                 # restore (NuGet audit as errors), build, test
& "<checkout>\scripts\Build.ps1" -Configuration Release -SkipTests
```

The script uses the `dotnet` on PATH and insists on a .NET 10 SDK. When another SDK is first on PATH, point `SCARLET_DOTNET` at the right `dotnet.exe` (an absolute path) for the session instead of editing PATH.

## Run

```powershell
dotnet run --project "<checkout>\src\ScarletMeter.App\ScarletMeter.App.csproj" -- --no-capture
```

Switches: `--no-capture` opens the window without Npcap (status reads "Capture is off"); `--data-dir <absolute path>` moves the history database and logs for a session, for example to keep a test run away from real data. Anything else is rejected with a dialog.

Data and logs: `%LocalAppData%\ScarletMeter` (`combat-history.db`, `logs\scarlet-<date>.log`). The log is where "[CAPTURE] !!! LOCKED !!!" appears once upstream's capture finds the game's connection.

The apphost needs a .NET 10 runtime it can find: an installed .NET 10 Desktop Runtime or SDK, or `DOTNET_ROOT` set to one (an absolute path). Without it `ScarletMeter.exe` shows the .NET install prompt and exits with 0x80008096. A self-contained publish carries its own runtime.

## Publish

```powershell
dotnet publish "<checkout>\src\ScarletMeter.App\ScarletMeter.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "<absolute output directory>"
```

Keep the whole published folder: the game-data JSON files (`GameData\Assets`) and `wwwroot` sit beside `ScarletMeter.exe`. CI runs the same publish as a smoke check. No release process exists yet; see docs/SECURITY.md before publishing a binary.

## Updating upstream

```powershell
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.4.334
```

The script fetches upstream's tags, refuses anything that is not a release tag, moves the submodule, stages the new pin and runs the build and tests. Then skim upstream's release notes, record the update in docs/WORKLOG.md and the pinned release in docs/ARCHITECTURE.md, and commit. A compile error in `src/ScarletMeter.Engine/Upstream` means upstream changed an API the engine maps; the table in ARCHITECTURE.md says where each kind of change lands.

Checking what the latest release is: `gh release list -R Kuroukihime/AIon2-Dps-Meter --limit 5`, or upstream's Releases page.

## CI

`.github/workflows/ci.yml` runs on every push and pull request on `windows-latest`: checkout with submodules, .NET 10, `scripts/Build.ps1 -Configuration Release`, then a self-contained single-file publish. CI does not exercise capture, WebView rendering or the window lifecycle.

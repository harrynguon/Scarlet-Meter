<#
.SYNOPSIS
Builds Scarlet Meter and keeps one ready-to-run portable copy up to date: <output root>\ScarletMeter\ScarletMeter.exe.

.DESCRIPTION
Runs scripts\Build.ps1 -Configuration Release (restore with audit as errors, build, tests), publishes the
self-contained single-file win-x64 build (the .NET runtime is bundled, so nothing needs installing but Npcap) into
"<output root>\ScarletMeter.staging", checks it with scripts\Test-PublishLayout.ps1, lists its files in a manifest
and adds the ScarletMeter.portable marker that makes the meter keep its settings, history and logs in a "data"
folder beside the exe.

It then makes that build the live copy, "<output root>\ScarletMeter", so there is one copy and one ScarletMeter.exe
to run, always the latest. The build's files are replaced; everything else in the copy (the data folder, and
anything the player put beside it) moves into the new build: the live folder is renamed aside, the player's files
move into the new build, the new build takes the live folder's name, and the previous build's files are deleted.
Every step is a rename on one drive; if one fails, the earlier ones are undone and the message says where
everything is, and an update that was cut short is finished or undone at the start of the next run. Nothing of the
player's is ever deleted, and only folders this script made are renamed or deleted (scripts\PortableFolders.ps1).
Refuses while ScarletMeter.exe runs from the copy (close the meter first) and while another run updates the same
output root.

With -Zip it also writes the build, without the player's files, to "<output root>\ScarletMeter-win-x64.zip",
replacing the previous zip: for handing the meter on, not needed to run it. Without -Zip a zip an earlier run left
there is removed, so nothing older than the live copy stays. Builds that earlier versions of this script left under
stamped names (ScarletMeter-<commit>-upstream-<release>) are removed when they hold nothing but a build's files.

The build carries no debug symbols or API documentation files, so it names nothing of the builder's folders. Every
path derives from this script's location; an output root given must be absolute. Targets Windows PowerShell 5.1 and pwsh.

.PARAMETER OutputRoot
Absolute folder that holds the live copy. Default: "<checkout>\portable", which git ignores. Elsewhere inside the
checkout is refused, so the player's data cannot end up where git sees it, and so is a folder inside a portable copy.

.PARAMETER SkipTests
Builds without running the tests (CI, where they ran in an earlier step).

.PARAMETER Zip
Also write ScarletMeter-win-x64.zip, the build without the player's files, replacing the previous one.

.EXAMPLE
& "<checkout>\scripts\Publish-Portable.ps1"
#>
[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$SkipTests,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src\ScarletMeter.App\ScarletMeter.App.csproj'
. (Join-Path $PSScriptRoot 'PortableFolders.ps1')

$defaultRoot = Join-Path $repoRoot 'portable'
$OutputRoot = Resolve-PortableRoot $(if ($OutputRoot) { $OutputRoot } else { $defaultRoot })
# Inside the checkout only the git-ignored default is allowed: a copy run from anywhere else in it would write the
# player's history, logs and settings where git add could pick them up (AGENTS.md, "Security").
$checkout = Get-FinalPath $repoRoot
$defaultFinal = Resolve-FinalPath $defaultRoot
if ((Test-PathWithin $OutputRoot $checkout) -and -not (Test-PathWithin $OutputRoot $defaultFinal)) {
    throw "OutputRoot $OutputRoot is inside this checkout; use the default ($defaultFinal) or a folder outside it."
}
$folders = New-PortableFolders $OutputRoot
$running = { Assert-MeterNotRunningFrom @($folders.Live, $folders.Old, $folders.Staging) 'run Publish-Portable.ps1 again' }

# First line git prints for these arguments in $Directory, or $null when git, the repository or its .git entry is
# missing. Never throws: stderr is discarded with the preference relaxed, because Windows PowerShell 5.1 turns
# redirected native stderr into a terminating error under Stop. (The same helper as Install-Meter.ps1's.)
function Get-GitLine([string]$Directory, [string[]]$Arguments) {
    if (-not (Test-Path -LiteralPath (Join-Path $Directory '.git'))) { return $null }
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { return $null }
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $git.Source -C $Directory @Arguments 2>$null)
        if ($LASTEXITCODE -eq 0 -and $output.Count) { return ([string]$output[0]).Trim() }
        return $null
    }
    catch { return $null }
    finally { $ErrorActionPreference = $preference }
}

$lock = Enter-PortableLock $OutputRoot
try {
    # --- Before building: the meter is closed, a cut-short update is resolved, the folders are ours ---

    & $running
    if (Test-Path -LiteralPath $OutputRoot) {
        $restored = Restore-InterruptedPortableUpdate $folders
        if ($restored) { Write-Host $restored }
    }
    Assert-PortableFoldersOwned $folders
    Clear-PortableLeftovers $folders
    if (Get-GitLine $repoRoot @('status', '--porcelain')) { # untracked sources are compiled in too, as Install-Meter.ps1 counts them
        Write-Warning 'This checkout has uncommitted changes: they are in the build, but its stamped commit does not show them.'
    }

    # --- Build into staging, check, list, mark ---

    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -SkipTests:$SkipTests

    $dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
    [void][IO.Directory]::CreateDirectory($folders.Staging)
    [IO.File]::WriteAllText((Join-Path $folders.Staging $PortableStagingMarker), $PortableStagingMarkerText)
    try {
        # global.json (the SDK band) is discovered from the working directory, so anchor it as Build.ps1 does.
        Push-Location -LiteralPath $repoRoot
        try {
            # Meant to be handed on, so no debug symbols (their paths name the builder's folders and account) and no
            # API documentation files: a deterministic build without PDBs or XML docs.
            & $dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:DebugType=none -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true `
                -p:PublishDocumentationFiles=false -p:PublishReferencesDocumentationFiles=false `
                --output $folders.Staging
            if ($LASTEXITCODE) { throw 'Publish failed.' }
        }
        finally {
            Pop-Location
        }
        & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $folders.Staging
        Assert-NoLinksInside $folders.Staging
        Write-PortableManifest $folders.Staging
        [IO.File]::WriteAllText((Join-Path $folders.Staging $PortableMarker), $PortableMarkerText + "`r`n")
    }
    catch {
        # The live copy is untouched; drop the half-made staging folder (nothing of the player's is in it yet).
        try { Remove-PortableFolder $folders.Staging $null }
        catch { Write-Warning "Could not remove $($folders.Staging); the next run removes it. $($_.Exception.Message)" }
        throw
    }

    # --- Make it the live copy, carrying the player's files over ---

    # The build took minutes: the meter may have been started since the first check.
    & $running
    Assert-PortableFoldersOwned $folders
    Invoke-PortableSwap $folders
    $build = Read-PortableManifest $folders.Live

    # Only the latest build is kept: a zip from an earlier run would now be older than the live copy.
    if ($Zip) { Write-PortableZip $folders.Live $folders.Zip }
    elseif (Test-PortableZip $folders.Zip) {
        Remove-Item -LiteralPath $folders.Zip -Force
        Write-Host "Removed $($folders.Zip), from an earlier build; run with -Zip for a zip of this one."
    }
    $legacy = Remove-LegacyPortableBuilds $OutputRoot $build
    if ($legacy.Removed.Count) { Write-Host "Removed earlier builds: $($legacy.Removed -join ', ')" }
    if ($legacy.Kept.Count) {
        Write-Warning ("Kept earlier builds that hold more than a build's files: $($legacy.Kept -join ', '). Your settings and history may be in their ScarletMeter\data folder: " +
            "before starting the new copy, move that data folder (and any other files of yours) into $($folders.Live), then run this script again to remove the old build.")
    }

    $exe = Join-Path $folders.Live 'ScarletMeter.exe'
    Write-Host "Portable copy: $exe"
    if ($Zip) { Write-Host "Zip:           $($folders.Zip)" }
    $kept = @(Get-PlayerEntries $folders.Live $build)
    if ($kept.Count) { Write-Host "Kept your files beside it: $($kept -join ', ')." }
}
finally {
    Exit-PortableLock $lock
}

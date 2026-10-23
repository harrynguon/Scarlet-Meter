<#
.SYNOPSIS
Builds Scarlet Meter from this checkout and installs it for the current user, keeping the previous install for rollback.

.DESCRIPTION
Runs scripts\Build.ps1 -Configuration Release (restore with audit as errors, build, tests), publishes a
self-contained single-file win-x64 build into "<install dir>.staging" beside the install directory, checks it
with scripts\Test-PublishLayout.ps1, marks it as an install (a ".scarlet-install" file with fixed content), then
swaps folders: the current install becomes "<install dir>.previous" (replacing the one kept by the install
before) and the staging folder becomes the install. To roll back by hand, close the meter, rename the install
folder away and rename "<install dir>.previous" to the install name.

The swap is a series of renames; if one fails, each earlier rename is undone separately and the error says
which folders are where. Refuses, before building and again just before the swap, while a ScarletMeter.exe runs
from the install directory or its .previous or .retired folder (close the meter first), and repeats the
ownership checks there too.

What it will rename or delete: only folders this script made, recognised by their marker file (".scarlet-install",
or ".scarlet-install-staging" for a staging folder), or empty folders. Any other existing folder at those names,
a folder holding a link or junction, a link at those names, an install directory that overlaps the meter's data
directory or this checkout (compared after resolving links, short names and substituted drives), a path that is
not on a local drive (UNC, admin shares, \\?\ and \\.\ forms) and a ".retired" folder without an install are
refused, with nothing touched. Deletes keep the marker until last, so a delete stopped by an open file leaves a
folder the next install still recognises. It never touches the data directory (%LocalAppData%\ScarletMeter).
Every path derives from this script's location or the platform's per-user folders. The folder rules live in
scripts\InstallFolders.ps1. Targets Windows PowerShell 5.1 and pwsh.

.PARAMETER InstallDirectory
Absolute path on a local drive. Default: %LocalAppData%\Programs\ScarletMeter.

.PARAMETER Shortcut
Also create or refresh a "Scarlet Meter" shortcut to the installed exe in the current user's Start menu.
The install path stays the same across installs, so the shortcut keeps working after later updates.

.PARAMETER SkipTests
Builds without running the tests (CI, where they ran in an earlier step).

.PARAMETER CheckOnly
Runs the path checks and refusals, then stops without building, publishing or touching any folder
(scripts\Test-InstallMeter.ps1 uses it).

.EXAMPLE
& "<checkout>\scripts\Install-Meter.ps1" -Shortcut
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$Shortcut,
    [switch]$SkipTests,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src\ScarletMeter.App\ScarletMeter.App.csproj'
. (Join-Path $PSScriptRoot 'InstallFolders.ps1')

# First line git prints for these arguments in $Directory, or $null when git, the repository or its .git entry
# is missing (the same guard as BuildStamp.targets: without it git would walk up to an enclosing repository).
# Never throws: stderr is discarded with the preference relaxed, because Windows PowerShell 5.1 turns
# redirected native stderr into a terminating error under Stop.
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

# --- Paths, and the refusals that need no build ---

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ($localAppData -notmatch '^[A-Za-z]:\\') { throw 'The platform did not provide a per-user data folder (LocalApplicationData) on a local drive.' }
if (-not $InstallDirectory) { $InstallDirectory = Join-Path $localAppData 'Programs\ScarletMeter' }
# A local drive path (C:\...) only. UNC paths, admin shares (\\localhost\C$) and device paths (\\?\, \\.\) are
# refused: they name the same folders in forms the checks below would have to undo first.
if ($InstallDirectory -notmatch '^[A-Za-z]:[\\/]') {
    throw "InstallDirectory must be an absolute path on a local drive (such as C:\...), not `"$InstallDirectory`"."
}
# A colon after the drive names a stream, and wildcards cannot be folder names (.NET Framework would also
# reject both with a less helpful message).
if ($InstallDirectory.Substring(2) -match '[:*?"<>|]') {
    throw "InstallDirectory names a folder Windows cannot create (`"$InstallDirectory`")."
}
$requested = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/')
$leaf = [IO.Path]::GetFileName($requested)
if (-not $leaf) { throw "InstallDirectory must be a folder, not the root `"$InstallDirectory`"." }
# Windows drops a trailing dot or space when it creates a folder; refuse such names, so the folder created is
# the folder checked. Defence in depth: GetFullPath usually strips them first, and the overlap checks below then
# see the normalised name.
foreach ($component in $requested.Substring(3).Split('\')) {
    if ($component -match '[.\s]$') {
        throw "InstallDirectory has a folder name Windows would change (`"$component`")."
    }
}

# The install folder in final form: its parent resolved through any links, and the folder itself too when it
# exists and is not a link (a link is refused below), so an 8.3 name for it matches the running meter's path and
# its siblings get their real names. The siblings then sit beside it: .staging, .previous, .retired.
$parentFinal = Resolve-FinalPath (Split-Path -Parent $requested)
$install = "$parentFinal\$leaf"
if ((Test-Path -LiteralPath $install -PathType Container) -and
    -not ((Get-Item -LiteralPath $install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    $install = Get-FinalPath $install
}
$set = New-InstallFolders $install
$allFolders = @($set.Install, $set.Staging, $set.Previous, $set.Retired)

# The meter's default data directory, as EngineOptions.DefaultDataDirectory defines it, resolved the same way
# (it may itself be a junction to elsewhere). A data directory moved with --data-dir cannot be known here.
$dataDirectory = Resolve-FinalPath (Join-Path $localAppData 'ScarletMeter')
$checkout = Get-FinalPath $repoRoot
foreach ($folder in $allFolders) {
    if ((Test-PathWithin $folder $dataDirectory) -or (Test-PathWithin $dataDirectory $folder)) {
        throw "$folder overlaps the meter's data directory $dataDirectory, which an install must never touch. Nothing was changed; choose another -InstallDirectory."
    }
    if ((Test-PathWithin $folder $checkout) -or (Test-PathWithin $checkout $folder)) {
        throw "$folder overlaps this checkout ($checkout). Nothing was changed; install outside it."
    }
}
Assert-InstallFoldersOwned $set
Assert-MeterNotRunningFrom @($set.Install, $set.Previous, $set.Retired)

if ($CheckOnly) {
    Write-Host "Install checks passed for $($set.Install); nothing was built, installed or changed."
    return
}

# --- Build, publish into staging, check the layout, mark ---

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -SkipTests:$SkipTests

$dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
[void][IO.Directory]::CreateDirectory($parentFinal)
try {
    if (Test-Path -LiteralPath $set.Staging) { Remove-OwnedFolder $set.Staging }
    [void][IO.Directory]::CreateDirectory($set.Staging)
    # Marks the folder as this script's from the start, so a failed or interrupted run can clear it.
    [IO.File]::WriteAllText((Join-Path $set.Staging $StagingMarker), $StagingMarkerText)

    # global.json (the SDK band) is discovered from the working directory, so anchor it as Build.ps1 does.
    Push-Location -LiteralPath $repoRoot
    try {
        & $dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $set.Staging
        if ($LASTEXITCODE) { throw 'Publish failed.' }
    }
    finally {
        Pop-Location
    }
    & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $set.Staging
    Assert-NoLinksInside $set.Staging
}
catch {
    # The current install is untouched; drop the half-made staging folder.
    if (Test-Marker $set.Staging $StagingMarker $StagingMarkerText) {
        try { Remove-OwnedFolder $set.Staging }
        catch { Write-Warning "Could not remove $($set.Staging); it stays marked, and the next install removes it. $($_.Exception.Message)" }
    }
    throw
}
# The checked publish becomes an install: the marker that lets the next install replace it, then no staging mark.
[IO.File]::WriteAllText((Join-Path $set.Staging $InstallMarker), $InstallMarkerText)
Remove-Item -LiteralPath (Join-Path $set.Staging $StagingMarker)

# --- Swap ---

# The build took minutes: the meter may have been started, or a folder replaced, since the first checks.
Assert-InstallFoldersOwned $set
Assert-MeterNotRunningFrom @($set.Install, $set.Previous, $set.Retired)
Invoke-FolderSwap $set

$exe = Join-Path $set.Install 'ScarletMeter.exe'
if ($Shortcut) {
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if (-not $programs) { throw 'The platform did not provide a Start menu folder.' }
    $link = Join-Path $programs 'Scarlet Meter.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcutFile = $shell.CreateShortcut($link)
        $shortcutFile.TargetPath = $exe
        $shortcutFile.WorkingDirectory = $set.Install
        $shortcutFile.Description = 'Scarlet Meter, a DPS meter for AION 2'
        $shortcutFile.Save()
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
    Write-Host "Start menu shortcut: $link"
}

# Reports what was installed: the values BuildStamp.targets stamped into the exe, read again from git here.
$upstreamDirectory = Join-Path $repoRoot 'upstream'
$upstreamRelease = Get-GitLine $upstreamDirectory @('describe', '--tags', '--exact-match')
if (-not $upstreamRelease) { $upstreamRelease = Get-GitLine $upstreamDirectory @('rev-parse', '--short', 'HEAD') }
$scarletCommit = Get-GitLine $repoRoot @('rev-parse', '--short', 'HEAD')
if (Get-GitLine $repoRoot @('status', '--porcelain')) {
    Write-Warning 'The checkout has uncommitted changes: the installed build includes them, its stamped commit does not show them.'
}
Write-Host ''
Write-Host "Installed Scarlet Meter (upstream $upstreamRelease, Scarlet $scarletCommit): $exe"
if (Test-Path -LiteralPath $set.Previous) {
    Write-Host "The previous install stays in $($set.Previous) until the next install. To roll back, close the meter and swap the two folders' names."
}

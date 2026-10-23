<#
.SYNOPSIS
Builds Scarlet Meter from this checkout and installs it for the current user, keeping the previous install for rollback.

.DESCRIPTION
Runs scripts\Build.ps1 -Configuration Release (restore with audit as errors, build, tests), publishes a
self-contained single-file win-x64 build into "<install dir>.staging" beside the install directory, checks it
with scripts\Test-PublishLayout.ps1, then swaps folders: the current install becomes "<install dir>.previous"
(replacing the one kept by the install before) and the staging folder becomes the install. To roll back by
hand, close the meter, rename the install folder away and rename "<install dir>.previous" to the install name.

The swap is a series of renames that are undone if one fails, so a failure leaves the current install working.
Refuses, before building and again before the swap, while a ScarletMeter.exe runs from the install directory
or its .previous folder (close the meter first). Refuses an install directory that overlaps the meter's data
directory or this checkout, and any folder it would replace or delete that is not a Scarlet Meter install
(absent, empty, or holding ScarletMeter.exe), so a mistyped path cannot move someone's files. It never touches
the data directory (%LocalAppData%\ScarletMeter: history, logs, WebView2 profile). Every path derives from this
script's location or the platform's per-user folders. Targets Windows PowerShell 5.1 and pwsh.

.PARAMETER InstallDirectory
Absolute path of the install. Default: %LocalAppData%\Programs\ScarletMeter.

.PARAMETER Shortcut
Also create or refresh a "Scarlet Meter" shortcut to the installed exe in the current user's Start menu.
The install path stays the same across installs, so the shortcut keeps working after later updates.

.PARAMETER SkipTests
Builds without running the tests (CI, where they ran in an earlier step).

.EXAMPLE
& "<checkout>\scripts\Install-Meter.ps1" -Shortcut
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$Shortcut,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src\ScarletMeter.App\ScarletMeter.App.csproj'
$stagingMarker = '.scarlet-install-staging'

# Full form of an absolute path without a trailing separator, for comparisons and messages.
function Get-NormalizedPath([string]$Path) {
    [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

# True when $Path is $Container or lies inside it. Windows paths compare case-insensitively.
function Test-PathWithin([string]$Path, [string]$Container) {
    $Path.Equals($Container, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Container + '\', [StringComparison]::OrdinalIgnoreCase)
}

# Refuses a folder this script would rename or delete unless it is absent, empty, or a Scarlet Meter install,
# publish or staging folder. Refuses links too: deleting through one would reach its target.
function Assert-ReplaceableFolder([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer) { throw "$Path is a file, not a Scarlet Meter folder. Move it away first." }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "$Path is a link or junction. Replace it with a plain folder first."
    }
    $isMeter = (Test-Path -LiteralPath (Join-Path $Path 'ScarletMeter.exe') -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $Path $stagingMarker) -PathType Leaf)
    $isEmpty = -not (Get-ChildItem -LiteralPath $Path -Force | Select-Object -First 1)
    if (-not ($isMeter -or $isEmpty)) {
        throw "$Path exists and is not a Scarlet Meter install (no ScarletMeter.exe). Choose another -InstallDirectory or move that folder away."
    }
}

# Refuses while a ScarletMeter.exe runs from one of $Folders. A process whose location cannot be read (an
# elevated meter seen from a non-elevated shell) might be running from there, so it refuses too.
function Assert-MeterNotRunningFrom([string[]]$Folders) {
    $blocking = @(foreach ($process in @(Get-Process -Name 'ScarletMeter' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { $path = $null }
        if (-not $path) {
            "process $($process.Id), location unreadable (perhaps elevated)"
        }
        elseif (@($Folders | Where-Object { Test-PathWithin (Get-NormalizedPath $path) $_ }).Count) {
            "process $($process.Id), $path"
        }
    })
    if ($blocking.Count) {
        throw "Scarlet Meter is running ($($blocking -join '; ')). Close it, then run the install again."
    }
}

# Moves staging into place: .previous -> .retired, install -> .previous, staging -> install, then deletes
# .retired. Renames are atomic on one volume; if one fails, the earlier ones are undone and the error rethrown,
# so the current install keeps working. A failed delete only leaves .retired for the next install to remove.
function Invoke-FolderSwap {
    if (Test-Path -LiteralPath $retired) { [IO.Directory]::Delete($retired, $true) }
    $previousRetired = $false
    $installKeptAsPrevious = $false
    try {
        if (Test-Path -LiteralPath $previous) {
            [IO.Directory]::Move($previous, $retired)
            $previousRetired = $true
        }
        if (Test-Path -LiteralPath $install) {
            [IO.Directory]::Move($install, $previous)
            $installKeptAsPrevious = $true
        }
        [IO.Directory]::Move($staging, $install)
    }
    catch {
        $reason = $_.Exception.Message
        if ($installKeptAsPrevious) { [IO.Directory]::Move($previous, $install) }
        if ($previousRetired) { [IO.Directory]::Move($retired, $previous) }
        throw "Swapping the folders failed, so the current install was left as it was and the new build stays in $staging. A file in one of the folders may be open (the meter, an Explorer window, a virus scan); close it and run the install again. $reason"
    }
    if (Test-Path -LiteralPath $retired) {
        try { [IO.Directory]::Delete($retired, $true) }
        catch { Write-Warning "Could not remove $retired; the next install removes it. $($_.Exception.Message)" }
    }
}

# First line git prints for these arguments in $Directory, or $null when git or the repository is missing.
# Never throws: stderr is discarded with the preference relaxed, because Windows PowerShell 5.1 turns
# redirected native stderr into a terminating error under Stop.
function Get-GitLine([string]$Directory, [string[]]$Arguments) {
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
if ($localAppData -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') { throw 'The platform did not provide a per-user data folder (LocalApplicationData).' }
if (-not $InstallDirectory) { $InstallDirectory = Join-Path $localAppData 'Programs\ScarletMeter' }
# A drive path (C:\...) or a UNC path; Path.IsPathFullyQualified is missing from Windows PowerShell 5.1's .NET.
if ($InstallDirectory -notmatch '^(?:[A-Za-z]:[\\/]|\\\\|//)') {
    throw "InstallDirectory must be an absolute path, not `"$InstallDirectory`"."
}
$install = Get-NormalizedPath $InstallDirectory
if (-not [IO.Path]::GetFileName($install)) { throw "InstallDirectory must be a folder, not the root `"$InstallDirectory`"." }
$staging = "$install.staging"
$previous = "$install.previous"
$retired = "$install.retired"
$folders = @($install, $staging, $previous, $retired)

# The meter's default data directory, as EngineOptions.DefaultDataDirectory defines it. A data directory moved
# with --data-dir is the player's own choice and cannot be known here.
$dataDirectory = Get-NormalizedPath (Join-Path $localAppData 'ScarletMeter')
foreach ($folder in $folders) {
    if ((Test-PathWithin $folder $dataDirectory) -or (Test-PathWithin $dataDirectory $folder)) {
        throw "$folder overlaps the meter's data directory $dataDirectory, which an install must never touch. Choose another -InstallDirectory."
    }
    if ((Test-PathWithin $folder $repoRoot) -or (Test-PathWithin $repoRoot $folder)) {
        throw "$folder overlaps this checkout ($repoRoot). Install outside it."
    }
}
foreach ($folder in $folders) { Assert-ReplaceableFolder $folder }
Assert-MeterNotRunningFrom @($install, $previous, $retired)

# --- Build, publish into staging, check the layout ---

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -SkipTests:$SkipTests

$dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $install))
try {
    if (Test-Path -LiteralPath $staging) { [IO.Directory]::Delete($staging, $true) }
    [void][IO.Directory]::CreateDirectory($staging)
    # Marks the folder as this script's until the checked publish is in it, so a failed or interrupted run
    # can clear it.
    Set-Content -LiteralPath (Join-Path $staging $stagingMarker) -Value 'Scarlet Meter install staging folder.'

    # global.json (the SDK band) is discovered from the working directory, so anchor it as Build.ps1 does.
    Push-Location -LiteralPath $repoRoot
    try {
        & $dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $staging
        if ($LASTEXITCODE) { throw 'Publish failed.' }
    }
    finally {
        Pop-Location
    }
    & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $staging
}
catch {
    # The current install is untouched; drop the half-made staging folder.
    if (Test-Path -LiteralPath (Join-Path $staging $stagingMarker)) {
        try { [IO.Directory]::Delete($staging, $true) }
        catch { Write-Warning "Could not remove $staging; the next install removes it. $($_.Exception.Message)" }
    }
    throw
}
Remove-Item -LiteralPath (Join-Path $staging $stagingMarker)

# --- Swap ---

Assert-MeterNotRunningFrom @($install, $previous, $retired) # the meter may have been started during the build
Invoke-FolderSwap

$exe = Join-Path $install 'ScarletMeter.exe'
if ($Shortcut) {
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if (-not $programs) { throw 'The platform did not provide a Start menu folder.' }
    $link = Join-Path $programs 'Scarlet Meter.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcutFile = $shell.CreateShortcut($link)
        $shortcutFile.TargetPath = $exe
        $shortcutFile.WorkingDirectory = $install
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
if (Test-Path -LiteralPath $previous) {
    Write-Host "The previous install stays in $previous until the next install. To roll back, close the meter and swap the two folders' names."
}

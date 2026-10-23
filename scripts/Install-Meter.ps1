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
which folders are where. Refuses, before building and again before the swap, while a ScarletMeter.exe runs from
the install directory or its .previous or .retired folder (close the meter first).

What it will rename or delete: only folders this script made, recognised by their marker file (".scarlet-install",
or ".scarlet-install-staging" for a staging folder), or empty folders. Any other existing folder at those names,
a folder holding a link or junction, a link at those names, an install directory that overlaps the meter's data
directory or this checkout (compared after resolving links, short names and substituted drives), a path that is
not on a local drive (UNC, admin shares, \\?\ and \\.\ forms) and a ".retired" folder without an install are
refused, with nothing touched. It never touches the data directory (%LocalAppData%\ScarletMeter). Every path
derives from this script's location or the platform's per-user folders. Targets Windows PowerShell 5.1 and pwsh.

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
$installMarker = '.scarlet-install'
$installMarkerText = 'Scarlet Meter install, made by scripts\Install-Meter.ps1. The next install replaces this folder; keep nothing else in it.'
$stagingMarker = '.scarlet-install-staging'
$stagingMarkerText = 'Scarlet Meter staging folder, made by scripts\Install-Meter.ps1 and removed by it.'

# The final path of an existing file or folder: links, junctions, 8.3 short names and substituted drives
# resolved by Windows (GetFinalPathNameByHandle), without the \\?\ prefix or a trailing separator. A path that
# resolves to a network share is refused.
function Get-FinalPath([string]$Path) {
    if (-not ('ScarletInstall.FinalPath' -as [type])) {
        Add-Type -Language CSharp -TypeDefinition @'
namespace ScarletInstall
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.Win32.SafeHandles;

    // GetFinalPathNameByHandle for Install-Meter.ps1: the path Windows actually opens.
    public static class FinalPath
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        public static string Of(string path)
        {
            // FILE_READ_ATTRIBUTES, share everything, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS (opens folders too).
            using (SafeFileHandle handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open " + path);
                StringBuilder buffer = new StringBuilder(512);
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder((int)length + 1);
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                }
                if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve " + path);
                return buffer.ToString();
            }
        }
    }
}
'@
    }
    $final = [ScarletInstall.FinalPath]::Of($Path)
    if ($final.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase) -or -not $final.StartsWith('\\?\')) {
        throw "$Path resolves to $final, which is not on a local drive."
    }
    $final.Substring(4).TrimEnd('\')
}

# The final form of a path that may not exist yet: the final path of its nearest existing ancestor (which must be
# a folder), followed by the components below it, which do not exist and so cannot be links.
function Resolve-FinalPath([string]$Path) {
    $existing = $Path
    $rest = @()
    while (-not (Test-Path -LiteralPath $existing)) {
        $parent = Split-Path -Parent $existing
        if (-not $parent) { throw "No part of $Path exists." }
        $rest = @(Split-Path -Leaf $existing) + $rest
        $existing = $parent
    }
    if ($rest.Count -and -not (Test-Path -LiteralPath $existing -PathType Container)) {
        throw "$existing is a file, so $Path cannot be created."
    }
    $final = Get-FinalPath $existing
    foreach ($component in $rest) { $final = "$final\$component" }
    $final
}

# True when $Path is $Container or lies inside it. Both are final paths; Windows paths compare case-insensitively.
function Test-PathWithin([string]$Path, [string]$Container) {
    $Path.Equals($Container, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Container.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

# True when $Folder holds the marker file $Name with exactly $Text (a small plain file, not a link).
function Test-Marker([string]$Folder, [string]$Name, [string]$Text) {
    $file = Join-Path $Folder $Name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $file -Force
    if ($item.Length -gt 1024 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
    return [IO.File]::ReadAllText($file).Trim().Equals($Text, [StringComparison]::Ordinal)
}

# Refuses when anything inside $Path is a link or junction: renaming or deleting the folder must never reach a
# link's target. Walks the tree without following links.
function Assert-NoLinksInside([string]$Path) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count) {
        foreach ($entry in (New-Object IO.DirectoryInfo $pending.Pop()).EnumerateFileSystemInfos()) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "$($entry.FullName) is a link or junction, so $Path is not a plain Scarlet Meter folder. Nothing was changed; remove the link first."
            }
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry.FullName) }
        }
    }
}

# Refuses a folder this script would rename or delete unless it is absent, empty, or carries one of the accepted
# markers (and holds no link). Anything else is someone's folder and is left untouched.
function Assert-OwnedFolder([string]$Path, [string[]]$Markers) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer) { throw "$Path is a file, not a Scarlet Meter folder. Nothing was changed; move it away first." }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "$Path is a link or junction. Nothing was changed; replace it with a plain folder first."
    }
    if (-not (Get-ChildItem -LiteralPath $Path -Force | Select-Object -First 1)) { return }
    $owned = $false
    foreach ($marker in $Markers) {
        $text = if ($marker -eq $installMarker) { $installMarkerText } else { $stagingMarkerText }
        if (Test-Marker $Path $marker $text) { $owned = $true }
    }
    if (-not $owned) {
        throw "$Path exists and was not made by Install-Meter.ps1 (it has no $($Markers -join ' or ') marker). Nothing was changed; choose another -InstallDirectory, or move or delete that folder yourself."
    }
    Assert-NoLinksInside $Path
}

# Refuses while a ScarletMeter.exe runs from one of $Folders (final paths). A process whose location cannot be
# read or resolved (an elevated meter seen from a non-elevated shell) might be running from there, so it refuses too.
function Assert-MeterNotRunningFrom([string[]]$Folders) {
    $blocking = @(foreach ($process in @(Get-Process -Name 'ScarletMeter' -ErrorAction SilentlyContinue)) {
        $directory = $null
        try { $directory = Get-FinalPath (Split-Path -Parent $process.Path) } catch { $directory = $null }
        if (-not $directory) {
            "process $($process.Id), location unreadable (perhaps elevated)"
        }
        elseif (@($Folders | Where-Object { Test-PathWithin $directory $_ }).Count) {
            "process $($process.Id), $($process.Path)"
        }
    })
    if ($blocking.Count) {
        throw "Scarlet Meter is running ($($blocking -join '; ')). Close it, then run the install again."
    }
}

# Which of the four folders exist, and whether each carries its marker: for messages after a failure.
function Get-FolderState {
    $parts = foreach ($folder in $install, $previous, $retired, $staging) {
        if (-not (Test-Path -LiteralPath $folder)) { "$folder absent"; continue }
        $marked = (Test-Marker $folder $installMarker $installMarkerText) -or (Test-Marker $folder $stagingMarker $stagingMarkerText)
        if ($marked) { "$folder present" } else { "$folder present (no marker)" }
    }
    $parts -join '; '
}

# Moves staging into place: .previous -> .retired, install -> .previous, staging -> install, then deletes
# .retired. Renames are atomic on one volume. If one fails, each earlier rename is undone on its own (a failed
# undo does not stop the others) and the error lists what failed and where every folder now is. A failed delete
# only leaves .retired for the next install, which removes it while an install exists.
function Invoke-FolderSwap {
    if (Test-Path -LiteralPath $retired) {
        if (-not (Test-Path -LiteralPath $install)) {
            throw "$retired exists but $install does not, so it may be the only copy of the meter. Nothing was changed; rename the folder that holds the meter to $install, then run the install again. Folders: $(Get-FolderState)"
        }
        [IO.Directory]::Delete($retired, $true)
    }
    $done = New-Object 'System.Collections.Generic.List[object]'
    try {
        if (Test-Path -LiteralPath $previous) {
            [IO.Directory]::Move($previous, $retired)
            $done.Add([pscustomobject]@{ From = $previous; To = $retired })
        }
        if (Test-Path -LiteralPath $install) {
            [IO.Directory]::Move($install, $previous)
            $done.Add([pscustomobject]@{ From = $install; To = $previous })
        }
        [IO.Directory]::Move($staging, $install)
    }
    catch {
        $reason = $_.Exception.Message
        $undoFailures = @()
        for ($i = $done.Count - 1; $i -ge 0; $i--) {
            try { [IO.Directory]::Move($done[$i].To, $done[$i].From) }
            catch { $undoFailures += "moving $($done[$i].To) back to $($done[$i].From) failed: $($_.Exception.Message)" }
        }
        if ($undoFailures.Count) {
            throw "Swapping the folders failed ($reason), and undoing it failed too: $($undoFailures -join '; '). Folders now: $(Get-FolderState). Rename them back by hand before running the install again."
        }
        throw "Swapping the folders failed, so the current install was left as it was and the new build stays in $staging. A file in one of the folders may be open (the meter, an Explorer window, a virus scan); close it and run the install again. $reason Folders now: $(Get-FolderState)"
    }
    if (Test-Path -LiteralPath $retired) {
        try { [IO.Directory]::Delete($retired, $true) }
        catch { Write-Warning "Could not remove $retired; the next install removes it. $($_.Exception.Message)" }
    }
}

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
# the folder checked.
foreach ($component in $requested.Substring(3).Split('\')) {
    if ($component -match '[.\s]$') {
        throw "InstallDirectory has a folder name Windows would change (`"$component`")."
    }
}

# The install and its sibling folders, in final form: the parent resolved through any links, the names as given.
$parentFinal = Resolve-FinalPath (Split-Path -Parent $requested)
$install = "$parentFinal\$leaf"
$staging = "$install.staging"
$previous = "$install.previous"
$retired = "$install.retired"
$folders = @($install, $staging, $previous, $retired)

# The meter's default data directory, as EngineOptions.DefaultDataDirectory defines it, resolved the same way
# (it may itself be a junction to elsewhere). A data directory moved with --data-dir cannot be known here.
$dataDirectory = Resolve-FinalPath (Join-Path $localAppData 'ScarletMeter')
$checkout = Get-FinalPath $repoRoot
foreach ($folder in $folders) {
    if ((Test-PathWithin $folder $dataDirectory) -or (Test-PathWithin $dataDirectory $folder)) {
        throw "$folder overlaps the meter's data directory $dataDirectory, which an install must never touch. Nothing was changed; choose another -InstallDirectory."
    }
    if ((Test-PathWithin $folder $checkout) -or (Test-PathWithin $checkout $folder)) {
        throw "$folder overlaps this checkout ($checkout). Nothing was changed; install outside it."
    }
}
if ((Test-Path -LiteralPath $retired) -and -not (Test-Path -LiteralPath $install)) {
    throw "$retired exists but $install does not, so it may be the only copy of the meter. Nothing was changed; rename the folder that holds the meter to $install, then run the install again."
}
Assert-OwnedFolder $install @($installMarker)
Assert-OwnedFolder $previous @($installMarker)
Assert-OwnedFolder $retired @($installMarker)
# A staging folder is this script's while being built, or a checked build left by a failed swap.
Assert-OwnedFolder $staging @($stagingMarker, $installMarker)
Assert-MeterNotRunningFrom @($install, $previous, $retired)

if ($CheckOnly) {
    Write-Host "Install checks passed for $install; nothing was built, installed or changed."
    return
}

# --- Build, publish into staging, check the layout, mark ---

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -SkipTests:$SkipTests

$dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
[void][IO.Directory]::CreateDirectory($parentFinal)
try {
    if (Test-Path -LiteralPath $staging) { [IO.Directory]::Delete($staging, $true) }
    [void][IO.Directory]::CreateDirectory($staging)
    # Marks the folder as this script's from the start, so a failed or interrupted run can clear it.
    [IO.File]::WriteAllText((Join-Path $staging $stagingMarker), $stagingMarkerText)

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
    Assert-NoLinksInside $staging
}
catch {
    # The current install is untouched; drop the half-made staging folder.
    if (Test-Marker $staging $stagingMarker $stagingMarkerText) {
        try { [IO.Directory]::Delete($staging, $true) }
        catch { Write-Warning "Could not remove $staging; the next install removes it. $($_.Exception.Message)" }
    }
    throw
}
# The checked publish becomes an install: the marker that lets the next install replace it, then no staging mark.
[IO.File]::WriteAllText((Join-Path $staging $installMarker), $installMarkerText)
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

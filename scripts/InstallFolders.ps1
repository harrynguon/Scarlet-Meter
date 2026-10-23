<#
.SYNOPSIS
Folder rules for Install-Meter.ps1: markers, path resolution, ownership and link checks, the reversible swap.

.DESCRIPTION
A library, not a command: dot-source it (". <scripts>\InstallFolders.ps1"). It defines the marker constants and
the functions below and does nothing else. Install-Meter.ps1 uses it to install; Test-InstallMeter.ps1 uses it
to test the delete and swap paths on throwaway folders without building. A set of install folders is the object
New-InstallFolders returns (Install, Staging, Previous, Retired). Targets Windows PowerShell 5.1 and pwsh.
#>

$InstallMarker = '.scarlet-install'
$InstallMarkerText = 'Scarlet Meter install, made by scripts\Install-Meter.ps1. The next install replaces this folder; keep nothing else in it.'
$StagingMarker = '.scarlet-install-staging'
$StagingMarkerText = 'Scarlet Meter staging folder, made by scripts\Install-Meter.ps1 and removed by it.'

# The four folders an install works with, from the install folder's final path.
function New-InstallFolders([string]$Install) {
    [pscustomobject]@{
        Install = $Install
        Staging = "$Install.staging"
        Previous = "$Install.previous"
        Retired = "$Install.retired"
    }
}

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

# Refuses a folder the install would rename or delete unless it is absent, empty, or carries one of the accepted
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
        $text = if ($marker -eq $InstallMarker) { $InstallMarkerText } else { $StagingMarkerText }
        if (Test-Marker $Path $marker $text) { $owned = $true }
    }
    if (-not $owned) {
        throw "$Path exists and was not made by Install-Meter.ps1 (it has no $($Markers -join ' or ') marker). Nothing was changed; choose another -InstallDirectory, or move or delete that folder yourself."
    }
    Assert-NoLinksInside $Path
}

# The ownership refusals for a set of install folders: a .retired without an install (it may be the only copy),
# then each folder that may be renamed or deleted. Run before building and again just before the swap, since the
# build takes minutes. A staging folder is the script's while being built (staging marker) or a checked build
# (install marker).
function Assert-InstallFoldersOwned($Folders) {
    if ((Test-Path -LiteralPath $Folders.Retired) -and -not (Test-Path -LiteralPath $Folders.Install)) {
        throw "$($Folders.Retired) exists but $($Folders.Install) does not, so it may be the only copy of the meter. Nothing was changed; rename the folder that holds the meter to $($Folders.Install), then run the install again."
    }
    Assert-OwnedFolder $Folders.Install @($InstallMarker)
    Assert-OwnedFolder $Folders.Previous @($InstallMarker)
    Assert-OwnedFolder $Folders.Retired @($InstallMarker)
    Assert-OwnedFolder $Folders.Staging @($StagingMarker, $InstallMarker)
}

# Deletes a folder the install made so that a delete stopping part way (a file held open) leaves it still marked:
# every entry but the markers first, then the markers, then the folder. A plain recursive delete goes in
# directory order, where ".scarlet-install" sorts first, and would leave an unmarked folder that the next install
# refuses as someone else's. Callers have checked the folder holds no link (Assert-OwnedFolder).
function Remove-OwnedFolder([string]$Path) {
    $markers = @($InstallMarker, $StagingMarker)
    foreach ($entry in @((New-Object IO.DirectoryInfo $Path).GetFileSystemInfos())) {
        if ($markers -contains $entry.Name) { continue }
        if ($entry -is [IO.DirectoryInfo]) { [IO.Directory]::Delete($entry.FullName, $true) }
        else { [IO.File]::Delete($entry.FullName) }
    }
    foreach ($marker in $markers) {
        $file = Join-Path $Path $marker
        if (Test-Path -LiteralPath $file -PathType Leaf) { [IO.File]::Delete($file) }
    }
    [IO.Directory]::Delete($Path, $false)
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
function Get-FolderState($Folders) {
    $parts = foreach ($folder in $Folders.Install, $Folders.Previous, $Folders.Retired, $Folders.Staging) {
        if (-not (Test-Path -LiteralPath $folder)) { "$folder absent"; continue }
        $marked = (Test-Marker $folder $InstallMarker $InstallMarkerText) -or (Test-Marker $folder $StagingMarker $StagingMarkerText)
        if ($marked) { "$folder present" } else { "$folder present (no marker)" }
    }
    $parts -join '; '
}

# Moves the checked build in staging into place: .previous -> .retired, install -> .previous, staging -> install,
# then deletes .retired. A .retired left by an earlier install is deleted first, only while an install exists;
# if that fails nothing is swapped. Renames are atomic on one volume. If one fails, each earlier rename is undone
# on its own (a failed undo does not stop the others) and the error lists what failed and where every folder now
# is. A failed final delete only leaves .retired, still marked, for the next install to remove.
function Invoke-FolderSwap($Folders) {
    if (Test-Path -LiteralPath $Folders.Retired) {
        if (-not (Test-Path -LiteralPath $Folders.Install)) {
            throw "$($Folders.Retired) exists but $($Folders.Install) does not, so it may be the only copy of the meter. Nothing was changed; rename the folder that holds the meter to $($Folders.Install), then run the install again. Folders: $(Get-FolderState $Folders)"
        }
        try { Remove-OwnedFolder $Folders.Retired }
        catch {
            throw "Could not remove the leftover $($Folders.Retired) (a file in it may be open), so nothing was swapped: the install is as it was and the new build stays in $($Folders.Staging). Close whatever holds the file and run the install again. $($_.Exception.Message) Folders now: $(Get-FolderState $Folders)"
        }
    }
    $done = New-Object 'System.Collections.Generic.List[object]'
    try {
        if (Test-Path -LiteralPath $Folders.Previous) {
            [IO.Directory]::Move($Folders.Previous, $Folders.Retired)
            $done.Add([pscustomobject]@{ From = $Folders.Previous; To = $Folders.Retired })
        }
        if (Test-Path -LiteralPath $Folders.Install) {
            [IO.Directory]::Move($Folders.Install, $Folders.Previous)
            $done.Add([pscustomobject]@{ From = $Folders.Install; To = $Folders.Previous })
        }
        [IO.Directory]::Move($Folders.Staging, $Folders.Install)
    }
    catch {
        $reason = $_.Exception.Message
        $undoFailures = @()
        for ($i = $done.Count - 1; $i -ge 0; $i--) {
            try { [IO.Directory]::Move($done[$i].To, $done[$i].From) }
            catch { $undoFailures += "moving $($done[$i].To) back to $($done[$i].From) failed: $($_.Exception.Message)" }
        }
        if ($undoFailures.Count) {
            throw "Swapping the folders failed ($reason), and undoing it failed too: $($undoFailures -join '; '). Folders now: $(Get-FolderState $Folders). Rename them back by hand before running the install again."
        }
        throw "Swapping the folders failed, so the current install was left as it was and the new build stays in $($Folders.Staging). A file in one of the folders may be open (the meter, an Explorer window, a virus scan); close it and run the install again. $reason Folders now: $(Get-FolderState $Folders)"
    }
    if (Test-Path -LiteralPath $Folders.Retired) {
        try { Remove-OwnedFolder $Folders.Retired }
        catch { Write-Warning "Could not remove $($Folders.Retired); it stays marked, and the next install removes it. $($_.Exception.Message)" }
    }
}

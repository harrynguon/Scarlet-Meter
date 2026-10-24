# The folder rules behind Publish-Portable.ps1's one live portable copy, dot-sourced by it and by
# Test-PortableFolders.ps1 (which checks them against throwaway folders, without building anything).
#
# <root>\ScarletMeter          the live copy: ScarletMeter.exe, the portable marker, and the player's data folder
# <root>\ScarletMeter.staging  a new build while it is made and checked; never holds data except mid-update
# <root>\ScarletMeter.old      the previous program files, for a moment during an update
# <root>\ScarletMeter-win-x64.zip   only with -Zip: the latest build without data, replaced each time
#
# Every rename stays on one drive (siblings in <root>), so the player's data folder is moved, never copied, and
# nothing that holds a data folder is ever deleted. Only folders this script made are renamed or deleted: the live
# and old folders carry the portable marker, the staging folder its own marker; links are refused. The generic
# path helpers (final paths, link checks, the running-meter check) come from InstallFolders.ps1.

. (Join-Path $PSScriptRoot 'InstallFolders.ps1')

# Must match PortableLayout.MarkerFileName in the app (PortableLayoutTests checks the script that writes it).
$PortableMarker = 'ScarletMeter.portable'
$PortableMarkerText = "This file makes Scarlet Meter keep its settings, history and logs in the data folder beside it.`r`n" +
    'Delete it to use the per-user data folder (%LocalAppData%\ScarletMeter) instead.'
$PortableStagingMarker = '.scarlet-portable-staging'
$PortableStagingMarkerText = 'Scarlet Meter portable build being made by scripts\Publish-Portable.ps1; it removes this folder or makes it the live copy.'
$PortableDataFolder = 'data'

# The folder set for an output root (an absolute, final path).
function New-PortableFolders([string]$Root) {
    $live = Join-Path $Root 'ScarletMeter'
    [pscustomobject]@{
        Root    = $Root
        Live    = $live
        Staging = "$live.staging"
        Old     = "$live.old"
        Zip     = Join-Path $Root 'ScarletMeter-win-x64.zip'
    }
}

function Test-PortableCopy([string]$Path) { Test-Marker $Path $PortableMarker $PortableMarkerText }

function Test-PortableStaging([string]$Path) { Test-Marker $Path $PortableStagingMarker $PortableStagingMarkerText }

function Test-HoldsData([string]$Path) { Test-Path -LiteralPath (Join-Path $Path $PortableDataFolder) }

# True for an existing folder with nothing in it.
function Test-EmptyFolder([string]$Path) {
    (Test-Path -LiteralPath $Path -PathType Container) -and -not @((New-Object IO.DirectoryInfo $Path).EnumerateFileSystemInfos()).Count
}

# Refuses a path that is a file or a link, or a folder holding a link: renaming or deleting it must never reach a
# link's target.
function Assert-PlainFolder([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Path is a file, not a folder. Nothing was changed; move it away." }
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "$Path is a link or junction. Nothing was changed; remove the link first."
    }
    Assert-NoLinksInside $Path
}

# Finishes or undoes an update that stopped part way, so the live copy and its data are where they belong before
# anything else runs. Returns a sentence describing what it did, or $null.
#  - no live copy, staging holds data: the data had moved into the new build; the new build becomes the live copy.
#  - no live copy, old is ours: the live copy had just been renamed aside; it becomes the live copy again.
function Restore-InterruptedPortableUpdate($Folders) {
    if (Test-Path -LiteralPath $Folders.Live) { return $null }
    if ((Test-Path -LiteralPath $Folders.Staging) -and (Test-HoldsData $Folders.Staging) -and
        ((Test-PortableStaging $Folders.Staging) -or (Test-PortableCopy $Folders.Staging))) {
        Assert-PlainFolder $Folders.Staging
        [IO.Directory]::Move($Folders.Staging, $Folders.Live)
        return "An earlier update had stopped part way; it was finished ($($Folders.Live) holds your data)."
    }
    if ((Test-Path -LiteralPath $Folders.Old) -and (Test-PortableCopy $Folders.Old)) {
        Assert-PlainFolder $Folders.Old
        [IO.Directory]::Move($Folders.Old, $Folders.Live)
        return "An earlier update had stopped part way; it was undone ($($Folders.Live) is back as it was)."
    }
    return $null
}

# The refusals that need no build: each folder at the three names must be plain, and ours or absent; a staging or
# old folder still holding data (an interruption the restore above could not resolve) is refused, never deleted.
function Assert-PortableFoldersOwned($Folders) {
    foreach ($folder in $Folders.Live, $Folders.Staging, $Folders.Old) { Assert-PlainFolder $folder }
    if ((Test-Path -LiteralPath $Folders.Live) -and -not (Test-PortableCopy $Folders.Live) -and -not (Test-EmptyFolder $Folders.Live)) {
        throw "$($Folders.Live) exists but is not a portable copy this script made (no ScarletMeter.portable marker). Nothing was changed; move it away or choose another -OutputRoot."
    }
    foreach ($leftover in $Folders.Staging, $Folders.Old) {
        if (-not (Test-Path -LiteralPath $leftover)) { continue }
        if (Test-HoldsData $leftover) {
            throw "$leftover holds a data folder, so it may hold your settings and history. Nothing was changed; move its data folder into $($Folders.Live) (or keep it elsewhere), delete $leftover, then run again."
        }
        if (-not (Test-PortableStaging $leftover) -and -not (Test-PortableCopy $leftover) -and -not (Test-EmptyFolder $leftover)) {
            throw "$leftover exists but was not made by Publish-Portable.ps1. Nothing was changed; move it away, then run again."
        }
    }
}

# Deletes the staging and old folders a previous run left (checked as ours and without data first).
function Clear-PortableLeftovers($Folders) {
    foreach ($leftover in $Folders.Staging, $Folders.Old) {
        if ((Test-Path -LiteralPath $leftover) -and -not (Test-HoldsData $leftover)) { Remove-OwnedFolder $leftover }
    }
}

# Makes the checked build in staging the live copy, carrying the live copy's data folder over:
#   1. live -> old   2. old\data -> staging\data   3. staging -> live   4. delete old (program files only)
# If step 2 or 3 fails, the earlier steps are undone and the error says where everything is; a failed step 4
# only warns (the next run deletes the folder). The caller has checked that no meter runs from live or old.
function Invoke-PortableSwap($Folders) {
    if (-not (Test-Path -LiteralPath $Folders.Live)) {
        [IO.Directory]::Move($Folders.Staging, $Folders.Live)
    }
    else {
        [IO.Directory]::Move($Folders.Live, $Folders.Old)
        $oldData = Join-Path $Folders.Old $PortableDataFolder
        $newData = Join-Path $Folders.Staging $PortableDataFolder
        try {
            if (Test-Path -LiteralPath $oldData) { [IO.Directory]::Move($oldData, $newData) }
            try {
                [IO.Directory]::Move($Folders.Staging, $Folders.Live)
            }
            catch {
                if (Test-Path -LiteralPath $newData) { [IO.Directory]::Move($newData, $oldData) }
                throw
            }
        }
        catch {
            $failure = $_.Exception.Message
            try { [IO.Directory]::Move($Folders.Old, $Folders.Live) }
            catch {
                throw "Updating $($Folders.Live) stopped part way and could not be undone ($failure; then $($_.Exception.Message)). Your data folder is in $(if (Test-Path -LiteralPath $oldData) { $oldData } elseif (Test-Path -LiteralPath $newData) { $newData } else { 'neither folder: look in ' + $Folders.Root }). Running the script again finishes or undoes the update."
            }
            throw "Updating $($Folders.Live) failed and was undone; your copy and its data are as they were. $failure"
        }
        try { if (-not (Test-HoldsData $Folders.Old)) { Remove-OwnedFolder $Folders.Old } }
        catch { Write-Warning "Could not delete $($Folders.Old) (the previous program files, no data); the next run deletes it. $($_.Exception.Message)" }
    }
    $mark = Join-Path $Folders.Live $PortableStagingMarker
    if (Test-Path -LiteralPath $mark) { Remove-Item -LiteralPath $mark -Force }
}

# Writes the build in $Source (staging, before the data moves in) to $ZipPath with "ScarletMeter/" at the top and
# forward slashes, leaving out the staging marker and any data. Written beside it first, then moved over the
# previous zip, so a failure leaves the previous zip as it was and no partial file.
function Write-PortableZip([string]$Source, [string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $partial = "$ZipPath.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    try {
        $archive = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File -Force) {
                $relative = $file.FullName.Substring($Source.Length).TrimStart('\')
                if ($relative -eq $PortableStagingMarker -or $relative -like "$PortableDataFolder\*") { continue }
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, 'ScarletMeter/' + ($relative -replace '\\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally { $archive.Dispose() }
        Move-Item -LiteralPath $partial -Destination $ZipPath -Force
    }
    catch {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
        throw
    }
}

# Removes builds earlier versions of Publish-Portable.ps1 left under stamped names in $Root
# (ScarletMeter-<commit>-upstream-<release>\ScarletMeter and its zip): only a folder whose ScarletMeter subfolder
# carries the portable marker, holds no data folder and no link. Returns the names removed and the ones kept.
function Remove-LegacyPortableBuilds([string]$Root) {
    $removed = @(); $kept = @()
    foreach ($folder in @(Get-ChildItem -LiteralPath $Root -Directory -Force | Where-Object { $_.Name -cmatch '\AScarletMeter-[A-Za-z0-9._-]+-upstream-[A-Za-z0-9._-]+\z' })) {
        $inner = Join-Path $folder.FullName 'ScarletMeter'
        $ours = ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and (Test-PortableCopy $inner)
        if (-not $ours) { continue }
        if (Test-HoldsData $inner) { $kept += $folder.Name; continue }
        try {
            Assert-NoLinksInside $folder.FullName
            Remove-OwnedFolder $inner
            if (Test-EmptyFolder $folder.FullName) { [IO.Directory]::Delete($folder.FullName, $false) }
            $removed += $folder.Name
        }
        catch { $kept += $folder.Name }
    }
    foreach ($zip in @(Get-ChildItem -LiteralPath $Root -File -Force | Where-Object { $_.Name -cmatch '\AScarletMeter-[A-Za-z0-9._-]+-upstream-[A-Za-z0-9._-]+-win-x64\.zip\z' })) {
        Remove-Item -LiteralPath $zip.FullName -Force
        $removed += $zip.Name
    }
    [pscustomobject]@{ Removed = $removed; Kept = $kept }
}

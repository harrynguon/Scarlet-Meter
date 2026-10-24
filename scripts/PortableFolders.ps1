# The folder rules behind Publish-Portable.ps1's one live portable copy, dot-sourced by it, by Update-Meter.ps1 and
# by Test-PortableFolders.ps1 (which checks them against throwaway folders, without building anything).
#
# <root>\ScarletMeter          the live copy: the build's files, the portable marker, the player's data folder and
#                              anything else the player put there
# <root>\ScarletMeter.staging  a new build while it is made and checked; holds the player's files only mid-update
# <root>\ScarletMeter.old      the previous program files, for a moment during an update
# <root>\ScarletMeter-win-x64.zip   only with -Zip: the latest build without the player's files
#
# A build lists its own top-level files in a manifest (.scarlet-portable-files). Everything else in the copy is the
# player's (the data folder, a backup they made beside it, notes) and is carried into each new build; only the
# listed files are ever deleted. Every move is a rename within <root>, so the player's files are moved, never copied,
# and nothing of theirs is deleted. Only folders this script made are renamed or deleted: the live and old folders
# carry the portable marker, the staging folder its own marker, and the markers are deleted last, so a delete cut
# short leaves a folder the next run still recognises. Links are refused. The generic path helpers (final paths,
# link checks, the running-meter check, the marker-last delete) come from InstallFolders.ps1.

. (Join-Path $PSScriptRoot 'InstallFolders.ps1')

# Must match PortableLayout.MarkerFileName and DataFolderName in the app (PortableLayoutTests checks).
$PortableMarker = 'ScarletMeter.portable'
$PortableMarkerText = "This file makes Scarlet Meter keep its settings, history and logs in the data folder beside it.`r`n" +
    'Delete it to use the per-user data folder (%LocalAppData%\ScarletMeter) instead.'
$PortableStagingMarker = '.scarlet-portable-staging'
$PortableStagingMarkerText = 'Scarlet Meter portable build being made by scripts\Publish-Portable.ps1; it removes this folder or makes it the live copy.'
$PortableManifest = '.scarlet-portable-files'
$PortableManifestHeader = '# The files of this Scarlet Meter build (scripts\Publish-Portable.ps1). An update replaces these and carries everything else in this folder into the new build.'
$PortableDataFolder = 'data'
# The script's own files in a portable folder, in the order a delete removes them: last of all, the marker.
$PortableOwnFiles = @($PortableManifest, $PortableStagingMarker, $PortableMarker)

# The folder set for an output root (from Resolve-PortableRoot).
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

# The final path of an output root given as an absolute path. A drive root stays "C:\" (as "C:" Windows would read
# it as that drive's current directory). Refuses a relative path, a file, a path that is not on a local drive
# (Get-FinalPath), and a root inside an existing portable copy, whose update would carry a copy there along as the
# player's files.
function Resolve-PortableRoot([string]$Path) {
    if ($Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') { throw "OutputRoot must be an absolute path, not `"$Path`"." }
    if ($Path -match '^[\\/]{2}[?.][\\/]') { throw "OutputRoot must be a plain path such as D:\Games, not the device path `"$Path`"." }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[A-Za-z]:\\$') { $full = $full.TrimEnd('\', '/') }
    $final = Resolve-FinalPath $full
    if ($final -match '^[A-Za-z]:$') { $final += '\' }
    if ((Test-Path -LiteralPath $final) -and -not (Test-Path -LiteralPath $final -PathType Container)) {
        throw "OutputRoot $final is a file."
    }
    for ($folder = $final; $folder; $folder = Split-Path -Parent $folder) {
        if (Test-Path -LiteralPath (Join-Path $folder $PortableMarker) -PathType Leaf) {
            throw "OutputRoot $final is inside the portable copy $folder. Nothing was changed; choose a folder outside it."
        }
    }
    $final
}

function Test-PortableCopy([string]$Path) { Test-Marker $Path $PortableMarker $PortableMarkerText }

function Test-PortableStaging([string]$Path) { Test-Marker $Path $PortableStagingMarker $PortableStagingMarkerText }

function Test-HoldsData([string]$Path) { Test-Path -LiteralPath (Join-Path $Path $PortableDataFolder) }

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

# The names of $Folder's top-level entries.
function Get-EntryNames([string]$Folder) {
    @((New-Object IO.DirectoryInfo $Folder).GetFileSystemInfos() | ForEach-Object { $_.Name })
}

# Lists $Folder's top-level entries, the build just published, as its manifest. A build never has a data folder,
# so the player's data can never be listed as the build's.
function Write-PortableManifest([string]$Folder) {
    $names = @(Get-EntryNames $Folder | Where-Object { $PortableOwnFiles -notcontains $_ } | Sort-Object)
    if ($names -contains $PortableDataFolder) { throw "The new build in $Folder holds a $PortableDataFolder folder; a build never has one." }
    if (-not $names.Count) { throw "The new build in $Folder is empty." }
    [IO.File]::WriteAllText((Join-Path $Folder $PortableManifest), ((@($PortableManifestHeader) + $names) -join "`r`n") + "`r`n")
}

# The build's top-level entries as $Folder's manifest lists them, or $null when it has none or it is not one this
# script wrote (another header, a name with a path character, a leading space or a trailing dot or space, which
# covers "." and "..", the data folder, one of the script's own files).
function Read-PortableManifest([string]$Folder) {
    $file = Join-Path $Folder $PortableManifest
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $file -Force
    if ($item.Length -gt 65536 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $null }
    $lines = [IO.File]::ReadAllLines($file)
    if (-not $lines.Count -or $lines[0] -cne $PortableManifestHeader) { return $null }
    $names = @($lines | Select-Object -Skip 1 | Where-Object { $_ })
    if (-not $names.Count) { return $null }
    foreach ($name in $names) {
        # Windows drops a trailing dot or space when it opens a name ("data." is data), so such names are refused.
        if ($name -match '[\\/:*?"<>|]' -or $name -match '(^ |[. ]$)' -or $name -eq $PortableDataFolder -or $PortableOwnFiles -contains $name) {
            return $null
        }
    }
    , $names
}

# The player's entries in $Folder: every top-level entry its manifest does not list, apart from the script's own
# files; the data folder always. A folder without a valid manifest is judged by $Fallback (another build's list)
# instead, and with neither, everything is the player's. A staging folder without a manifest holds nothing of the
# player's but a data folder, since the swap writes the manifest before anything of theirs moves in.
function Get-PlayerEntries([string]$Folder, [string[]]$Fallback) {
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) { return }
    $names = Get-EntryNames $Folder
    $build = Read-PortableManifest $Folder
    if ($null -eq $build) { $build = if (Test-PortableStaging $Folder) { $names } else { @($Fallback) } }
    $names | Where-Object { $PortableOwnFiles -notcontains $_ -and ($_ -eq $PortableDataFolder -or $build -notcontains $_) }
}

# Renames a file or folder within the output root. Every move the library makes goes through here (the tests stand
# in for it to make a chosen move fail).
function Move-PortableEntry([string]$From, [string]$To) {
    if (Test-Path -LiteralPath $From -PathType Container) { [IO.Directory]::Move($From, $To) }
    else { [IO.File]::Move($From, $To) }
}

# Deletes a folder this script made: refuses, deleting nothing, when it holds anything of the player's at this
# moment (judged as Get-PlayerEntries does, with $Fallback), or when it is or holds a link (so a delete never reaches
# a link's target); otherwise deletes the build's files and then
# the markers, last, so a delete cut short by an open file leaves a folder the next run recognises and finishes.
function Remove-PortableFolder([string]$Path, [string[]]$Fallback) {
    $mine = @(Get-PlayerEntries $Path $Fallback)
    if ($mine.Count -or (Test-HoldsData $Path)) {
        throw "$Path holds $(@($mine) -join ', '), which may be yours, so it was not deleted. Move those into $(Split-Path -Parent $Path)\ScarletMeter (or keep them elsewhere), delete $Path, then run again."
    }
    Assert-PlainFolder $Path
    Remove-OwnedFolder $Path $PortableOwnFiles
}

# Moves the player's entries $Names from $From into $To. Returns nothing; throws on the first failure, with the
# names moved so far added to $Moved (a List[string] the caller keeps for an undo).
function Move-PlayerEntries([string]$From, [string]$To, [string[]]$Names, $Moved) {
    foreach ($name in $Names) {
        Move-PortableEntry (Join-Path $From $name) (Join-Path $To $name)
        $Moved.Add($name)
    }
}

# The player's entries in $From that $To already has, so cannot be moved there.
function Get-Clashes([string]$To, [string[]]$Names) {
    @($Names | Where-Object { Test-Path -LiteralPath (Join-Path $To $_) })
}

# Finishes or undoes an update that stopped part way, so the live copy and the player's files are where they belong
# before anything else runs. Returns a sentence describing what it did, or $null.
#  - no live copy, the staging folder (ours) holds some of the player's files: they had begun moving into the new
#    build; it becomes the live copy, keeping its staging marker until the rest has followed (below).
#  - no live copy, the old folder is ours: the live copy had just been renamed aside, or the update was undone all
#    but the last rename; it becomes the live copy again.
#  - the live copy carries the staging marker: an update or a restore stopped before the rest of the player's files
#    followed from the old folder (or before the marker went); they follow now, then the marker goes. A restore cut
#    short here is therefore resumed by the next run, and one that cannot finish stops with what to do.
function Restore-InterruptedPortableUpdate($Folders) {
    if (-not (Test-Path -LiteralPath $Folders.Live)) {
        if ((Test-PortableStaging $Folders.Staging) -and @(Get-PlayerEntries $Folders.Staging).Count) {
            Assert-PlainFolder $Folders.Staging
            Move-PortableEntry $Folders.Staging $Folders.Live
        }
        elseif (Test-PortableCopy $Folders.Old) {
            Assert-PlainFolder $Folders.Old
            Move-PortableEntry $Folders.Old $Folders.Live
            return "An earlier update had stopped part way; it was undone ($($Folders.Live) is back as it was)."
        }
        else { return $null }
    }
    elseif (-not (Test-PortableStaging $Folders.Live)) { return $null }
    if (Test-PortableCopy $Folders.Old) {
        Assert-PlainFolder $Folders.Old
        $rest = @(Get-PlayerEntries $Folders.Old (Read-PortableManifest $Folders.Live))
        $clashes = Get-Clashes $Folders.Live $rest
        if ($clashes.Count) {
            throw "An earlier update had stopped part way, and $($clashes -join ', ') in $($Folders.Old) cannot follow into $($Folders.Live), which already has them. Do not start the meter yet: compare the two, keep the one you want in $($Folders.Live), delete $($Folders.Old), then run again."
        }
        try { Move-PlayerEntries $Folders.Old $Folders.Live $rest (New-Object 'System.Collections.Generic.List[string]') }
        catch {
            throw "An earlier update had stopped part way, and finishing it stopped too ($($_.Exception.Message)): some of your files are still in $($Folders.Old). Do not start the meter now. Close whatever has files open there, then run Publish-Portable.ps1 again, which finishes it."
        }
    }
    Remove-Item -LiteralPath (Join-Path $Folders.Live $PortableStagingMarker) -Force
    return "An earlier update had stopped part way; it was finished ($($Folders.Live) holds your files)."
}

# The refusals that need no build: each folder at the three names must be plain, and ours or absent (or empty); a
# staging or old folder still holding the player's files (an interruption the restore above could not resolve) is
# refused, never deleted.
function Assert-PortableFoldersOwned($Folders) {
    foreach ($folder in $Folders.Live, $Folders.Staging, $Folders.Old) { Assert-PlainFolder $folder }
    if ((Test-Path -LiteralPath $Folders.Live) -and -not (Test-PortableCopy $Folders.Live) -and -not (Test-EmptyFolder $Folders.Live)) {
        throw "$($Folders.Live) exists but is not a portable copy this script made (no ScarletMeter.portable marker). Nothing was changed; move it away or choose another -OutputRoot."
    }
    $build = if (Test-PortableCopy $Folders.Live) { Read-PortableManifest $Folders.Live } else { $null }
    $leftovers = @(
        @{ Path = $Folders.Staging; Ours = { param($p) Test-PortableStaging $p } },
        @{ Path = $Folders.Old; Ours = { param($p) Test-PortableCopy $p } })
    foreach ($leftover in $leftovers) {
        $path = $leftover.Path
        if (-not (Test-Path -LiteralPath $path) -or (Test-EmptyFolder $path)) { continue }
        if (-not (& $leftover.Ours $path)) {
            throw "$path exists but was not made by Publish-Portable.ps1. Nothing was changed; move it away, then run again."
        }
        $mine = @(Get-PlayerEntries $path $build)
        if ($mine.Count) {
            throw "$path holds $($mine -join ', '), which may be yours (settings, history). Nothing was changed; move those into $($Folders.Live) (or keep them elsewhere), delete $path, then run again."
        }
    }
}

# True for an existing folder with nothing in it.
function Test-EmptyFolder([string]$Path) {
    (Test-Path -LiteralPath $Path -PathType Container) -and -not @(Get-EntryNames $Path).Count
}

# Deletes the staging and old folders a previous run left (checked by Assert-PortableFoldersOwned first).
function Clear-PortableLeftovers($Folders) {
    $build = if (Test-PortableCopy $Folders.Live) { Read-PortableManifest $Folders.Live } else { $null }
    foreach ($leftover in $Folders.Staging, $Folders.Old) {
        if (Test-Path -LiteralPath $leftover) { Remove-PortableFolder $leftover $build }
    }
}

# Makes the checked build in staging (with its manifest) the live copy, carrying the player's files over:
#   1. live -> old   2. each of the player's entries old\<name> -> staging\<name>   3. staging -> live
#   4. delete old (the previous build's files only)
# Before step 1 it refuses, changing nothing, when one of the player's entries has a name the new build uses. If
# step 2 or 3 fails, what moved goes back and step 1 is undone; if something cannot move back, the live name is left
# free so the next run finishes the update, and the error says where everything is. A failed step 4 only warns.
# The caller has checked that no meter runs from live or old.
function Invoke-PortableSwap($Folders) {
    $build = Read-PortableManifest $Folders.Staging
    if ($null -eq $build) { throw "$($Folders.Staging) has no list of its files ($PortableManifest). Nothing was changed." }
    if (-not (Test-Path -LiteralPath $Folders.Live)) {
        Move-PortableEntry $Folders.Staging $Folders.Live
    }
    else {
        $mine = @(Get-PlayerEntries $Folders.Live $build)
        $clashes = Get-Clashes $Folders.Staging $mine
        if ($clashes.Count) {
            throw "The new build has its own $($clashes -join ', '), which $($Folders.Live) holds as yours, so they cannot be carried over. Nothing was changed; rename them in $($Folders.Live), then run again."
        }
        try { Move-PortableEntry $Folders.Live $Folders.Old }
        catch {
            throw "Could not start updating $($Folders.Live): $($_.Exception.Message) Something has a file in it open: close the meter (and any helper process it left), and any Explorer window or editor showing that folder, then run again. Nothing was changed."
        }
        $moved = New-Object 'System.Collections.Generic.List[string]'
        try {
            Move-PlayerEntries $Folders.Old $Folders.Staging $mine $moved
            Move-PortableEntry $Folders.Staging $Folders.Live
        }
        catch {
            $failure = $_.Exception.Message
            $stranded = @()
            for ($i = $moved.Count - 1; $i -ge 0; $i--) {
                try { Move-PortableEntry (Join-Path $Folders.Staging $moved[$i]) (Join-Path $Folders.Old $moved[$i]) }
                catch { $stranded += $moved[$i] }
            }
            if ($stranded.Count) {
                throw "Updating $($Folders.Live) stopped part way ($failure), and $($stranded -join ', ') could not be moved back: they are in $($Folders.Staging), the rest of the copy in $($Folders.Old). Do not start the meter now. Close whatever has files open there, then run Publish-Portable.ps1 again, which finishes the update."
            }
            try { Move-PortableEntry $Folders.Old $Folders.Live }
            catch {
                throw "Updating $($Folders.Live) failed ($failure) and the last undo step failed too ($($_.Exception.Message)): the copy and all your files are in $($Folders.Old). Do not start the meter now. Run Publish-Portable.ps1 again, which puts it back."
            }
            throw "Updating $($Folders.Live) failed and was undone; your copy and its data are as they were. $failure"
        }
        try { Remove-PortableFolder $Folders.Old $build }
        catch { Write-Warning "Could not delete $($Folders.Old), the previous program files; the next run deletes it. $($_.Exception.Message)" }
    }
    $mark = Join-Path $Folders.Live $PortableStagingMarker
    if (Test-Path -LiteralPath $mark) { Remove-Item -LiteralPath $mark -Force }
}

# Writes the build in $Source (the live copy) to $ZipPath with "ScarletMeter/" at the top and forward slashes: only
# what its manifest lists, the manifest and the portable marker, so never the player's data or files. Written
# beside it first, then moved over the previous zip, so a failure leaves the previous zip as it was.
function Write-PortableZip([string]$Source, [string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $build = Read-PortableManifest $Source
    if ($null -eq $build) { throw "$Source has no list of its files ($PortableManifest); no zip was written." }
    $partial = "$ZipPath.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    try {
        $archive = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in @($build) + $PortableManifest + $PortableMarker) {
                $path = Join-Path $Source $name
                # The name Windows opens must be the one listed, so a listed name can never reach the player's data.
                $actual = (Get-Item -LiteralPath $path -Force).Name
                if ($actual -ne $name -or $actual -eq $PortableDataFolder) { throw "$Source lists $name, which opens as $actual; no zip was written." }
                $files = if (Test-Path -LiteralPath $path -PathType Container) { @(Get-ChildItem -LiteralPath $path -Recurse -File -Force) } else { @(Get-Item -LiteralPath $path -Force) }
                foreach ($file in $files) {
                    $relative = $file.FullName.Substring($Source.Length).TrimStart('\')
                    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, 'ScarletMeter/' + ($relative -replace '\\', '/'), [IO.Compression.CompressionLevel]::Optimal)
                }
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

# True when $Path is a zip this script (or an earlier version of it) wrote: a readable zip whose ScarletMeter/
# folder holds the portable marker with its text. Another file under the same name is left alone.
function Test-PortableZip([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $entry = $archive.GetEntry("ScarletMeter/$PortableMarker")
            if (-not $entry -or $entry.Length -gt 1024) { return $false }
            $reader = New-Object IO.StreamReader($entry.Open())
            try { return $reader.ReadToEnd().Trim().Equals($PortableMarkerText, [StringComparison]::Ordinal) }
            finally { $reader.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    catch { return $false }
}

# Removes builds earlier versions of Publish-Portable.ps1 left under stamped names in $Root
# (ScarletMeter-<commit>-upstream-<release>\ScarletMeter, and its zip): only a folder whose ScarletMeter subfolder
# carries the portable marker and holds nothing beyond the files of a build ($Build, the live copy's list; those
# builds had no manifest) and no link, and only zips Test-PortableZip recognises. Returns the names removed and the
# ones kept.
function Remove-LegacyPortableBuilds([string]$Root, [string[]]$Build) {
    $removed = @(); $kept = @()
    foreach ($folder in @(Get-ChildItem -LiteralPath $Root -Directory -Force | Where-Object { $_.Name -cmatch '\AScarletMeter-[A-Za-z0-9._-]+-upstream-[A-Za-z0-9._-]+\z' })) {
        $inner = Join-Path $folder.FullName 'ScarletMeter'
        $ours = ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and (Test-PortableCopy $inner)
        if (-not $ours) { continue }
        try {
            # Remove-PortableFolder refuses an inner folder that is itself a link, so its target is never reached.
            Remove-PortableFolder $inner $Build
            if (Test-EmptyFolder $folder.FullName) { [IO.Directory]::Delete($folder.FullName, $false) }
            $removed += $folder.Name
        }
        catch { $kept += $folder.Name }
    }
    foreach ($zip in @(Get-ChildItem -LiteralPath $Root -File -Force | Where-Object { $_.Name -cmatch '\AScarletMeter-[A-Za-z0-9._-]+-upstream-[A-Za-z0-9._-]+-win-x64\.zip\z' })) {
        if (-not (Test-PortableZip $zip.FullName)) { continue }
        Remove-Item -LiteralPath $zip.FullName -Force
        $removed += $zip.Name
    }
    [pscustomobject]@{ Removed = $removed; Kept = $kept }
}

# One run per output root at a time: two would share the staging folder and could delete each other's build. Takes
# a named mutex for the root's final path and returns it (give it to Exit-PortableLock); throws when another run
# holds it. A mutex a crashed run left behind is taken over.
function Enter-PortableLock([string]$Root) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Root.ToUpperInvariant())) }
    finally { $sha.Dispose() }
    $name = 'Local\ScarletMeter-Portable-' + (($digest[0..11] | ForEach-Object { $_.ToString('x2') }) -join '')
    $mutex = New-Object Threading.Mutex($false, $name)
    $owned = $false
    try { $owned = $mutex.WaitOne(0) }
    catch {
        # PowerShell may wrap the method's exception; an abandoned mutex is owned by the caller all the same.
        if ($_.Exception -is [Threading.AbandonedMutexException] -or $_.Exception.InnerException -is [Threading.AbandonedMutexException]) { $owned = $true }
        else { $mutex.Dispose(); throw }
    }
    if (-not $owned) {
        $mutex.Dispose()
        throw "Another Publish-Portable.ps1 or Update-Meter.ps1 run is updating $Root. Nothing was changed; let it finish, then run again."
    }
    $mutex
}

# Releases the lock Enter-PortableLock took. A mutex is re-entrant on the thread that holds it, so a nested Enter in
# the same run would succeed; only Publish-Portable.ps1 takes it (Update-Meter.ps1 calls that script, not the lock).
function Exit-PortableLock($Mutex) {
    if ($Mutex) { try { $Mutex.ReleaseMutex() } finally { $Mutex.Dispose() } }
}

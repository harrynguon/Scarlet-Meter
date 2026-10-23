<#
.SYNOPSIS
Checks Install-Meter.ps1's refusals and acceptances, and its delete and swap paths, without building anything.

.DESCRIPTION
Builds a set of folders in a new, uniquely named folder under the user's temp directory: someone's folder that
happens to hold a ScarletMeter.exe (with another file and a junction), installs holding a link, a junction
install, wrong markers, a .retired folder without an install, junctions to the checkout and to the data
directory's parent. It then runs Install-Meter.ps1 -CheckOnly against each, and against paths in forms that
used to slip past the overlap checks (\\?\, \\.\, admin shares, UNC, short names, forward slashes, trailing
dots). Every refusal must name its reason, every acceptance must pass, and the foreign folder must be exactly
as it was. -CheckOnly never builds or changes a folder, so a broken check cannot move anything.

The delete and swap paths are tested directly through scripts\InstallFolders.ps1 on fake install folders: a
.retired holding a file held open in a subfolder must keep its marker and block the swap with nothing moved, be
accepted by the next install's checks, and go once the file is released; a failed rename must be undone. The temp
folder is removed at the end, links first, so no link target is touched. Targets Windows PowerShell 5.1 and pwsh;
CI runs it.

.EXAMPLE
& "<checkout>\scripts\Test-InstallMeter.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-Meter.ps1'
. (Join-Path $PSScriptRoot 'InstallFolders.ps1')
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-install-test-' + [guid]::NewGuid().ToString('N'))
$markerText = $InstallMarkerText
$failures = New-Object 'System.Collections.Generic.List[string]'
$links = New-Object 'System.Collections.Generic.List[string]'

# A folder holding the given files (name -> text); returns its path.
function New-TestFolder([string]$Path, [hashtable]$Files) {
    [void][IO.Directory]::CreateDirectory($Path)
    foreach ($name in $Files.Keys) { [IO.File]::WriteAllText((Join-Path $Path $name), $Files[$name]) }
    $Path
}

# A directory junction at $Path to $Target, remembered so cleanup removes the link and never its target.
function New-TestJunction([string]$Path, [string]$Target) {
    New-Item -ItemType Junction -Path $Path -Value $Target | Out-Null
    $links.Add($Path)
    $Path
}

# Expects Install-Meter.ps1 -CheckOnly to refuse $Path with a message containing one of $Expected.
function Test-Refused([string]$Name, [string]$Path, [string[]]$Expected) {
    try {
        & $installer -InstallDirectory $Path -CheckOnly 6> $null | Out-Null
        $failures.Add("$Name`: accepted $Path")
        Write-Host "FAIL $Name (accepted)"
    }
    catch {
        $message = $_.Exception.Message
        if (@($Expected | Where-Object { $message.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) {
            Write-Host "ok   $Name"
        }
        else {
            $failures.Add("$Name`: refused for another reason: $message")
            Write-Host "FAIL $Name ($message)"
        }
    }
}

# Expects Install-Meter.ps1 -CheckOnly to accept $Path.
function Test-Accepted([string]$Name, [string]$Path) {
    try {
        & $installer -InstallDirectory $Path -CheckOnly 6> $null | Out-Null
        Write-Host "ok   $Name"
    }
    catch {
        $failures.Add("$Name`: refused: $($_.Exception.Message)")
        Write-Host "FAIL $Name ($($_.Exception.Message))"
    }
}

# Records a failure unless $Path's tree still matches $Before (a Get-TreeSnapshot taken earlier).
# A folder that is gone counts as changed: a rename that was not undone must fail the case, not crash it.
function Test-Unchanged([string]$Name, [string]$Path, [string]$Before) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { $failures.Add("$Name`: $Path is gone"); Write-Host "FAIL $Name ($Path is gone)" }
    elseif ((Get-TreeSnapshot $Path) -ne $Before) { $failures.Add("$Name`: $Path changed") ; Write-Host "FAIL $Name (changed)" }
}

# Every file and link under $Path, relative, with file contents: compared before and after the checks.
function Get-TreeSnapshot([string]$Path) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    $entries = while ($pending.Count) {
        foreach ($entry in (New-Object IO.DirectoryInfo $pending.Pop()).EnumerateFileSystemInfos()) {
            $relative = $entry.FullName.Substring($Path.Length)
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { "link $relative" }
            elseif ($entry -is [IO.DirectoryInfo]) { "dir  $relative"; $pending.Push($entry.FullName) }
            else { "file $relative $([IO.File]::ReadAllText($entry.FullName))" }
        }
    }
    (@($entries) | Sort-Object) -join "`n"
}

try {
    [void][IO.Directory]::CreateDirectory($work)
    $target = New-TestFolder (Join-Path $work 'target') @{ 'keep.txt' = 'a link target' }

    # The auditor's reproduction: a Downloads-like folder that holds a ScarletMeter.exe, other files and a junction.
    $downloads = New-TestFolder (Join-Path $work 'Downloads') @{ 'ScarletMeter.exe' = 'not a meter'; 'important.txt' = 'keep me' }
    New-TestJunction (Join-Path $downloads 'linked') $target | Out-Null
    $foreignBefore = (Get-TreeSnapshot $downloads) + "`n" + (Get-TreeSnapshot $target)

    # Someone's folders at the other names an install renames or deletes: .previous, .staging and .retired.
    $foreign = @{ 'ScarletMeter.exe' = 'not a meter'; 'important.txt' = 'keep me' }
    $foreignPrevious = New-TestFolder (Join-Path $work 'Meter2.previous') $foreign
    $foreignStaging = New-TestFolder (Join-Path $work 'Stage.staging') $foreign
    New-TestFolder (Join-Path $work 'Ret') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null
    $foreignRetired = New-TestFolder (Join-Path $work 'Ret.retired') $foreign
    $foreignSnapshots = @{}
    foreach ($folder in $foreignPrevious, $foreignStaging, $foreignRetired) { $foreignSnapshots[$folder] = Get-TreeSnapshot $folder }
    $linkedInstall = New-TestFolder (Join-Path $work 'Linked') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText }
    New-TestJunction (Join-Path $linkedInstall 'linked') $target | Out-Null
    New-TestJunction (Join-Path $work 'JunctionInstall') $target | Out-Null
    New-TestFolder (Join-Path $work 'Fake') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = 'hello' } | Out-Null
    [IO.File]::WriteAllText((Join-Path $work 'AFile'), 'a file')
    New-TestFolder (Join-Path $work 'Gone.retired') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null
    $toCheckout = New-TestJunction (Join-Path $work 'toCheckout') $repoRoot
    $toLocalAppData = New-TestJunction (Join-Path $work 'toLocalAppData') $localAppData

    New-TestFolder (Join-Path $work 'Empty') @{} | Out-Null
    New-TestFolder (Join-Path $work 'Marked') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null
    New-TestFolder (Join-Path $work 'Marked.previous') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null
    New-TestFolder (Join-Path $work 'Left') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null
    New-TestFolder (Join-Path $work 'Left.staging') @{ 'ScarletMeter.exe' = 'x'; '.scarlet-install' = $markerText } | Out-Null

    $dataDirectory = Join-Path $localAppData 'ScarletMeter'
    $drive = $localAppData.Substring(0, 1)
    $local = 'local drive'

    # Path forms.
    Test-Refused 'relative path' 'relative\ScarletMeter' @($local)
    Test-Refused 'drive root' "$($drive):\" @('not the root')
    Test-Refused '\\?\ form of the data directory' "\\?\$dataDirectory\x" @($local)
    Test-Refused '\\.\ form' "\\.\$work\x" @($local)
    Test-Refused 'admin share' "\\localhost\$drive`$$($dataDirectory.Substring(2))\x" @($local)
    Test-Refused 'UNC share' '\\server\share\ScarletMeter' @($local)
    # Windows drops a trailing dot or space; whether .NET's normalisation or the installer's own check catches it,
    # the data directory spelt this way must be refused.
    Test-Refused 'the data directory with a trailing dot' "$dataDirectory." @('would change', 'data directory')
    Test-Refused 'the data directory with a trailing space' "$dataDirectory " @('would change', 'data directory')
    Test-Refused 'a stream name' "$work\Meter:stream" @('cannot create')
    Test-Refused 'a wildcard' "$work\Meter*" @('cannot create')

    # Overlaps, in every spelling.
    Test-Refused 'the data directory' $dataDirectory @('data directory')
    Test-Refused 'inside the data directory' "$dataDirectory\bin" @('data directory')
    Test-Refused 'the data directory with forward slashes' ($dataDirectory.Replace('\', '/') + '/x') @('data directory')
    Test-Refused 'the data directory''s parent' $localAppData @('data directory')
    Test-Refused 'the data directory through a junction to its parent' "$toLocalAppData\ScarletMeter\x" @('data directory')
    $short = $null
    try { $short = (New-Object -ComObject Scripting.FileSystemObject).GetFolder($localAppData).ShortPath } catch { $short = $null }
    if ($short -and -not $short.Equals($localAppData, [StringComparison]::OrdinalIgnoreCase)) {
        Test-Refused 'the data directory by its short name' "$short\ScarletMeter\x" @('data directory')
    }
    else {
        Write-Host 'skip the data directory by its short name (no 8.3 name on this volume)'
    }
    Test-Refused 'inside the checkout' "$repoRoot\inst" @('checkout')
    Test-Refused 'the checkout through a junction' "$toCheckout\inst2" @('checkout')

    # Folders the installer did not make.
    Test-Refused 'a folder holding a ScarletMeter.exe that is not an install' $downloads @('not made by Install-Meter')
    Test-Refused 'such a folder as .previous' (Join-Path $work 'Meter2') @('not made by Install-Meter')
    Test-Refused 'such a folder as .staging' (Join-Path $work 'Stage') @('not made by Install-Meter')
    Test-Refused 'an install with such a folder as .retired' (Join-Path $work 'Ret') @('not made by Install-Meter')
    foreach ($folder in $foreignSnapshots.Keys) { Test-Unchanged 'a refused foreign folder' $folder $foreignSnapshots[$folder] }
    Test-Refused 'an install holding a junction' $linkedInstall @('link or junction')
    Test-Refused 'an install that is a junction' (Join-Path $work 'JunctionInstall') @('link or junction')
    Test-Refused 'a marker with other text' (Join-Path $work 'Fake') @('not made by Install-Meter')
    Test-Refused 'a file' (Join-Path $work 'AFile') @('is a file')
    Test-Refused '.retired without an install' (Join-Path $work 'Gone') @('only copy')

    # What must still pass.
    Test-Accepted 'a new folder under a new parent' (Join-Path $work 'Fresh\ScarletMeter')
    Test-Accepted 'an empty folder' (Join-Path $work 'Empty')
    Test-Accepted 'a marked install with a marked .previous' (Join-Path $work 'Marked')
    Test-Accepted 'a checked build left in staging' (Join-Path $work 'Left')

    # Deleting a leftover .retired stopped by a file held open in a subfolder: the marker must survive (it is
    # deleted last), the swap must not start, the next install's checks must still accept the folder, and once
    # the file is released the swap must complete.
    $locked = New-InstallFolders (Join-Path $work 'Locked')
    New-TestFolder $locked.Install @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'install'; $InstallMarker = $markerText } | Out-Null
    New-TestFolder $locked.Previous @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'previous'; $InstallMarker = $markerText } | Out-Null
    New-TestFolder $locked.Retired @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'retired'; $InstallMarker = $markerText } | Out-Null
    New-TestFolder (Join-Path $locked.Retired 'sub') @{ 'held.txt' = 'held open' } | Out-Null
    New-TestFolder $locked.Staging @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'new'; $InstallMarker = $markerText } | Out-Null
    $lockedBefore = @{}
    foreach ($folder in $locked.Install, $locked.Previous, $locked.Staging) { $lockedBefore[$folder] = Get-TreeSnapshot $folder }
    $held = [IO.File]::Open((Join-Path $locked.Retired 'sub\held.txt'), 'Open', 'Read', 'None')
    try {
        try {
            Invoke-FolderSwap $locked
            $failures.Add('a .retired held open: the swap went ahead')
            Write-Host 'FAIL a .retired held open (swapped)'
        }
        catch {
            if ($_.Exception.Message.Contains('Could not remove the leftover')) { Write-Host 'ok   a .retired held open blocks the swap' }
            else { $failures.Add("a .retired held open: $($_.Exception.Message)"); Write-Host "FAIL a .retired held open ($($_.Exception.Message))" }
        }
        if (Test-Marker $locked.Retired $InstallMarker $InstallMarkerText) { Write-Host 'ok   a .retired held open keeps its marker' }
        else { $failures.Add('a .retired held open lost its marker'); Write-Host 'FAIL a .retired held open lost its marker' }
        foreach ($folder in $lockedBefore.Keys) { Test-Unchanged 'a .retired held open' $folder $lockedBefore[$folder] }
        Test-Accepted 'the next install accepts a half-deleted .retired' $locked.Install
    }
    finally {
        $held.Dispose()
    }
    try {
        Invoke-FolderSwap $locked
        $installed = [IO.File]::ReadAllText((Join-Path $locked.Install 'version.txt'))
        $kept = [IO.File]::ReadAllText((Join-Path $locked.Previous 'version.txt'))
        if ($installed -eq 'new' -and $kept -eq 'install' -and -not (Test-Path -LiteralPath $locked.Retired) -and -not (Test-Path -LiteralPath $locked.Staging)) {
            Write-Host 'ok   the rerun recovers once the file is released'
        }
        else {
            $failures.Add("the rerun left install=$installed previous=$kept; $(Get-FolderState $locked)")
            Write-Host 'FAIL the rerun recovers once the file is released'
        }
    }
    catch {
        $failures.Add("the rerun failed: $($_.Exception.Message)")
        Write-Host "FAIL the rerun recovers once the file is released ($($_.Exception.Message))"
    }

    # A rename that fails during the swap (a file held open in the install) is undone: every folder as it was.
    $undo = New-InstallFolders (Join-Path $work 'Undo')
    New-TestFolder $undo.Install @{ 'ScarletMeter.exe' = 'x'; 'held.txt' = 'held open'; $InstallMarker = $markerText } | Out-Null
    New-TestFolder $undo.Previous @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'previous'; $InstallMarker = $markerText } | Out-Null
    New-TestFolder $undo.Staging @{ 'ScarletMeter.exe' = 'x'; 'version.txt' = 'new'; $InstallMarker = $markerText } | Out-Null
    $undoBefore = @{}
    foreach ($folder in $undo.Install, $undo.Previous, $undo.Staging) { $undoBefore[$folder] = Get-TreeSnapshot $folder }
    $held = [IO.File]::Open((Join-Path $undo.Install 'held.txt'), 'Open', 'Read', 'None')
    try {
        try {
            Invoke-FolderSwap $undo
            $failures.Add('a failed rename: the swap reported success')
            Write-Host 'FAIL a failed rename is undone (no error)'
        }
        catch {
            $missing = @($undoBefore.Keys | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Container) })
            if ($_.Exception.Message.Contains('left as it was') -and -not $missing) { Write-Host 'ok   a failed rename is undone' }
            elseif ($missing) { $failures.Add("a failed rename was not undone: $($missing -join ', ') missing"); Write-Host 'FAIL a failed rename is undone (folders missing)' }
            else { $failures.Add("a failed rename: $($_.Exception.Message)"); Write-Host "FAIL a failed rename is undone ($($_.Exception.Message))" }
        }
    }
    finally {
        $held.Dispose()
    }
    foreach ($folder in $undoBefore.Keys) { Test-Unchanged 'a failed rename' $folder $undoBefore[$folder] }
    if (Test-Path -LiteralPath $undo.Retired) { $failures.Add('a failed rename left .retired behind'); Write-Host 'FAIL a failed rename left .retired behind' }

    $foreignAfter = (Get-TreeSnapshot $downloads) + "`n" + (Get-TreeSnapshot $target)
    if ($foreignAfter -ne $foreignBefore) { $failures.Add('the foreign folder or its link target changed') }
    foreach ($suffix in '.previous', '.retired', '.staging') {
        if (Test-Path -LiteralPath "$downloads$suffix") { $failures.Add("$downloads$suffix was created") }
    }
}
finally {
    # Links first, removed as links (a non-recursive delete), so no target is touched; then the plain tree.
    foreach ($link in $links) {
        if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link, $false) }
    }
    if (Test-Path -LiteralPath $work) { [IO.Directory]::Delete($work, $true) }
}

if ($failures.Count) {
    throw "Install-Meter.ps1 checks failed:`n$($failures -join "`n")"
}
Write-Host 'Install-Meter.ps1 refused every unsafe target and accepted every owned one.'

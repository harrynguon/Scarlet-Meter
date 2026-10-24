<#
.SYNOPSIS
Checks the live portable copy's folder rules (scripts\PortableFolders.ps1) against throwaway folders.

.DESCRIPTION
Builds nothing and opens no window: each case makes fake builds (a few files, a manifest and the markers) in a new
folder under the temp directory, runs the library's functions on them and checks what moved, what stayed and what
was refused. Covers the first copy; an update that replaces the build's files and carries the player's (data, a
backup beside it, notes) over; a clash with the new build; refusing folders the script did not make, links, a
drive-relative or nested output root; finishing and undoing a cut-short update; leftovers; a delete cut short by an
open file keeping its markers; earlier stamped builds and zips; the zip's contents; a ScarletMeter.exe running from
the copy (a hidden, renamed ping.exe stands in); a second run at once (a child PowerShell process, no window); and
failed swaps, including one whose undo also fails (a stand-in for the library's move makes chosen moves fail).
Prints ok or FAIL per case and throws when any failed. Paths derive from this script's location and the platform's
temp folder. Targets Windows PowerShell 5.1 and pwsh.

.EXAMPLE
& "<checkout>\scripts\Test-PortableFolders.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$library = Join-Path $PSScriptRoot 'PortableFolders.ps1'
. $library

$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-portable-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = New-Object 'System.Collections.Generic.List[string]'

function Pass([string]$Name) { Write-Host "ok   $Name" }
function Fail([string]$Name, [string]$Why) { $failures.Add("$Name`: $Why"); Write-Host "FAIL $Name ($Why)" }
function Check([string]$Name, [bool]$Condition, [string]$Why) { if ($Condition) { Pass $Name } else { Fail $Name $Why } }

# Stands in for the library's Move-PortableEntry: a move whose "<from>><to>" matches a pattern in $script:failMoves
# fails as a held-open file would make it fail; every other move is the real one.
$realMove = ${function:Move-PortableEntry}
$script:failMoves = @()
function Move-PortableEntry([string]$From, [string]$To) {
    foreach ($pattern in $script:failMoves) { if ("$From>$To" -like $pattern) { throw "Injected failure moving $From." } }
    & $realMove $From $To
}

# A fake checked build in $Path, as Publish-Portable.ps1 leaves staging before the swap: its files, its manifest,
# the staging and portable markers. $Extra names more top-level files the build has.
function New-FakeBuild([string]$Path, [string]$Version, [string[]]$Extra = @()) {
    [void][IO.Directory]::CreateDirectory((Join-Path $Path 'wwwroot'))
    [IO.File]::WriteAllText((Join-Path $Path 'ScarletMeter.exe'), $Version)
    [IO.File]::WriteAllText((Join-Path $Path 'wwwroot\index.html'), $Version)
    foreach ($name in $Extra) { [IO.File]::WriteAllText((Join-Path $Path $name), $Version) }
    [IO.File]::WriteAllText((Join-Path $Path $PortableStagingMarker), $PortableStagingMarkerText)
    Write-PortableManifest $Path
    [IO.File]::WriteAllText((Join-Path $Path $PortableMarker), $PortableMarkerText + "`r`n")
}

# A live copy: a build without the staging marker, with a data folder holding a settings file.
function New-FakeCopy([string]$Path, [string]$Version, [string]$Settings, [string[]]$Extra = @()) {
    New-FakeBuild $Path $Version $Extra
    Remove-Item -LiteralPath (Join-Path $Path $PortableStagingMarker)
    [void][IO.Directory]::CreateDirectory((Join-Path $Path 'data'))
    if ($Settings) { [IO.File]::WriteAllText((Join-Path $Path 'data\settings.json'), $Settings) }
}

# A build as earlier versions of Publish-Portable.ps1 left it: no manifest, no staging marker.
function New-LegacyBuild([string]$Path) {
    New-FakeBuild $Path 'legacy'
    Remove-Item -LiteralPath (Join-Path $Path $PortableStagingMarker)
    Remove-Item -LiteralPath (Join-Path $Path $PortableManifest)
}

function Write-Text([string]$Path, [string]$Text) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Text)
}

function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path -PathType Leaf) { [IO.File]::ReadAllText($Path) } else { '<missing>' } }

# A fresh root per case.
function New-Root([string]$Name) {
    $root = Join-Path $work $Name
    [void][IO.Directory]::CreateDirectory($root)
    New-PortableFolders $root
}

function New-Junction([string]$Path, [string]$Target) {
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & cmd.exe /c mklink /J "$Path" "$Target" 2>$null | Out-Null } finally { $ErrorActionPreference = $preference }
}

function Get-Throws([scriptblock]$Action) {
    try { & $Action; return $null } catch { return $_.Exception.Message }
}

# Runs Enter-PortableLock on $Root in a child PowerShell process (same host, no window, -EncodedCommand so nothing
# needs quoting) and returns what it printed: "acquired" or "refused: <message>".
function Invoke-LockInChild([string]$Root) {
    $code = ". '$($library.Replace("'", "''"))'; try { `$lock = Enter-PortableLock '$($Root.Replace("'", "''"))'; 'acquired'; Exit-PortableLock `$lock } catch { 'refused: ' + `$_.Exception.Message }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { (@(& (Get-Process -Id $PID).Path -NoProfile -NonInteractive -EncodedCommand $encoded 2>$null) -join ' ').Trim() }
    finally { $ErrorActionPreference = $preference }
}

try {
    [void][IO.Directory]::CreateDirectory($work)

    # The first copy: staging becomes the live copy, without its staging marker, with its manifest.
    $f = New-Root 'first'
    New-FakeBuild $f.Staging 'v1'
    Invoke-PortableSwap $f
    Check 'the first build becomes the live copy' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and -not (Test-Path -LiteralPath $f.Staging)) 'live missing or staging left'
    Check 'the live copy loses its staging marker and keeps its manifest' (-not (Test-Path -LiteralPath (Join-Path $f.Live $PortableStagingMarker)) -and $null -ne (Read-PortableManifest $f.Live)) 'marker left or manifest missing'

    # An update: the build's files replaced, everything of the player's carried over, nothing left beside.
    $f = New-Root 'update'
    New-FakeCopy $f.Live 'v1' '{ "mine": true }' @('dropped.dll')
    Write-Text (Join-Path $f.Live 'data - Copy\settings.json') '{ "backup": true }'
    Write-Text (Join-Path $f.Live 'notes.txt') 'my notes'
    New-FakeBuild $f.Staging 'v2'
    Assert-PortableFoldersOwned $f
    Invoke-PortableSwap $f
    Check 'an update replaces the program files' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v2') 'still v1'
    Check 'an update keeps the data folder' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "mine": true }') 'settings lost'
    Check 'an update keeps the player''s other files (a backup, notes)' ((Read-Text (Join-Path $f.Live 'data - Copy\settings.json')) -eq '{ "backup": true }' -and (Read-Text (Join-Path $f.Live 'notes.txt')) -eq 'my notes') 'lost'
    Check 'an update drops a file only the old build had' (-not (Test-Path -LiteralPath (Join-Path $f.Live 'dropped.dll'))) 'still there'
    Check 'an update leaves no .old or .staging' (-not (Test-Path -LiteralPath $f.Old) -and -not (Test-Path -LiteralPath $f.Staging)) 'leftovers'
    Check 'the player''s files are not in the new manifest' (@(Get-PlayerEntries $f.Live) -join ',' -eq 'data,data - Copy,notes.txt') (@(Get-PlayerEntries $f.Live) -join ',')

    # A file of the player's with a name the new build uses: refused before anything moves.
    $f = New-Root 'clash'
    New-FakeCopy $f.Live 'v1' '{}'
    Write-Text (Join-Path $f.Live 'extra.txt') 'mine'
    New-FakeBuild $f.Staging 'v2' @('extra.txt')
    $message = Get-Throws { Invoke-PortableSwap $f }
    Check 'a player''s file named like a new build file is refused' ($message -and $message.Contains('cannot be carried over')) "message: $message"
    Check 'and nothing moved' ((Read-Text (Join-Path $f.Live 'extra.txt')) -eq 'mine' -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and (Test-Path -LiteralPath $f.Staging) -and -not (Test-Path -LiteralPath $f.Old)) 'changed'

    # Someone else's folder at the live name is refused and untouched.
    $f = New-Root 'foreign'
    Write-Text (Join-Path $f.Live 'important.txt') 'keep me'
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a folder without the marker is refused' ($message -and $message.Contains('not a portable copy')) "message: $message"
    Check 'and left as it was' ((Read-Text (Join-Path $f.Live 'important.txt')) -eq 'keep me') 'changed'

    # A link inside the live copy is refused.
    $f = New-Root 'link'
    New-FakeCopy $f.Live 'v1' '{}'
    $target = Join-Path $work 'link-target'
    Write-Text (Join-Path $target 'keep.txt') 'target'
    New-Junction (Join-Path $f.Live 'linked') $target
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a live copy holding a junction is refused' ($message -and $message.Contains('link or junction')) "message: $message"
    # Deletes the junction itself; Remove-Item on a junction asks for confirmation in Windows PowerShell 5.1.
    [IO.Directory]::Delete((Join-Path $f.Live 'linked'), $false)
    Check 'and the link target is untouched' ((Read-Text (Join-Path $target 'keep.txt')) -eq 'target') 'target changed'

    # Output roots: a drive root stays a root, never "C:" (that drive's current directory); one inside a copy and a
    # relative one are refused.
    $drive = [IO.Path]::GetPathRoot([Environment]::SystemDirectory)
    Check 'a drive root resolves to the root, not the current directory' ((Resolve-PortableRoot $drive) -eq $drive) "got $(Resolve-PortableRoot $drive)"
    $f = New-Root 'nested'
    New-FakeCopy $f.Live 'v1' '{}'
    $message = Get-Throws { Resolve-PortableRoot (Join-Path $f.Live 'inner') }
    Check 'an output root inside a portable copy is refused' ($message -and $message.Contains('inside the portable copy')) "message: $message"
    $message = Get-Throws { Resolve-PortableRoot 'portable' }
    Check 'a relative output root is refused' ($message -and $message.Contains('absolute')) "message: $message"

    # Cut short after step 1 (live renamed to old): undone.
    $f = New-Root 'undo'
    New-FakeCopy $f.Old 'v1' '{ "undo": 1 }'
    $said = Restore-InterruptedPortableUpdate $f
    Check 'a cut-short update is undone from .old' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "undo": 1 }' -and -not (Test-Path -LiteralPath $f.Old) -and $said) 'not restored'

    # Cut short in step 2 (data moved into staging, notes still in old): finished, the rest following.
    $f = New-Root 'finish'
    New-FakeBuild $f.Staging 'v2'
    Write-Text (Join-Path $f.Staging 'data\settings.json') '{ "finish": 2 }'
    New-FakeCopy $f.Old 'v1' $null
    [IO.Directory]::Delete((Join-Path $f.Old 'data'))
    Write-Text (Join-Path $f.Old 'notes.txt') 'still in old'
    $said = Restore-InterruptedPortableUpdate $f
    Check 'a cut-short update is finished from .staging' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "finish": 2 }' -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v2' -and $said) 'not finished'
    Check 'with the player''s other files following from .old' ((Read-Text (Join-Path $f.Live 'notes.txt')) -eq 'still in old') 'notes left behind'
    Check 'and without the staging marker' (-not (Test-Path -LiteralPath (Join-Path $f.Live $PortableStagingMarker))) 'marker left'
    Assert-PortableFoldersOwned $f
    Clear-PortableLeftovers $f
    Check 'and the old program files are cleared' (-not (Test-Path -LiteralPath $f.Old)) '.old left'

    # A leftover holding the player's files is refused, never deleted.
    $f = New-Root 'leftover-data'
    New-FakeCopy $f.Live 'v2' '{}'
    New-FakeCopy $f.Old 'v1' '{ "old": "data" }'
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a leftover .old holding data is refused' ($message -and $message.Contains('which may be yours')) "message: $message"
    $message = Get-Throws { Clear-PortableLeftovers $f }
    Check 'and never deleted' ($message -and (Read-Text (Join-Path $f.Old 'data\settings.json')) -eq '{ "old": "data" }') 'deleted'

    # A staging folder holding a data folder is never deleted, even without a manifest.
    $f = New-Root 'staging-data'
    [void][IO.Directory]::CreateDirectory($f.Staging)
    [IO.File]::WriteAllText((Join-Path $f.Staging $PortableStagingMarker), $PortableStagingMarkerText)
    Write-Text (Join-Path $f.Staging 'data\settings.json') 'precious'
    $message = Get-Throws { Remove-PortableFolder $f.Staging $null }
    Check 'a staging folder holding data is not deleted' ($message -and (Read-Text (Join-Path $f.Staging 'data\settings.json')) -eq 'precious') 'deleted'

    # A leftover someone else made at the staging name is refused.
    $f = New-Root 'leftover-foreign'
    Write-Text (Join-Path $f.Staging 'important.txt') 'keep me'
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'someone else''s folder at the staging name is refused' ($message -and $message.Contains('not made by Publish-Portable')) "message: $message"

    # A delete cut short by an open file keeps the markers, so the next run still recognises and finishes it.
    $f = New-Root 'held-delete'
    New-FakeCopy $f.Live 'v2' '{}'
    New-FakeBuild $f.Old 'v1'
    Remove-Item -LiteralPath (Join-Path $f.Old $PortableStagingMarker)
    $held = [IO.File]::Open((Join-Path $f.Old 'wwwroot\index.html'), 'Open', 'Read', 'None')
    try { $message = Get-Throws { Clear-PortableLeftovers $f } }
    finally { $held.Dispose() }
    Check 'a delete stopped by an open file fails' ($null -ne $message) 'it did not fail'
    Check 'and leaves the folder marked and accepted' ((Test-PortableCopy $f.Old) -and -not (Get-Throws { Assert-PortableFoldersOwned $f })) 'marker gone or refused'
    Clear-PortableLeftovers $f
    Check 'and the next run deletes it' (-not (Test-Path -LiteralPath $f.Old)) 'still there'

    # Earlier stamped builds: ours with only a build's files removed, anything more kept, anything else untouched.
    $f = New-Root 'legacy'
    New-FakeCopy $f.Live 'v2' '{}'
    $build = Read-PortableManifest $f.Live
    New-LegacyBuild (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341\ScarletMeter')
    New-LegacyBuild (Join-Path $f.Root 'ScarletMeter-0d9af74-upstream-v1.11.5.341\ScarletMeter')
    Write-Text (Join-Path $f.Root 'ScarletMeter-0d9af74-upstream-v1.11.5.341\ScarletMeter\data\settings.json') '{ "used": true }'
    New-LegacyBuild (Join-Path $f.Root 'ScarletMeter-1111111-upstream-v1.11.5.341\ScarletMeter')
    Write-Text (Join-Path $f.Root 'ScarletMeter-1111111-upstream-v1.11.5.341\ScarletMeter\my-notes.txt') 'mine'
    Write-Text (Join-Path $f.Root 'ScarletMeter-notes-upstream-mine\notes.txt') 'mine'
    Write-PortableZip $f.Live (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341-win-x64.zip')
    Write-Text (Join-Path $f.Root 'ScarletMeter-2222222-upstream-v1.11.5.341-win-x64.zip') 'not a zip of ours'
    $result = Remove-LegacyPortableBuilds $f.Root $build
    Check 'an earlier build holding only a build''s files is removed' (-not (Test-Path -LiteralPath (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341'))) 'still there'
    Check 'its stamped zip is removed' (-not (Test-Path -LiteralPath (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341-win-x64.zip'))) 'still there'
    Check 'an earlier build holding data is kept' ((Read-Text (Join-Path $f.Root 'ScarletMeter-0d9af74-upstream-v1.11.5.341\ScarletMeter\data\settings.json')) -eq '{ "used": true }' -and ($result.Kept -contains 'ScarletMeter-0d9af74-upstream-v1.11.5.341')) 'removed or not reported'
    Check 'an earlier build holding a file of the player''s is kept' ((Read-Text (Join-Path $f.Root 'ScarletMeter-1111111-upstream-v1.11.5.341\ScarletMeter\my-notes.txt')) -eq 'mine') 'removed'
    Check 'a stamped-looking folder without the marker is untouched' ((Read-Text (Join-Path $f.Root 'ScarletMeter-notes-upstream-mine\notes.txt')) -eq 'mine') 'touched'
    Check 'a stamped-looking zip that is not ours is untouched' ((Read-Text (Join-Path $f.Root 'ScarletMeter-2222222-upstream-v1.11.5.341-win-x64.zip')) -eq 'not a zip of ours') 'deleted'

    # The zip: only the build's files under ScarletMeter/, forward slashes, never the player's.
    $f = New-Root 'zip'
    New-FakeCopy $f.Live 'v3' 'private'
    Write-Text (Join-Path $f.Live 'notes.txt') 'private'
    Write-PortableZip $f.Live $f.Zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($f.Zip)
    try { $entries = @($archive.Entries | ForEach-Object FullName | Sort-Object) } finally { $archive.Dispose() }
    $expected = @("ScarletMeter/$PortableManifest", "ScarletMeter/$PortableMarker", 'ScarletMeter/ScarletMeter.exe', 'ScarletMeter/wwwroot/index.html') | Sort-Object
    Check 'the zip holds exactly the build, its manifest and the marker' (($entries -join '|') -eq ($expected -join '|')) ($entries -join ', ')
    Write-PortableZip $f.Live $f.Zip
    Check 'a second zip replaces the first' (@(Get-ChildItem -LiteralPath $f.Root -Filter '*.zip*').Count -eq 1) 'extra files'
    Check 'the zip is recognised as ours, another file by that name is not' ((Test-PortableZip $f.Zip) -and -not (Test-PortableZip (Join-Path $f.Live 'notes.txt'))) 'wrong'

    # A meter running from the copy blocks the update: a hidden ping.exe renamed ScarletMeter.exe stands in.
    $f = New-Root 'running'
    New-FakeCopy $f.Live 'v1' '{}'
    Copy-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('System')) 'PING.EXE') -Destination (Join-Path $f.Live 'ScarletMeter.exe') -Force
    $process = Start-Process -FilePath (Join-Path $f.Live 'ScarletMeter.exe') -ArgumentList '-n', '30', '127.0.0.1' -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 500
        $live = Resolve-FinalPath $f.Live
        $message = Get-Throws { Assert-MeterNotRunningFrom @($live) 'run Publish-Portable.ps1 again' }
        Check 'a meter running from the copy is refused' ($message -and $message.Contains('Close it, then run Publish-Portable.ps1 again')) "message: $message"
    }
    finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }

    # A second run on the same output root at once is refused; once the first ends, it may go.
    $f = New-Root 'lock'
    $lock = Enter-PortableLock $f.Root
    try { $while = Invoke-LockInChild $f.Root }
    finally { Exit-PortableLock $lock }
    Check 'a second run on the same output root is refused' ($while -like 'refused: Another Publish-Portable.ps1*') "child said: $while"
    $after = Invoke-LockInChild $f.Root
    Check 'and allowed once the first has ended' ($after -eq 'acquired') "child said: $after"

    # A swap that fails at step 3 (a file held open in the new build) is undone with the player's files back.
    $f = New-Root 'failed-swap'
    New-FakeCopy $f.Live 'v1' '{ "keep": "me" }'
    Write-Text (Join-Path $f.Live 'notes.txt') 'mine'
    New-FakeBuild $f.Staging 'v2'
    $held = [IO.File]::Open((Join-Path $f.Staging 'wwwroot\index.html'), 'Open', 'Read', 'None')
    try { $message = Get-Throws { Invoke-PortableSwap $f } }
    finally { $held.Dispose() }
    Check 'a failed swap says it was undone' ($message -and $message.Contains('was undone')) "message: $message"
    Check 'and the copy and the player''s files are as they were' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and (Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "keep": "me" }' -and (Read-Text (Join-Path $f.Live 'notes.txt')) -eq 'mine' -and -not (Test-Path -LiteralPath $f.Old)) 'not restored'
    Check 'and the new build holds nothing of the player''s' (-not @(Get-PlayerEntries $f.Staging).Count) 'files left in staging'

    # A failure at step 1 (the copy in use) changes nothing and says what to close.
    $f = New-Root 'step1'
    New-FakeCopy $f.Live 'v1' '{}'
    New-FakeBuild $f.Staging 'v2'
    $script:failMoves = @('*\ScarletMeter>*\ScarletMeter.old')
    try { $message = Get-Throws { Invoke-PortableSwap $f } } finally { $script:failMoves = @() }
    Check 'a copy in use is left as it was, with what to close' ($message -and $message.Contains('Nothing was changed') -and $message.Contains('Explorer') -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1') "message: $message"

    # Step 3 fails and a file of the player's cannot move back: the live name stays free, the message says where
    # everything is, and the next run finishes the update with every file of the player's in the copy.
    $f = New-Root 'stranded'
    New-FakeCopy $f.Live 'v1' '{ "stranded": true }'
    Write-Text (Join-Path $f.Live 'notes.txt') 'mine'
    New-FakeBuild $f.Staging 'v2'
    $script:failMoves = @('*\ScarletMeter.staging>*\ScarletMeter', '*\ScarletMeter.staging\notes.txt>*')
    try { $message = Get-Throws { Invoke-PortableSwap $f } } finally { $script:failMoves = @() }
    Check 'an undo that fails says so and where the files are' ($message -and $message.Contains('could not be moved back') -and $message.Contains('notes.txt') -and -not $message.Contains('as they were')) "message: $message"
    Check 'and leaves the live name free' (-not (Test-Path -LiteralPath $f.Live)) 'live exists'
    $said = Restore-InterruptedPortableUpdate $f
    Assert-PortableFoldersOwned $f
    Clear-PortableLeftovers $f
    Check 'the next run finishes it with every file of the player''s' ($said -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v2' -and (Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "stranded": true }' -and (Read-Text (Join-Path $f.Live 'notes.txt')) -eq 'mine' -and -not (Test-Path -LiteralPath $f.Old) -and -not (Test-Path -LiteralPath $f.Staging)) "said: $said"

    # Step 3 fails, everything moves back, and the last undo rename fails: the message says the copy is in .old,
    # and the next run puts it back.
    $f = New-Root 'undo-fails'
    New-FakeCopy $f.Live 'v1' '{ "back": true }'
    New-FakeBuild $f.Staging 'v2'
    $script:failMoves = @('*\ScarletMeter.staging>*\ScarletMeter', '*\ScarletMeter.old>*\ScarletMeter')
    try { $message = Get-Throws { Invoke-PortableSwap $f } } finally { $script:failMoves = @() }
    Check 'a failed last undo says the copy is in .old' ($message -and $message.Contains('are in') -and $message.Contains('.old') -and -not $message.Contains('as they were')) "message: $message"
    $said = Restore-InterruptedPortableUpdate $f
    Check 'and the next run puts it back' ($said -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and (Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "back": true }') "said: $said"
}
finally {
    $script:failMoves = @()
    # Junctions first, so the clean-up never reaches a link's target; only this run's folder.
    if (Test-Path -LiteralPath $work) {
        Get-ChildItem -LiteralPath $work -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | ForEach-Object { [IO.Directory]::Delete($_.FullName, $false) }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures.Count) { throw "$($failures.Count) portable folder case(s) failed: $($failures -join '; ')" }
Write-Host 'The live portable copy''s folder rules hold.'

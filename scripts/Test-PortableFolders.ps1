<#
.SYNOPSIS
Checks the live portable copy's folder rules (scripts\PortableFolders.ps1) against throwaway folders.

.DESCRIPTION
Builds nothing and opens no window: each case makes fake builds (a few files and the markers) in a new folder under
the temp directory, runs the library's functions on them and checks what moved, what stayed and what was refused.
Covers the first copy, an update that keeps the data folder, refusing folders the script did not make and links,
finishing and undoing a cut-short update, leftovers with and without data, removing earlier stamped builds, the
zip's contents, refusing while a ScarletMeter.exe runs from the copy (a hidden, renamed ping.exe stands in), and an
update that fails part way being undone. Prints ok or FAIL per case and throws when any failed. Paths derive from
this script's location and the platform's temp folder. Targets Windows PowerShell 5.1 and pwsh.

.EXAMPLE
& "<checkout>\scripts\Test-PortableFolders.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PortableFolders.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-portable-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = New-Object 'System.Collections.Generic.List[string]'

function Pass([string]$Name) { Write-Host "ok   $Name" }
function Fail([string]$Name, [string]$Why) { $failures.Add("$Name`: $Why"); Write-Host "FAIL $Name ($Why)" }
function Check([string]$Name, [bool]$Condition, [string]$Why) { if ($Condition) { Pass $Name } else { Fail $Name $Why } }

# A fake checked build in $Path, as Publish-Portable.ps1 leaves staging before the swap.
function New-FakeBuild([string]$Path, [string]$Version) {
    [void][IO.Directory]::CreateDirectory((Join-Path $Path 'wwwroot'))
    [IO.File]::WriteAllText((Join-Path $Path 'ScarletMeter.exe'), $Version)
    [IO.File]::WriteAllText((Join-Path $Path 'wwwroot\index.html'), $Version)
    [IO.File]::WriteAllText((Join-Path $Path $PortableStagingMarker), $PortableStagingMarkerText)
    [IO.File]::WriteAllText((Join-Path $Path $PortableMarker), $PortableMarkerText + "`r`n")
}

# A live copy with a data folder holding a settings file.
function New-FakeCopy([string]$Path, [string]$Version, [string]$Settings) {
    New-FakeBuild $Path $Version
    Remove-Item -LiteralPath (Join-Path $Path $PortableStagingMarker)
    [void][IO.Directory]::CreateDirectory((Join-Path $Path 'data'))
    if ($Settings) { [IO.File]::WriteAllText((Join-Path $Path 'data\settings.json'), $Settings) }
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

try {
    [void][IO.Directory]::CreateDirectory($work)

    # The first copy: staging becomes the live copy, without its staging marker.
    $f = New-Root 'first'
    New-FakeBuild $f.Staging 'v1'
    Invoke-PortableSwap $f
    Check 'the first build becomes the live copy' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and -not (Test-Path -LiteralPath $f.Staging)) 'live missing or staging left'
    Check 'the live copy loses its staging marker' (-not (Test-Path -LiteralPath (Join-Path $f.Live $PortableStagingMarker))) 'marker left'

    # An update: new program files, same data, nothing left beside.
    $f = New-Root 'update'
    New-FakeCopy $f.Live 'v1' '{ "mine": true }'
    New-FakeBuild $f.Staging 'v2'
    Assert-PortableFoldersOwned $f
    Invoke-PortableSwap $f
    Check 'an update replaces the program files' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v2') 'still v1'
    Check 'an update keeps the data folder' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "mine": true }') 'settings lost'
    Check 'an update leaves no .old or .staging' (-not (Test-Path -LiteralPath $f.Old) -and -not (Test-Path -LiteralPath $f.Staging)) 'leftovers'

    # Someone else's folder at the live name is refused and untouched.
    $f = New-Root 'foreign'
    [void][IO.Directory]::CreateDirectory($f.Live)
    [IO.File]::WriteAllText((Join-Path $f.Live 'important.txt'), 'keep me')
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a folder without the marker is refused' ($message -and $message.Contains('not a portable copy')) "message: $message"
    Check 'and left as it was' ((Read-Text (Join-Path $f.Live 'important.txt')) -eq 'keep me') 'changed'

    # A link inside the live copy is refused.
    $f = New-Root 'link'
    New-FakeCopy $f.Live 'v1' '{}'
    $target = Join-Path $work 'link-target'
    [void][IO.Directory]::CreateDirectory($target)
    [IO.File]::WriteAllText((Join-Path $target 'keep.txt'), 'target')
    New-Junction (Join-Path $f.Live 'linked') $target
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a live copy holding a junction is refused' ($message -and $message.Contains('link or junction')) "message: $message"
    # Deletes the junction itself; Remove-Item on a junction asks for confirmation in Windows PowerShell 5.1.
    [IO.Directory]::Delete((Join-Path $f.Live 'linked'), $false)
    Check 'and the link target is untouched' ((Read-Text (Join-Path $target 'keep.txt')) -eq 'target') 'target changed'

    # Cut short after step 1 (live renamed to old): undone.
    $f = New-Root 'undo'
    New-FakeCopy $f.Old 'v1' '{ "undo": 1 }'
    $said = Restore-InterruptedPortableUpdate $f
    Check 'a cut-short update is undone from .old' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "undo": 1 }' -and -not (Test-Path -LiteralPath $f.Old) -and $said) 'not restored'

    # Cut short after step 2 (data moved into staging): finished.
    $f = New-Root 'finish'
    New-FakeBuild $f.Staging 'v2'
    [void][IO.Directory]::CreateDirectory((Join-Path $f.Staging 'data'))
    [IO.File]::WriteAllText((Join-Path $f.Staging 'data\settings.json'), '{ "finish": 2 }')
    New-FakeBuild $f.Old 'v1'
    $said = Restore-InterruptedPortableUpdate $f
    Check 'a cut-short update is finished from .staging' ((Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "finish": 2 }' -and (Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v2' -and $said) 'not finished'
    Assert-PortableFoldersOwned $f
    Clear-PortableLeftovers $f
    Check 'and the old program files are cleared' (-not (Test-Path -LiteralPath $f.Old)) '.old left'

    # A leftover holding data is refused, never deleted.
    $f = New-Root 'leftover-data'
    New-FakeCopy $f.Live 'v2' '{}'
    New-FakeCopy $f.Old 'v1' '{ "old": "data" }'
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'a leftover .old holding data is refused' ($message -and $message.Contains('holds a data folder')) "message: $message"
    Clear-PortableLeftovers $f
    Check 'and never deleted' ((Read-Text (Join-Path $f.Old 'data\settings.json')) -eq '{ "old": "data" }') 'deleted'

    # A leftover someone else made at the staging name is refused.
    $f = New-Root 'leftover-foreign'
    [void][IO.Directory]::CreateDirectory($f.Staging)
    [IO.File]::WriteAllText((Join-Path $f.Staging 'important.txt'), 'keep me')
    $message = Get-Throws { Assert-PortableFoldersOwned $f }
    Check 'someone else''s folder at the staging name is refused' ($message -and $message.Contains('not made by Publish-Portable')) "message: $message"

    # Earlier stamped builds: ours without data removed, with data kept, anything else untouched.
    $f = New-Root 'legacy'
    New-FakeBuild (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341\ScarletMeter') 'old'
    [IO.File]::WriteAllText((Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341-win-x64.zip'), 'zip')
    New-FakeCopy (Join-Path $f.Root 'ScarletMeter-0d9af74-upstream-v1.11.5.341\ScarletMeter') 'used' '{ "used": true }'
    [void][IO.Directory]::CreateDirectory((Join-Path $f.Root 'ScarletMeter-notes-upstream-mine'))
    [IO.File]::WriteAllText((Join-Path $f.Root 'ScarletMeter-notes-upstream-mine\notes.txt'), 'mine')
    $result = Remove-LegacyPortableBuilds $f.Root
    Check 'an earlier build without data is removed' (-not (Test-Path -LiteralPath (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341'))) 'still there'
    Check 'its stamped zip is removed' (-not (Test-Path -LiteralPath (Join-Path $f.Root 'ScarletMeter-6e67467-upstream-v1.11.5.341-win-x64.zip'))) 'still there'
    Check 'an earlier build holding data is kept' ((Read-Text (Join-Path $f.Root 'ScarletMeter-0d9af74-upstream-v1.11.5.341\ScarletMeter\data\settings.json')) -eq '{ "used": true }' -and ($result.Kept -contains 'ScarletMeter-0d9af74-upstream-v1.11.5.341')) 'removed or not reported'
    Check 'a stamped-looking folder without the marker is untouched' ((Read-Text (Join-Path $f.Root 'ScarletMeter-notes-upstream-mine\notes.txt')) -eq 'mine') 'touched'

    # The zip: the build under ScarletMeter/, forward slashes, no staging marker, no data.
    $f = New-Root 'zip'
    New-FakeBuild $f.Staging 'v3'
    [void][IO.Directory]::CreateDirectory((Join-Path $f.Staging 'data'))
    [IO.File]::WriteAllText((Join-Path $f.Staging 'data\settings.json'), 'private')
    Write-PortableZip $f.Staging $f.Zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($f.Zip)
    try { $entries = @($archive.Entries | ForEach-Object FullName) } finally { $archive.Dispose() }
    Check 'the zip holds the build under ScarletMeter/' (($entries -contains 'ScarletMeter/ScarletMeter.exe') -and ($entries -contains 'ScarletMeter/wwwroot/index.html') -and ($entries -contains "ScarletMeter/$PortableMarker")) ($entries -join ', ')
    Check 'the zip leaves out the staging marker and any data' (-not ($entries | Where-Object { $_ -like "*$PortableStagingMarker" -or $_ -like 'ScarletMeter/data/*' })) ($entries -join ', ')
    Write-PortableZip $f.Staging $f.Zip
    Check 'a second zip replaces the first' (@(Get-ChildItem -LiteralPath $f.Root -Filter '*.zip*').Count -eq 1) 'extra files'

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

    # A swap that fails at step 3 (a file held open in the new build) is undone with the data back in place.
    $f = New-Root 'failed-swap'
    New-FakeCopy $f.Live 'v1' '{ "keep": "me" }'
    New-FakeBuild $f.Staging 'v2'
    $held = [IO.File]::Open((Join-Path $f.Staging 'wwwroot\index.html'), 'Open', 'Read', 'None')
    try { $message = Get-Throws { Invoke-PortableSwap $f } }
    finally { $held.Dispose() }
    Check 'a failed swap says it was undone' ($message -and $message.Contains('was undone')) "message: $message"
    Check 'and the copy and its data are as they were' ((Read-Text (Join-Path $f.Live 'ScarletMeter.exe')) -eq 'v1' -and (Read-Text (Join-Path $f.Live 'data\settings.json')) -eq '{ "keep": "me" }' -and -not (Test-Path -LiteralPath $f.Old)) 'not restored'
    Check 'and the new build has no data in it' (-not (Test-HoldsData $f.Staging)) 'data left in staging'
}
finally {
    # Junctions first, so the clean-up never reaches a link's target; only this run's folder.
    if (Test-Path -LiteralPath $work) {
        Get-ChildItem -LiteralPath $work -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | ForEach-Object { [IO.Directory]::Delete($_.FullName, $false) }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures.Count) { throw "$($failures.Count) portable folder case(s) failed: $($failures -join '; ')" }
Write-Host 'The live portable copy''s folder rules hold.'

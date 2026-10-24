<#
.SYNOPSIS
Builds a portable Scarlet Meter: a folder and a zip to unzip anywhere and run, like RATmeter's release zip.

.DESCRIPTION
Runs scripts\Build.ps1 -Configuration Release (restore with audit as errors, build, tests), publishes a
self-contained single-file win-x64 build (the .NET runtime is bundled, so nothing needs installing but Npcap) into a
new folder, checks it with scripts\Test-PublishLayout.ps1, adds the ScarletMeter.portable marker that makes the
meter keep its settings, history and logs in a "data" folder beside the exe, and zips the folder.

Output, under the output root:
  ScarletMeter-<commit>-upstream-<release>\ScarletMeter\   the portable folder; run ScarletMeter.exe from it
  ScarletMeter-<commit>-upstream-<release>-win-x64.zip     the same folder zipped, with "ScarletMeter" at its top

To update a portable copy: close the meter and extract the new zip over the old ScarletMeter folder. The zip holds
no data folder, so settings and history stay. This script never deletes or overwrites anything: when a name is
taken, the new build gets a counter. Every path derives from this script's location; an output root given must
be absolute. Targets Windows PowerShell 5.1 and pwsh.

.PARAMETER OutputRoot
Absolute folder for the build folder and the zip. Default: "<checkout>\portable", which git ignores.

.PARAMETER SkipTests
Builds without running the tests (CI, where they ran in an earlier step).

.EXAMPLE
& "<checkout>\scripts\Publish-Portable.ps1"
#>
[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src\ScarletMeter.App\ScarletMeter.App.csproj'
# Must match PortableLayout.MarkerFileName in the app (PortableLayoutTests checks).
$portableMarker = 'ScarletMeter.portable'

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'portable' }
elseif ($OutputRoot -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') { throw "OutputRoot must be an absolute path, not `"$OutputRoot`"." }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\', '/')

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

# The build is named after what it was made from, as the app's build stamp is.
$upstreamDirectory = Join-Path $repoRoot 'upstream'
$upstreamRelease = Get-GitLine $upstreamDirectory @('describe', '--tags', '--exact-match', 'HEAD')
if (-not $upstreamRelease) { $upstreamRelease = Get-GitLine $upstreamDirectory @('rev-parse', '--short', 'HEAD') }
if (-not $upstreamRelease) { $upstreamRelease = 'unknown' }
$commit = Get-GitLine $repoRoot @('rev-parse', '--short', 'HEAD')
if (-not $commit) { $commit = 'unknown' }
elseif (Get-GitLine $repoRoot @('status', '--porcelain', '--untracked-files=no')) {
    Write-Warning 'This checkout has uncommitted changes: they are in the build, but its stamped commit does not show them.'
    $commit += '-dirty'
}
$stem = ("ScarletMeter-$commit-upstream-$upstreamRelease") -replace '[^A-Za-z0-9._-]', '_'

# A new name every time, so an earlier build or zip is never replaced.
$name = $stem
for ($n = 2; (Test-Path -LiteralPath (Join-Path $OutputRoot $name)) -or (Test-Path -LiteralPath (Join-Path $OutputRoot "$name-win-x64.zip")); $n++) {
    if ($n -gt 99) { throw "Too many builds named $stem in $OutputRoot; move some away." }
    $name = "$stem-$n"
}
$buildFolder = Join-Path $OutputRoot $name
$meterFolder = Join-Path $buildFolder 'ScarletMeter'
$zip = Join-Path $OutputRoot "$name-win-x64.zip"

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release -SkipTests:$SkipTests

$dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
[void][IO.Directory]::CreateDirectory($meterFolder)
# global.json (the SDK band) is discovered from the working directory, so anchor it as Build.ps1 does.
Push-Location -LiteralPath $repoRoot
try {
    & $dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $meterFolder
    if ($LASTEXITCODE) { throw 'Publish failed.' }
}
finally {
    Pop-Location
}
& (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $meterFolder

[IO.File]::WriteAllText((Join-Path $meterFolder $portableMarker),
    "This file makes Scarlet Meter keep its settings, history and logs in the data folder beside it.`r`n" +
    "Delete it to use the per-user data folder (%LocalAppData%\ScarletMeter) instead.`r`n")

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($meterFolder, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

Write-Host "Portable folder: $meterFolder"
Write-Host "Zip:             $zip"
Write-Host 'Run ScarletMeter.exe from the folder, or unzip the zip anywhere you can write to (not Program Files).'

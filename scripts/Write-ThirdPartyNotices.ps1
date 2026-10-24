<#
.SYNOPSIS
Writes the NuGet package table in THIRD-PARTY-NOTICES.md from the app's restore.

.DESCRIPTION
Reads src\ScarletMeter.App\obj\project.assets.json (written by a restore or build of the app, so run
scripts\Build.ps1 first) and lists every package of its first target, the published program's packages, with the
licence each package declares in its .nuspec in the local NuGet cache: an SPDX expression, the name of a licence
file it ships, or the licence URL it links. Replaces the lines between "<!-- packages:begin -->" and
"<!-- packages:end -->" in THIRD-PARTY-NOTICES.md, leaving the rest of the file as written.
ThirdPartyNoticesTests fails when a package is missing, so run this after any package change. Every path derives
from this script's location and the restore's own record of the package folder. Targets Windows PowerShell 5.1
and pwsh.

.EXAMPLE
& "<checkout>\scripts\Write-ThirdPartyNotices.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$assetsPath = Join-Path $repoRoot 'src\ScarletMeter.App\obj\project.assets.json'
$noticesPath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
if (-not (Test-Path -LiteralPath $assetsPath)) { throw "No restore of the app at $assetsPath; run scripts\Build.ps1 first." }

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$cache = $assets.project.restore.packagesPath
if (-not $cache -or -not (Test-Path -LiteralPath $cache)) { throw 'The restore does not record a NuGet package folder.' }
$target = @($assets.targets.PSObject.Properties)[0]
$packages = @($target.Value.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name } | Sort-Object)

$rows = foreach ($package in $packages) {
    $id, $version = $package -split '/'
    $folder = Join-Path $cache (Join-Path $id.ToLowerInvariant() $version)
    $nuspec = Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    $licence = 'see package'
    $project = ''
    if ($nuspec) {
        [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
        $meta = $xml.package.metadata
        if ($meta.license) {
            $value = if ($meta.license.'#text') { $meta.license.'#text' } else { "$($meta.license)" }
            $licence = if ($meta.license.type -eq 'file') { "file in package: $value" } else { $value }
        }
        elseif ($meta.licenseUrl) { $licence = "<$($meta.licenseUrl)>" }
        if ($meta.projectUrl) { $project = "<$($meta.projectUrl)>" }
    }
    "| $id | $version | $licence | $project |"
}

$table = @("Target ``$($target.Name)``, $($packages.Count) packages.", '', '| Package | Version | Licence | Project |', '| --- | --- | --- | --- |') + $rows
$text = [IO.File]::ReadAllText($noticesPath)
$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$begin = '<!-- packages:begin -->'
$end = '<!-- packages:end -->'
$start = $text.IndexOf($begin); $stop = $text.IndexOf($end)
if ($start -lt 0 -or $stop -lt $start) { throw "THIRD-PARTY-NOTICES.md has no $begin ... $end section." }
$updated = $text.Substring(0, $start + $begin.Length) + $newline + ($table -join $newline) + $newline + $text.Substring($stop)
[IO.File]::WriteAllText($noticesPath, $updated, (New-Object Text.UTF8Encoding $false))
Write-Host "Listed $($packages.Count) packages in $noticesPath."

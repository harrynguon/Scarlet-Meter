<#
.SYNOPSIS
Writes the NuGet package table in THIRD-PARTY-NOTICES.md and the packages' own licence files in licenses\packages,
from the app's restore.

.DESCRIPTION
Reads src\ScarletMeter.App\obj\project.assets.json (written by a restore or build of the app, so run
scripts\Build.ps1 first) and lists every package of its first target that ships something in the program: one with
compile, runtime, native, resource or runtime-specific assets. Build-only packages (analyzers, the trimming tasks a
runtime-specific restore adds, empty meta-packages) ship nothing and are left out, so the table is the same after
any restore. For each package it records the licence its .nuspec in the local NuGet cache declares (an SPDX
expression, whose text is in licenses\, a licence file it ships, or the licence URL it links), and copies every
licence, notice and third-party-notices file the package ships into licenses\packages, each distinct text once,
named after the first package (alphabetically) that ships it. Replaces the lines between "<!-- packages:begin -->"
and "<!-- packages:end -->" in THIRD-PARTY-NOTICES.md, leaving the rest of the file as written, and removes files in
licenses\packages that no package ships any more. Fails when a package's .nuspec is missing.
ThirdPartyNoticesTests fails when the table or the copies fall behind the restore, so run this after any package
change. Every path derives from this script's location and the restore's own record of the package folder. Targets
Windows PowerShell 5.1 and pwsh.

.EXAMPLE
& "<checkout>\scripts\Write-ThirdPartyNotices.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$assetsPath = Join-Path $repoRoot 'src\ScarletMeter.App\obj\project.assets.json'
$noticesPath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$packageLicences = Join-Path $repoRoot 'licenses\packages'
if (-not (Test-Path -LiteralPath $assetsPath)) { throw "No restore of the app at $assetsPath; run scripts\Build.ps1 first." }

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$cache = $assets.project.restore.packagesPath
if (-not $cache -or -not (Test-Path -LiteralPath $cache)) { throw 'The restore does not record a NuGet package folder.' }
$target = @($assets.targets.PSObject.Properties)[0]
# The asset kinds that put a package's files into the program; ThirdPartyNoticesTests uses the same list.
$shippedAssets = @('compile', 'runtime', 'native', 'resource', 'runtimeTargets')
$packages = @($target.Value.PSObject.Properties | Where-Object {
        $_.Value.type -eq 'package' -and @($_.Value.PSObject.Properties.Name | Where-Object { $shippedAssets -contains $_ }).Count -gt 0
    } | ForEach-Object { $_.Name } | Sort-Object)

$sha = [Security.Cryptography.SHA256]::Create()
$copies = [ordered]@{}   # file name in licenses\packages -> bytes
$byHash = @{}            # content hash -> file name
$rows = foreach ($package in $packages) {
    $id, $version = $package -split '/'
    $folder = Join-Path $cache (Join-Path $id.ToLowerInvariant() $version)
    $nuspec = Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspec) { throw "No .nuspec for $id $version in $folder; restore the app again." }
    [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $meta = $xml.package.metadata
    $licence = 'none declared'
    if ($meta.license) {
        $value = if ($meta.license.'#text') { $meta.license.'#text' } else { "$($meta.license)" }
        $licence = if ($meta.license.type -eq 'file') { "file: $value" } else { $value }
    }
    elseif ($meta.licenseUrl) { $licence = "<$($meta.licenseUrl)>" }
    $project = if ($meta.projectUrl) { "<$($meta.projectUrl)>" } else { '' }

    # The package's own licence and notice files, each distinct text copied once.
    $notices = foreach ($file in @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICEN[CS]E|NOTICE|THIRD-PARTY-NOTICES)' } | Sort-Object Name)) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $hash = [BitConverter]::ToString($sha.ComputeHash($bytes))
        if (-not $byHash.ContainsKey($hash)) {
            $name = "$id.$($file.Name)"
            $byHash[$hash] = $name
            $copies[$name] = $bytes
        }
        $byHash[$hash]
    }
    "| $id | $version | $licence | $(@($notices) -join ', ') | $project |"
}
$sha.Dispose()

$table = @("Target ``$($target.Name)``, $($packages.Count) packages.", '',
    '| Package | Version | Licence | Its own notices, in `licenses/packages/` | Project |', '| --- | --- | --- | --- | --- |') + $rows
$text = [IO.File]::ReadAllText($noticesPath)
$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$begin = '<!-- packages:begin -->'
$end = '<!-- packages:end -->'
$start = $text.IndexOf($begin); $stop = $text.IndexOf($end)
if ($start -lt 0 -or $stop -lt $start) { throw "THIRD-PARTY-NOTICES.md has no $begin ... $end section." }
$updated = $text.Substring(0, $start + $begin.Length) + $newline + ($table -join $newline) + $newline + $text.Substring($stop)
[IO.File]::WriteAllText($noticesPath, $updated, (New-Object Text.UTF8Encoding $false))

[void][IO.Directory]::CreateDirectory($packageLicences)
foreach ($stale in @(Get-ChildItem -LiteralPath $packageLicences -File | Where-Object { -not $copies.Contains($_.Name) })) {
    [IO.File]::Delete($stale.FullName)
}
foreach ($name in $copies.Keys) { [IO.File]::WriteAllBytes((Join-Path $packageLicences $name), $copies[$name]) }
Write-Host "Listed $($packages.Count) packages in $noticesPath; $($copies.Count) licence and notice files in $packageLicences."

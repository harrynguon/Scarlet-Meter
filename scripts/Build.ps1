<#
.SYNOPSIS
Restores, builds and tests Scarlet Meter with the .NET 10 SDK, from any working directory.

.DESCRIPTION
Every path is derived from this script's own location, so the same command works for anyone on any machine.
Set SCARLET_DOTNET to a dotnet.exe when the .NET 10 SDK is not the one on PATH.

.EXAMPLE
& "<checkout>\scripts\Build.ps1" -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repoRoot 'ScarletMeter.slnx'
$upstreamProject = Join-Path $repoRoot 'upstream\AionDpsMeter.Services\AionDpsMeter.Services.csproj'
$dotnet = if ($env:SCARLET_DOTNET) { $env:SCARLET_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }

if (-not (Test-Path -LiteralPath $upstreamProject)) {
    throw "The upstream submodule is empty. Run: git -C `"$repoRoot`" submodule update --init"
}
$sdks = @(& $dotnet --list-sdks)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw "A .NET 10 SDK is required. $dotnet lists: $($sdks -join ', ')"
}

# global.json (SDK band and the test runner) is discovered from the working directory, so anchor it explicitly.
Push-Location -LiteralPath $repoRoot
try {
    & $dotnet restore $solution '-warnaserror:NU1901,NU1902,NU1903,NU1904'
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    & $dotnet build $solution --no-restore --configuration $Configuration
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & $dotnet test --solution $solution --no-restore --configuration $Configuration
        if ($LASTEXITCODE) { throw 'Tests failed.' }
    }
}
finally {
    Pop-Location
}

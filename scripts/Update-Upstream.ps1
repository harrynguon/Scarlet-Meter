<#
.SYNOPSIS
Moves the upstream submodule to a RATmeter release tag, then builds and tests Scarlet Meter against it.

.DESCRIPTION
Releases only: the tag must look like v1.11.5.341 and exist on upstream. Never point the submodule at
upstream's dev branch or an unreleased commit (AGENTS.md, "Upstream boundary"). Paths derive from this
script's location, so it runs from any working directory.

.EXAMPLE
& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.5.341
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+\.\d+$')]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$upstream = Join-Path $repoRoot 'upstream'
$git = (Get-Command git -ErrorAction Stop).Source

if (-not (Test-Path -LiteralPath (Join-Path $upstream '.git'))) {
    throw "The upstream submodule is not initialised. Run: git -C `"$repoRoot`" submodule update --init"
}

# --quiet: git reports success on stderr, which Windows PowerShell 5.1 turns into a terminating error under
# Stop when stderr is redirected or the host has no console (ISE, VS Code). $LASTEXITCODE is the failure signal.
& $git -C $upstream fetch --tags --quiet origin
if ($LASTEXITCODE) { throw 'Fetching upstream tags failed.' }
& $git -C $upstream rev-parse --verify --quiet "refs/tags/$Tag^{commit}" | Out-Null
if ($LASTEXITCODE) { throw "No release tag $Tag on upstream. Only released tags may be used." }

$before = (& $git -C $upstream rev-parse HEAD).Trim()
& $git -C $upstream checkout --quiet --detach "refs/tags/$Tag"
if ($LASTEXITCODE) { throw 'Checking out the tag failed.' }
$after = (& $git -C $upstream rev-parse HEAD).Trim()
& $git -C $repoRoot add upstream
if ($LASTEXITCODE) { throw 'Staging the new submodule commit failed.' }

Write-Host "upstream: $before -> $after ($Tag). Building and testing against it..."
& (Join-Path $PSScriptRoot 'Build.ps1')

Write-Host ''
Write-Host "Built and tested against $Tag."
Write-Host 'Next: skim upstream''s release notes, note the update in docs/WORKLOG.md and docs/ARCHITECTURE.md (pinned release), then:'
Write-Host "  git -C `"$repoRoot`" commit -m `"Update upstream to $Tag`""
Write-Host 'If the engine stopped compiling, upstream changed an API it maps: fix src/ScarletMeter.Engine/Upstream only.'

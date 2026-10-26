<#
.SYNOPSIS
Prints one release's section of CHANGELOG.md, from any working directory.

.DESCRIPTION
The section is the lines under the heading "## [<version>] - <yyyy-mm-dd>", up to the next "## " heading or the link
definitions that end the file. The release workflow adds it to the release's notes on GitHub, so each release lists
what changed, and refuses a tag whose version has none; CI runs it for the app's version (src/Directory.Build.props),
so a version bump without its section fails before any tag. The changelog is found from this script's own location.
Throws when the section is missing, its heading has no date, or it lists no change.

.EXAMPLE
& "<checkout>\scripts\Get-ChangelogSection.ps1" -Version 0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$changelog = Join-Path $repoRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelog -PathType Leaf)) { throw "There is no CHANGELOG.md at $changelog." }

$lines = [IO.File]::ReadAllLines($changelog)
$heading = "## [$Version]"
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -eq $heading -or $lines[$i].StartsWith("$heading ", [StringComparison]::Ordinal)) { $start = $i; break }
}
if ($start -lt 0) { throw "CHANGELOG.md has no section for ${Version}: add ""## [$Version] - <yyyy-mm-dd>"" with its changes (AGENTS.md, ""Changelog"")." }
if ($lines[$start] -cnotmatch '^## \[\d+\.\d+\.\d+\] - \d{4}-\d{2}-\d{2}$') { throw "CHANGELOG.md's heading for $Version has no date: ""## [$Version] - <yyyy-mm-dd>""." }

# The section ends at the next second-level heading or at the link definitions ("[0.1.0]: https://...") after the last.
$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith('## ', [StringComparison]::Ordinal) -or $lines[$i] -match '^\[[^\]]+\]:\s') { $end = $i; break }
}
$body = if ($end -gt $start + 1) { ($lines[($start + 1)..($end - 1)] -join "`n").Trim() } else { '' }
if ($body -notmatch '(?m)^- ') { throw "CHANGELOG.md's section for $Version lists no change." }
$body

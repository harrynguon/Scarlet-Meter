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
    $line = $lines[$i].TrimEnd()
    if ($line -eq $heading -or $line.StartsWith("$heading ", [StringComparison]::Ordinal)) { $start = $i; break }
}
if ($start -lt 0) { throw "CHANGELOG.md has no section for ${Version}: add ""## [$Version] - <yyyy-mm-dd>"" with its changes (AGENTS.md, ""Changelog"")." }
$date = [datetime]::MinValue
if ($lines[$start].TrimEnd() -cnotmatch '^## \[\d+\.\d+\.\d+\] - (?<date>\d{4}-\d{2}-\d{2})$' -or
    -not [datetime]::TryParseExact($Matches.date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$date)) {
    throw "CHANGELOG.md's heading for $Version needs a real date: ""## [$Version] - <yyyy-mm-dd>""."
}

# The section ends at the next second-level heading, or, for the last, where the link definitions that end the file
# begin ("[0.1.0]: https://...", blank lines among them). Only that closing block counts, so a reference link inside a
# section cannot cut it short (audit on PR #41).
$footer = $lines.Count
while ($footer -gt $start + 1 -and ($lines[$footer - 1].Trim() -eq '' -or $lines[$footer - 1] -match '^\[[^\]]+\]:\s')) { $footer-- }
$end = $footer
for ($i = $start + 1; $i -lt $footer; $i++) {
    if ($lines[$i].StartsWith('## ', [StringComparison]::Ordinal)) { $end = $i; break }
}
$body = if ($end -gt $start + 1) { ($lines[($start + 1)..($end - 1)] -join "`n").Trim() } else { '' }
if ($body -notmatch '(?m)^- ') { throw "CHANGELOG.md's section for $Version lists no change." }
$body

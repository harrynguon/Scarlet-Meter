<#
.SYNOPSIS
Checks scripts\Get-ChangelogSection.ps1 against throwaway changelogs.

.DESCRIPTION
Copies the script into a throwaway folder under the temp directory, writes a CHANGELOG.md beside it for each case and
checks what the script prints or refuses: a section and only its own lines (a reference link inside a section, a
heading with a trailing space, the link definitions that end the file), and a refusal for a missing section, a heading
without a date or with an impossible one, and a section that lists nothing. The release workflow and CI rely on those
refusals (audit on PR #41: they had only been run by hand). Prints ok or FAIL per case and throws when any failed. Paths
derive from this script's location and the platform's temp folder. Targets Windows PowerShell 5.1 and pwsh.

.EXAMPLE
& "<checkout>\scripts\Test-ChangelogSection.ps1"
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Get-ChangelogSection.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-changelog-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = New-Object Collections.Generic.List[string]

function Pass([string]$Name) { Write-Host "ok   $Name" }
function Fail([string]$Name, [string]$Why) { $failures.Add("$Name`: $Why"); Write-Host "FAIL $Name ($Why)" }

# Runs the copied script on a changelog of these lines; returns its output, or the refusal's message prefixed "THREW: ".
function Invoke-Section([string[]]$Changelog, [string]$Version) {
    [IO.File]::WriteAllText((Join-Path $work 'CHANGELOG.md'), ($Changelog -join "`r`n") + "`r`n")
    try { return [string](& (Join-Path $work 'scripts\Get-ChangelogSection.ps1') -Version $Version) }
    catch { return "THREW: $($_.Exception.Message)" }
}

function Expect-Section([string]$Name, [string[]]$Changelog, [string]$Version, [string[]]$Has, [string[]]$Lacks = @()) {
    $out = Invoke-Section $Changelog $Version
    if ($out.StartsWith('THREW: ')) { Fail $Name $out; return }
    foreach ($text in $Has) { if (-not $out.Contains($text)) { Fail $Name "lacks '$text'"; return } }
    foreach ($text in $Lacks) { if ($out.Contains($text)) { Fail $Name "has '$text'"; return } }
    Pass $Name
}

function Expect-Refusal([string]$Name, [string[]]$Changelog, [string]$Version, [string]$Because) {
    $out = Invoke-Section $Changelog $Version
    if ($out.StartsWith('THREW: ') -and $out.Contains($Because)) { Pass $Name } else { Fail $Name "expected a refusal naming '$Because', got: $out" }
}

$head = '# Changelog', '', '## [Unreleased]', ''
$links = '', '[Unreleased]: https://example.invalid/compare/v1.1.0...HEAD', '[1.1.0]: https://example.invalid/compare/v1.0.0...v1.1.0',
    '[1.0.0]: https://example.invalid/releases/tag/v1.0.0'
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $work 'scripts'))
    Copy-Item -LiteralPath $script -Destination (Join-Path $work 'scripts')

    $good = $head + @('## [1.1.0] - 2026-11-02', '', '### Added', '', '- Second release change (#2)', '',
        '## [1.0.0] - 2026-10-25', '', '### Fixed', '', '- First release change (#1)') + $links
    Expect-Section 'a section and only its own lines' $good '1.1.0' @('### Added', '- Second release change') @('First release', '## [')
    Expect-Section 'the last section stops at the closing links' $good '1.0.0' @('- First release change') @('[Unreleased]:', '[1.0.0]:')
    $inner = $head + @('## [1.0.0] - 2026-10-25', '', '### Fixed', '', '- Before a reference (#1)', '[x]: https://example.invalid/x',
        '- After a reference (#2)') + $links
    Expect-Section 'a reference link inside a section does not end it' $inner '1.0.0' @('- Before a reference', '- After a reference')
    $spaced = $head + @('## [1.0.0] - 2026-10-25 ', '', '### Fixed', '', '- A change (#1)') + $links
    Expect-Section 'a trailing space on the heading is ignored' $spaced '1.0.0' @('- A change')

    Expect-Refusal 'a missing section' $good '9.9.9' 'no section for 9.9.9'
    Expect-Refusal 'a heading without a date' ($head + @('## [1.0.0]', '', '### Fixed', '', '- A change (#1)') + $links) '1.0.0' 'needs a real date'
    Expect-Refusal 'an impossible date' ($head + @('## [1.0.0] - 2026-13-45', '', '### Fixed', '', '- A change (#1)') + $links) '1.0.0' 'needs a real date'
    Expect-Refusal 'a section that lists nothing' ($head + @('## [1.0.0] - 2026-10-25', '', '### Fixed', '') + $links) '1.0.0' 'lists no change'
    Expect-Refusal 'an empty last section before the links' ($head + @('## [1.0.0] - 2026-10-25') + $links) '1.0.0' 'lists no change'
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

if ($failures.Count -gt 0) { throw "$($failures.Count) changelog case(s) failed:`n$($failures -join "`n")" }
Write-Host 'All changelog cases passed.'

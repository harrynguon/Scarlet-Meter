<#
.SYNOPSIS
One command to update a portable Scarlet Meter to RATmeter's latest release: finds it, moves the upstream pin,
builds and tests, and writes a new portable zip.

.DESCRIPTION
1. Asks GitHub for RATmeter's latest published release (or takes -Tag). Releases only, never upstream's dev
   branch. If this checkout is already pinned to it, says so and stops.
2. Makes a temporary git worktree of this checkout's HEAD under the temp folder, on a local branch
   "update/upstream-<release>", and runs its scripts\Update-Upstream.ps1 -Tag <release>: the pin moves, then a
   Debug build and the tests run against the new release.
3. Commits the pin move on that branch, so the build is stamped with a real commit, then runs its
   scripts\Publish-Portable.ps1 (Release build, tests, publish, layout check, marker, zip) with this checkout's
   "portable" folder as the output.
4. Removes the temporary worktree. The branch stays: it holds the pin move, which becomes part of main through a
   pull request with the docs updated (docs/DEVELOPMENT.md, "Updating upstream").

It changes nothing in this checkout's files, in a portable copy or in its data: to update a copy, close the meter
and extract the new zip into the folder that holds its ScarletMeter folder, replacing files. When the new release
changed something Scarlet's engine uses, the build or the tests fail, no zip is written, and the message says so;
that needs a code change in src/ScarletMeter.Engine/Upstream. Uncommitted changes in this checkout are not in the
build (a warning says so). Needs git, the network (GitHub) and the .NET 10 SDK (SCARLET_DOTNET when it is not on
PATH). Every path derives from this script's location or the platform's temp folder. Targets Windows PowerShell 5.1
and pwsh.

.PARAMETER Tag
A RATmeter release tag such as v1.11.5.341, instead of asking GitHub for the latest.

.EXAMPLE
& "<checkout>\scripts\Update-Meter.ps1"
#>
[CmdletBinding()]
param(
    [ValidatePattern('^v\d{1,9}\.\d{1,9}\.\d{1,9}\.\d{1,9}$')]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$latestReleaseApi = 'https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest'
$releaseTag = '^v\d{1,9}\.\d{1,9}\.\d{1,9}\.\d{1,9}$'
$git = (Get-Command git -ErrorAction Stop).Source

# First line git prints for these arguments in $Directory, or $null on any failure. Never throws: stderr is
# discarded with the preference relaxed, because Windows PowerShell 5.1 turns redirected native stderr into a
# terminating error under Stop. (The same helper as Install-Meter.ps1's.)
function Get-GitLine([string]$Directory, [string[]]$Arguments) {
    if (-not (Test-Path -LiteralPath (Join-Path $Directory '.git'))) { return $null }
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $git -C $Directory @Arguments 2>$null)
        if ($LASTEXITCODE -eq 0 -and $output.Count) { return ([string]$output[0]).Trim() }
        return $null
    }
    catch { return $null }
    finally { $ErrorActionPreference = $preference }
}

# Runs git and throws with $Failure when it fails; --quiet arguments keep its success chatter off stderr.
function Invoke-Git([string[]]$Arguments, [string]$Failure) {
    & $git @Arguments
    if ($LASTEXITCODE) { throw $Failure }
}

# --- Which release ---

if (-not $Tag) {
    # Windows PowerShell 5.1 may default to TLS 1.0, which GitHub refuses.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    try {
        $release = Invoke-RestMethod -Uri $latestReleaseApi -TimeoutSec 20 -MaximumRedirection 0 -Headers @{
            'User-Agent' = 'ScarletMeter-Update-Meter'
            'Accept' = 'application/vnd.github+json'
        }
    }
    catch {
        throw "Could not ask GitHub for RATmeter's latest release ($($_.Exception.Message)). Try again later, or name one with -Tag."
    }
    # The answer is untrusted: only a release tag of the expected form is used.
    $Tag = [string]$release.tag_name
    if ($Tag -notmatch $releaseTag) { throw 'GitHub answered with something that is not a RATmeter release tag; nothing was changed.' }
}

$current = Get-GitLine (Join-Path $repoRoot 'upstream') @('describe', '--tags', '--exact-match', 'HEAD')
if ($current -and $current -match $releaseTag) {
    $currentVersion = [Version]$current.Substring(1)
    $wantedVersion = [Version]$Tag.Substring(1)
    if ($currentVersion -eq $wantedVersion) {
        Write-Host "Already on RATmeter $Tag; nothing to update. (Publish-Portable.ps1 rebuilds the zip if you need one.)"
        return
    }
    if ($currentVersion -gt $wantedVersion) {
        throw "This checkout is on $current, newer than $Tag; nothing was changed."
    }
}
Write-Host "Updating from RATmeter $(if ($current) { $current } else { 'an unknown release' }) to $Tag."

if (Get-GitLine $repoRoot @('status', '--porcelain')) {
    Write-Warning 'This checkout has uncommitted changes; they are not in the new build, which starts from the last commit.'
}

# --- A throwaway worktree, so this checkout is never touched ---

$branch = "update/upstream-$Tag"
$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-update-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$branchExists = [bool](Get-GitLine $repoRoot @('rev-parse', '--verify', '--quiet', "refs/heads/$branch"))
$startCommit = Get-GitLine $repoRoot @('rev-parse', 'HEAD')
if ($branchExists) {
    Write-Host "Reusing the local branch $branch from an earlier run."
    Invoke-Git @('-C', $repoRoot, 'worktree', 'add', '--quiet', $work, $branch) 'Could not create the temporary worktree.'
}
else {
    Invoke-Git @('-C', $repoRoot, 'worktree', 'add', '--quiet', '-b', $branch, $work, 'HEAD') 'Could not create the temporary worktree.'
}
try {
    Invoke-Git @('-C', $work, 'submodule', 'update', '--init', '--quiet', 'upstream') 'Could not fetch the upstream submodule into the temporary worktree.'

    try {
        & (Join-Path $work 'scripts\Update-Upstream.ps1') -Tag $Tag
    }
    catch {
        throw "Moving to RATmeter $Tag, or building and testing against it, failed: $($_.Exception.Message) Nothing was written to portable\ and your meter is unchanged. If the build or the tests failed, the release changed something Scarlet's engine uses, which needs a code change in src\ScarletMeter.Engine\Upstream (the scarlet-upstream-update skill)."
    }

    # Update-Upstream.ps1 staged the pin move; commit it on the branch (nothing to commit when reusing it).
    & $git -C $work diff --cached --quiet
    if ($LASTEXITCODE) {
        Invoke-Git @('-C', $work, 'commit', '--quiet', '-m', "Move upstream to release $Tag (Update-Meter.ps1)") 'Committing the pin move failed.'
    }

    & (Join-Path $work 'scripts\Publish-Portable.ps1') -OutputRoot (Join-Path $repoRoot 'portable')
}
finally {
    # Only the worktree this run made. --force because it holds a submodule and build output. The preference is
    # relaxed so git's stderr cannot turn the clean-up into a second error under Windows PowerShell 5.1.
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $git -C $repoRoot worktree remove --force $work 2>$null | Out-Null } catch { }
    finally { $ErrorActionPreference = $preference }
    if (Test-Path -LiteralPath $work) {
        Write-Warning "Could not remove the temporary worktree $work; delete it by hand, then run: git -C `"$repoRoot`" worktree prune"
    }
    # A branch this run created but never committed to (the run failed first) holds nothing: drop it.
    elseif (-not $branchExists -and $startCommit -and (Get-GitLine $repoRoot @('rev-parse', "refs/heads/$branch")) -eq $startCommit) {
        $ErrorActionPreference = 'Continue'
        try { & $git -C $repoRoot branch --quiet -D $branch 2>$null | Out-Null } catch { }
        finally { $ErrorActionPreference = $preference }
    }
}

Write-Host ''
Write-Host "Updated to RATmeter $Tag. To update a portable copy: close the meter, then extract the zip above into the folder"
Write-Host 'that holds your ScarletMeter folder, replacing files (your data folder is kept).'
Write-Host "The pin move is on the local branch $branch; it joins main through a pull request (docs/DEVELOPMENT.md)."

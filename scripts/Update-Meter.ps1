<#
.SYNOPSIS
One command to update the portable Scarlet Meter to RATmeter's latest release: finds it, moves the upstream pin,
builds and tests, and updates <checkout>\portable\ScarletMeter in place, keeping its data.

.DESCRIPTION
1. Asks GitHub for RATmeter's latest published release (or takes -Tag). Releases only, never upstream's dev
   branch. The release this checkout is on is read from the pin its HEAD commit records (not from upstream/'s
   files, which a plain git pull leaves behind); if it is already the wanted one, says so and stops, and an older
   one is refused.
2. Makes a temporary git worktree under the temp folder on a local branch "update/upstream-<release>", started
   from HEAD. An existing branch of that name from an earlier run is reused only when it contains HEAD, so the
   build never misses commits made since; otherwise the script stops and says how to recover.
3. Runs that worktree's scripts\Update-Upstream.ps1 -Tag <release> (the pin moves, then a Debug build and the
   tests), commits the pin move on the branch, so the build is stamped with a real commit, and runs its
   scripts\Publish-Portable.ps1 (Release build, tests, publish, layout check, marker) with this checkout's
   "portable" folder as the output: the new build replaces the program files of the one live copy,
   <checkout>\portable\ScarletMeter, and that copy's data folder is kept.
4. Removes the temporary worktree, and a branch it created this run without committing to. A branch holding the
   pin move stays: it joins main through a pull request with the docs (docs/DEVELOPMENT.md).

Close the meter first: the script refuses while ScarletMeter.exe runs from the portable copy, before building.
It changes none of this checkout's files and no data; besides the live copy's program files, it leaves only the
local branch (and, on a checkout whose upstream/ was never initialised, git's submodule settings in .git/config).
When the new release changed something Scarlet's engine uses, the build or the tests fail, the live copy is left
as it was, and the message says so; that needs a code change in src/ScarletMeter.Engine/Upstream.
Uncommitted changes in this checkout are not in the build (a warning says so). Needs git with a user identity
(for the commit), the network (GitHub) and the .NET 10 SDK (SCARLET_DOTNET when it is not on PATH). Every path
derives from this script's location or the platform's temp folder. Targets Windows PowerShell 5.1 and pwsh.

.PARAMETER Tag
A RATmeter release tag such as v1.11.5.341, instead of asking GitHub for the latest.

.EXAMPLE
& "<checkout>\scripts\Update-Meter.ps1"
#>
[CmdletBinding()]
param(
    # Case-sensitive, ASCII digits only, nothing after: the same rule as the app's release check.
    [ValidateScript({ $_ -cmatch '\Av[0-9]{1,9}(\.[0-9]{1,9}){3}\z' })]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$latestReleaseApi = 'https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest'
$releaseTag = '\Av[0-9]{1,9}(\.[0-9]{1,9}){3}\z'
$git = (Get-Command git -ErrorAction Stop).Source
. (Join-Path $PSScriptRoot 'PortableFolders.ps1')
$portable = New-PortableFolders (Resolve-FinalPath (Join-Path $repoRoot 'portable'))

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

# Runs git and throws $Failure, with git's last lines, when it fails. The preference is relaxed while it runs, so
# git's stderr becomes part of the message instead of a raw NativeCommandError under Windows PowerShell 5.1.
function Invoke-Git([string[]]$Arguments, [string]$Failure) {
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& $git @Arguments 2>&1) }
    finally { $ErrorActionPreference = $preference }
    if ($LASTEXITCODE) {
        $detail = (@($output | ForEach-Object { "$_".Trim() } | Where-Object { $_ }) | Select-Object -Last 3) -join ' '
        throw "$Failure $detail"
    }
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
    if ($Tag -cnotmatch $releaseTag) { throw 'GitHub answered with something that is not a RATmeter release tag; nothing was changed.' }
}

# The release HEAD pins: the submodule commit HEAD records, named by its tag when upstream/ has it.
$pinnedCommit = Get-GitLine $repoRoot @('rev-parse', 'HEAD:upstream')
$current = if ($pinnedCommit) { Get-GitLine (Join-Path $repoRoot 'upstream') @('describe', '--tags', '--exact-match', $pinnedCommit) }
if ($current -and $current -cmatch $releaseTag) {
    $currentVersion = [Version]$current.Substring(1)
    $wantedVersion = [Version]$Tag.Substring(1)
    if ($currentVersion -eq $wantedVersion) {
        Write-Host "Already on RATmeter $Tag; nothing to update. (Publish-Portable.ps1 rebuilds the portable copy if you need it.)"
        return
    }
    if ($currentVersion -gt $wantedVersion) {
        throw "This checkout is on $current, newer than $Tag; nothing was changed."
    }
}
elseif ($pinnedCommit) {
    Write-Warning "Could not tell which release HEAD pins (upstream commit $($pinnedCommit.Substring(0, [Math]::Min(7, $pinnedCommit.Length))), not a tag this checkout has); going ahead without the up-to-date or older-release checks."
}
# Refused now rather than after minutes of building; Publish-Portable.ps1 checks again before the swap.
Assert-MeterNotRunningFrom @($portable.Live, $portable.Old, $portable.Staging) 'run Update-Meter.ps1 again'
Write-Host "Updating from RATmeter $(if ($current) { $current } else { 'an unknown release' }) to $Tag."

if (Get-GitLine $repoRoot @('status', '--porcelain')) {
    Write-Warning 'This checkout has uncommitted changes; they are not in the new build, which starts from the last commit.'
}

# --- A throwaway worktree, so this checkout's files are never touched ---

$branch = "update/upstream-$Tag"
$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-update-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$startCommit = Get-GitLine $repoRoot @('rev-parse', 'HEAD')
if (-not $startCommit) { throw 'This checkout has no commit to build from.' }
$branchExists = [bool](Get-GitLine $repoRoot @('rev-parse', '--verify', '--quiet', "refs/heads/$branch"))
if ($branchExists) {
    # Reused only when it holds everything HEAD does, so the zip never misses commits made since an earlier run.
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $git -C $repoRoot merge-base --is-ancestor $startCommit "refs/heads/$branch" 2>$null; $containsHead = ($LASTEXITCODE -eq 0) }
    finally { $ErrorActionPreference = $preference }
    if (-not $containsHead) {
        throw "The local branch $branch, from an earlier run, does not contain this checkout's current commit, so building it would miss newer changes. Nothing was changed. If it holds only the pin move, delete it (git -C `"$repoRoot`" branch -D $branch) and run again; otherwise merge HEAD into it first."
    }
    Write-Host "Reusing the local branch $branch from an earlier run (it contains the current commit)."
    Invoke-Git @('-C', $repoRoot, 'worktree', 'add', '--quiet', $work, $branch) 'Could not create the temporary worktree.'
}
else {
    Invoke-Git @('-C', $repoRoot, 'worktree', 'add', '--quiet', '-b', $branch, $work, $startCommit) 'Could not create the temporary worktree.'
}
try {
    Invoke-Git @('-C', $work, 'submodule', 'update', '--init', '--quiet', 'upstream') 'Could not fetch the upstream submodule into the temporary worktree.'

    try {
        & (Join-Path $work 'scripts\Update-Upstream.ps1') -Tag $Tag
    }
    catch {
        throw "Moving to RATmeter $Tag, or building and testing against it, failed: $($_.Exception.Message) Your portable copy is unchanged. If the build or the tests failed, the release changed something Scarlet's engine uses, which needs a code change in src\ScarletMeter.Engine\Upstream (the scarlet-upstream-update skill)."
    }

    # Update-Upstream.ps1 staged the pin move; commit it on the branch (nothing to commit when reusing it).
    & $git -C $work diff --cached --quiet
    if ($LASTEXITCODE) {
        Invoke-Git @('-C', $work, 'commit', '--quiet', '-m', "Move upstream to release $Tag (Update-Meter.ps1)") 'Committing the pin move failed (is git''s user.name and user.email set?).'
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
    # A branch this run created but never committed to holds nothing of its own: drop it.
    elseif (-not $branchExists -and (Get-GitLine $repoRoot @('rev-parse', "refs/heads/$branch")) -eq $startCommit) {
        $ErrorActionPreference = 'Continue'
        try { & $git -C $repoRoot branch --quiet -D $branch 2>$null | Out-Null } catch { }
        finally { $ErrorActionPreference = $preference }
    }
}

Write-Host ''
Write-Host "Updated to RATmeter $Tag. Start the meter again from $(Join-Path $portable.Live 'ScarletMeter.exe') (your data folder was kept)."
if (Get-GitLine $repoRoot @('rev-parse', '--verify', '--quiet', "refs/heads/$branch")) {
    Write-Host "The pin move is on the local branch $branch; it joins main through a pull request (docs/DEVELOPMENT.md)."
}

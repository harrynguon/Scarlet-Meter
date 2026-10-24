<#
.SYNOPSIS
Writes the complete source of a commit, RATmeter's submodule included, as one zip.

.DESCRIPTION
GitHub's own "Source code" archives of a release leave out submodules, so RATmeter's half of the source would live
only in its author's repository. This zips the checkout's HEAD (git archive, so only committed files, no build output
or local data) under a "ScarletMeter-<Version>-source" folder, and adds the upstream submodule's HEAD (the commit
the checkout records, which must be checked out) under its "upstream" folder. Neither may have uncommitted changes
to tracked files, so the zip is the source of what a build of this tree gives. The release workflow attaches the
result to every release (NOTICE, THIRD-PARTY-NOTICES.md). The output path must be absolute, in an existing folder,
and must not exist; the checkout is the one this script lives in. Targets Windows PowerShell 5.1 and pwsh.

.EXAMPLE
& "<checkout>\scripts\Write-SourceArchive.ps1" -Version 0.1.0 -OutputPath "<absolute folder>\ScarletMeter-0.1.0-source.zip"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ($OutputPath -notmatch '^(?:[A-Za-z]:[\\/]|\\\\|//)') { throw "OutputPath must be an absolute path, not `"$OutputPath`"." }
if (Test-Path -LiteralPath $OutputPath) { throw "$OutputPath already exists." }
$outputFolder = Split-Path -Parent $OutputPath
if (-not $outputFolder -or -not (Test-Path -LiteralPath $outputFolder -PathType Container)) { throw "The folder for $OutputPath does not exist." }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$upstream = Join-Path $repoRoot 'upstream'
$prefix = "ScarletMeter-$Version-source"

# The submodule must be at the commit the checkout records, or the archive would not be this commit's source.
$recorded = (git -C $repoRoot rev-parse 'HEAD:upstream')
if ($LASTEXITCODE) { throw 'The checkout records no upstream submodule commit.' }
$checkedOut = (git -C $upstream rev-parse HEAD)
if ($LASTEXITCODE -or $checkedOut.Trim() -ne $recorded.Trim()) { throw "upstream/ is not checked out at the recorded commit $recorded; run git submodule update." }
# The archive is the commits, so a changed tracked file in either would make it differ from a build of this tree.
# Untracked files (build output) are not archived and not checked.
foreach ($repository in $repoRoot, $upstream) {
    $changes = @(git -C $repository status --porcelain --untracked-files=no)
    if ($LASTEXITCODE) { throw "git status failed in $repository." }
    if ($changes) { throw "$repository has uncommitted changes to tracked files; commit or stash them first:`n$($changes -join "`n")" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("scarlet-source-" + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
try {
    $main = Join-Path $work 'main.zip'
    $sub = Join-Path $work 'upstream.zip'
    git -C $repoRoot archive --format=zip "--prefix=$prefix/" -o $main HEAD
    if ($LASTEXITCODE) { throw 'git archive of the checkout failed.' }
    git -C $upstream archive --format=zip "--prefix=$prefix/upstream/" -o $sub HEAD
    if ($LASTEXITCODE) { throw 'git archive of upstream/ failed.' }

    # The checkout's archive has an empty upstream/ folder entry for the submodule; the submodule's files go there.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($main, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $source = [IO.Compression.ZipFile]::OpenRead($sub)
        try {
            foreach ($entry in $source.Entries) {
                if ($entry.FullName.EndsWith('/') -and $archive.GetEntry($entry.FullName)) { continue }
                $copy = $archive.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
                $copy.LastWriteTime = $entry.LastWriteTime
                $copy.ExternalAttributes = $entry.ExternalAttributes # file modes, as git archive wrote them
                if (-not $entry.FullName.EndsWith('/')) {
                    $in = $entry.Open(); $out = $copy.Open()
                    try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
                }
            }
        }
        finally { $source.Dispose() }
    }
    finally { $archive.Dispose() }
    Move-Item -LiteralPath $main -Destination $OutputPath
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Source archive: $OutputPath (checkout $((git -C $repoRoot rev-parse --short HEAD)), upstream $($recorded.Substring(0, 7)))"

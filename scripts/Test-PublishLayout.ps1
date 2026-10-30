<#
.SYNOPSIS
Checks that a self-contained single-file publish of Scarlet Meter holds the files the meter reads at runtime.

.DESCRIPTION
A publish can exit 0 and still leave out loose content: BlazorWebView's host page and framework script, the
meter's stylesheet, the game-data files upstream's GameDataProvider loads beside the executable, and the
class icons copied from upstream's UI project (served through the assets.scarlet.example host), and the files a
release carries for the player (README.txt, LICENSE.txt, NOTICE.txt, THIRD-PARTY-NOTICES.md, and the licence texts
and the runtime's and packages' notices under licenses\). This also checks
that the managed code was bundled into ScarletMeter.exe. It does not launch the meter.
The publish directory must be an absolute path (AGENTS.md, "Paths").

.EXAMPLE
& "<checkout>\scripts\Test-PublishLayout.ps1" -PublishDirectory "<absolute publish directory>"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
# A drive path (C:\...) or a UNC path; Path.IsPathFullyQualified is missing from Windows PowerShell 5.1's .NET.
if ($PublishDirectory -notmatch '^(?:[A-Za-z]:[\\/]|\\\\|//)') {
    throw "PublishDirectory must be an absolute path, not `"$PublishDirectory`"."
}

$required = @(
    'ScarletMeter.exe',
    'wwwroot\index.html',
    'wwwroot\css\app.css',
    'wwwroot\css\views.css',
    'wwwroot\css\alerts.css',
    'wwwroot\js\frame-cap.js',
    'wwwroot\js\overlay-fx.js',
    'wwwroot\images\avatar.png',
    'wwwroot\_framework\blazor.webview.js',
    'GameData\Assets\classes.json',
    'GameData\Assets\skills.json',
    'GameData\Assets\dot_skill_ids.json',
    'GameData\Assets\healing_skill_ids.json',
    'GameData\Assets\mobs.json',
    'assets\classes\11.png',
    'assets\classes\19.png',
    # What a GPLv3 release carries for the player (ScarletMeter.App.csproj), and the licence texts and notices its
    # bundled components require (THIRD-PARTY-NOTICES.md, "Licence texts").
    'README.txt',
    'LICENSE.txt',
    'NOTICE.txt',
    'THIRD-PARTY-NOTICES.md',
    'licenses\MIT.txt',
    'licenses\Apache-2.0.txt',
    'licenses\MPL-2.0.txt',
    'licenses\packages\Microsoft.Web.WebView2.LICENSE.txt',
    'licenses\dotnet-wpf\THIRD-PARTY-NOTICES.TXT',
    'licenses\runtime\Microsoft.NETCore.App.Runtime.win-x64\LICENSE.TXT',
    'licenses\runtime\Microsoft.NETCore.App.Runtime.win-x64\THIRD-PARTY-NOTICES.TXT'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $PublishDirectory $_) -PathType Leaf) })
if ($missing) { throw "The published layout is missing: $($missing -join ', ')" }

# Single-file bundling puts the app's own assembly inside ScarletMeter.exe; a loose copy means it did not happen.
if (Test-Path -LiteralPath (Join-Path $PublishDirectory 'ScarletMeter.dll')) {
    throw 'ScarletMeter.dll is loose beside the executable: single-file bundling did not happen.'
}

Write-Host "Published layout is complete: $PublishDirectory"

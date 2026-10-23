<#
.SYNOPSIS
Starts Scarlet Meter, lets a script drive and inspect its pages, then closes it and reports the exit code.

.DESCRIPTION
For checking UI changes without the game. The meter runs with a fresh data directory (deleted afterwards
unless -KeepData) and with WebView2 remote debugging on a localhost port for this run only, which lets
-Steps read and click page elements through the Chrome DevTools Protocol. -Steps receives one object:

  $m.Eval(window, js)            evaluates JavaScript in a window's page and returns the value
  $m.WaitFor(window, js, sec)    waits until the expression is truthy; returns its value or throws
  $m.Capture(window, path)       saves a PNG of a window's page (absolute path)
  $m.Windows()                   names of the open windows (overlay, settings, ...)
  $m.Drag(dx, dy)                drags the overlay by its header with the real mouse; returns the moved distance
  $m.Process                     the meter's process

Window names are each page's data-window attribute. Never use this against a meter holding real data;
the debugging port is open only while the script runs. Paths derive from this script's location.

.EXAMPLE
& "<checkout>\scripts\Smoke-Meter.ps1" -Arguments '--demo' -Steps { param($m) $m.WaitFor('overlay', "document.querySelectorAll('.row').length > 0", 15) }
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string[]]$Arguments = @('--no-capture'),
    [string]$DataDirectory,
    [int]$DevToolsPort = 9333,
    [scriptblock]$Steps,
    [switch]$KeepData,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $Exe) { $Exe = Join-Path $repoRoot 'src\ScarletMeter.App\bin\Debug\net10.0-windows10.0.20348.0\ScarletMeter.exe' }
if ($Exe -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') { throw "-Exe must be an absolute path, not `"$Exe`"." }
if (-not (Test-Path -LiteralPath $Exe)) { throw "No meter at $Exe. Build first (scripts\Build.ps1)." }
$ownsData = -not $DataDirectory
if ($ownsData) { $DataDirectory = Join-Path ([IO.Path]::GetTempPath()) ("scarlet-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 12)) }
elseif ($DataDirectory -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') { throw "-DataDirectory must be an absolute path." }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('ScarletSmoke.Mouse' -as [type])) {
    Add-Type -Namespace ScarletSmoke -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, System.UIntPtr extra);
'@
}

# --- Chrome DevTools Protocol over a WebSocket, one connection per page target. ---
$script:sockets = @{}
$script:nextId = 0

function Get-Targets {
    $list = Invoke-RestMethod -Uri "http://127.0.0.1:$DevToolsPort/json/list" -TimeoutSec 5
    @($list | Where-Object { $_.type -eq 'page' })
}

function Send-Cdp([string]$wsUrl, [string]$method, [hashtable]$params) {
    if (-not $script:sockets.ContainsKey($wsUrl)) {
        $socket = New-Object System.Net.WebSockets.ClientWebSocket
        $socket.ConnectAsync([Uri]$wsUrl, [Threading.CancellationToken]::None).Wait()
        $script:sockets[$wsUrl] = $socket
    }
    $socket = $script:sockets[$wsUrl]
    $script:nextId++
    $id = $script:nextId
    $payload = [Text.Encoding]::UTF8.GetBytes((@{ id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 8 -Compress))
    $socket.SendAsync([ArraySegment[byte]]$payload, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    $buffer = New-Object byte[] 1048576
    while ($true) {
        $stream = New-Object IO.MemoryStream
        do {
            $result = $socket.ReceiveAsync([ArraySegment[byte]]$buffer, [Threading.CancellationToken]::None).Result
            $stream.Write($buffer, 0, $result.Count)
        } while (-not $result.EndOfMessage)
        $message = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
        if ($message.id -eq $id) {
            if ($message.error) { throw "CDP $method failed: $($message.error.message)" }
            return $message.result
        }
    }
}

function Invoke-PageJs([string]$wsUrl, [string]$js) {
    $result = Send-Cdp $wsUrl 'Runtime.evaluate' @{ expression = $js; returnByValue = $true; awaitPromise = $true }
    if ($result.exceptionDetails) { throw "Page script failed: $($result.exceptionDetails.exception.description)" }
    $result.result.value
}

function Find-Window([string]$name) {
    foreach ($target in Get-Targets) {
        try {
            $found = Invoke-PageJs $target.webSocketDebuggerUrl "document.querySelector('[data-window]')?.dataset.window ?? ''"
            if ($found -eq $name) { return $target.webSocketDebuggerUrl }
        }
        catch { }
    }
    $null
}

function Wait-Window([string]$name, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try { $url = Find-Window $name; if ($url) { return $url } } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "No '$name' window within $seconds s."
}

$previousArgs = $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$DevToolsPort"
$allArgs = @($Arguments) + @('--data-dir', "`"$DataDirectory`"")
$process = Start-Process -FilePath $Exe -ArgumentList $allArgs -PassThru
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $previousArgs
$failure = $null
try {
    $null = Wait-Window 'overlay' 30
    $meter = [pscustomobject]@{ Process = $process; DataDirectory = $DataDirectory }
    $meter | Add-Member ScriptMethod Eval { param($window, $js) Invoke-PageJs (Wait-Window $window 10) $js }
    $meter | Add-Member ScriptMethod WaitFor {
        param($window, $js, $seconds)
        $deadline = (Get-Date).AddSeconds($seconds)
        $last = $null
        while ((Get-Date) -lt $deadline) {
            try { $value = Invoke-PageJs (Wait-Window $window 5) $js; if ($value) { return $value } } catch { $last = $_ }
            Start-Sleep -Milliseconds 250
        }
        throw "Timed out after $seconds s waiting in '$window' for: $js $last"
    }
    $meter | Add-Member ScriptMethod Capture {
        param($window, $path)
        $shot = Send-Cdp (Wait-Window $window 10) 'Page.captureScreenshot' @{ format = 'png' }
        [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($shot.data))
        $path
    }
    $meter | Add-Member ScriptMethod Windows {
        @(Get-Targets | ForEach-Object { try { Invoke-PageJs $_.webSocketDebuggerUrl "document.querySelector('[data-window]')?.dataset.window ?? ''" } catch { } } | Where-Object { $_ })
    }
    $meter | Add-Member ScriptMethod Drag {
        param($dx, $dy)
        $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $process.Id
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        $before = $window.Current.BoundingRectangle
        $x = [int]($before.X + 60); $y = [int]($before.Y + 14)
        [ScarletSmoke.Mouse]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 150
        [ScarletSmoke.Mouse]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
        foreach ($step in 1..10) { [ScarletSmoke.Mouse]::SetCursorPos($x + [int]($dx * $step / 10), $y + [int]($dy * $step / 10)) | Out-Null; Start-Sleep -Milliseconds 30 }
        [ScarletSmoke.Mouse]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
        $after = $window.Current.BoundingRectangle
        [pscustomobject]@{ Dx = $after.X - $before.X; Dy = $after.Y - $before.Y }
    }
    if ($Steps) { & $Steps $meter }
}
catch {
    $failure = $_
}
finally {
    foreach ($socket in $script:sockets.Values) { try { $socket.Dispose() } catch { } }
    if (-not $KeepRunning -and -not $process.HasExited) {
        # Close through the window, as a player would, so the deferred close and shutdown order run.
        $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $process.Id
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        $overlay = $windows | Where-Object { $_.Current.Name -eq 'Scarlet Meter' } | Select-Object -First 1
        if ($overlay) { $overlay.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        if (-not $process.WaitForExit(20000)) { Write-Warning 'The meter did not exit within 20 s; killing it.'; $process.Kill() }
    }
    if (-not $KeepRunning) { Write-Host "Exit code: $($process.ExitCode)" }
    if ($ownsData -and -not $KeepData -and -not $KeepRunning) {
        # WebView2's browser processes release the profile a moment after the meter exits.
        foreach ($attempt in 1..20) {
            if (-not (Test-Path -LiteralPath $DataDirectory)) { break }
            try { Remove-Item -LiteralPath $DataDirectory -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 }
        }
        if (Test-Path -LiteralPath $DataDirectory) { Write-Warning "Could not delete $DataDirectory" }
    }
    elseif ($KeepData) { Write-Host "Data kept in $DataDirectory" }
}
if ($failure) { throw $failure }

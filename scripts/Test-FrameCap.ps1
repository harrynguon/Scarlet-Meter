<#
.SYNOPSIS
Checks in headless Edge that wwwroot\js\frame-cap.js caps the page's animations at 60 steps a second and keeps them correct.

.DESCRIPTION
RazorMarkupTests pins frame-cap.js's text; this checks what it does, in a real browser in real time (virtual time
starts no animation, so it would prove nothing). Two synthetic pages load the checkout's frame-cap.js in headless
Edge (no window, a throwaway profile with sync off), and their results are read over the DevTools protocol on
127.0.0.1:
- an infinite animation is driven and keeps its timing; one the page paused stays where it is; a width transition
  reaches its end and fires transitionend; a one-off animation fires animationend; a delayed one is picked up; a
  transition restarted eight times is paused two frames after each start, so taken over on its first frame;
- on a screen faster than 70 Hz, an infinite animation changes at most 70 times a second, and on one faster than
  80 Hz, bars whose width transitions restart ten times a second (as the meter's do) at most 80: their 60 steps and up
  to one more change at each restart, a new transition being taken over a frame in; on a slower screen there is
  nothing to cap and the rate is skipped, saying so.
The pages and profile live in a new folder in the user's temp directory, deleted after. Exits 1 when a check fails.
Edge comes from its standard install folder, or -Edge (an absolute path).

.EXAMPLE
& "<checkout>\scripts\Test-FrameCap.ps1"
#>
[CmdletBinding()]
param(
    [string]$Edge
)

$ErrorActionPreference = 'Stop'
$cap = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ScarletMeter.App\wwwroot\js\frame-cap.js'
if (-not $Edge) {
    $Edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Edge -or -not (Test-Path -LiteralPath $Edge)) { throw 'Microsoft Edge was not found; pass -Edge with the absolute path of msedge.exe.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('scarlet-framecap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$style = '@keyframes spin { from { transform: translateX(0px); } to { transform: translateX(100px); } } @keyframes flash { from { opacity: 0; } to { opacity: 1; } } div { width: 10px; height: 10px; }'
$pages = @{
    'behaviour.html' = @"
<!DOCTYPE html><html><head><meta charset="utf-8"><style>$style
#loop { animation: spin 1s linear infinite; } #held { animation: spin 1s linear -0.25s infinite paused; }
#restart { width: 10%; transition: width 400ms linear; }
#bar { width: 10%; transition: width 120ms linear; } #once.go { animation: flash 300ms ease-out; } #late { animation: spin 1s linear 400ms infinite; }
</style></head><body style="width:400px"><div id="loop"></div><div id="held"></div><div id="bar"></div><div id="once"></div><div id="late"></div><div id="restart"></div>
<script src="frame-cap.js"></script><script>
const events = [], at = {};
document.addEventListener('transitionend', e => events.push('transitionend ' + e.target.id));
document.addEventListener('animationend', e => events.push('animationend ' + e.target.id));
const x = id => new DOMMatrixReadOnly(getComputedStyle(document.getElementById(id)).transform).m41;
const anim = id => document.getElementById(id).getAnimations()[0];
setTimeout(() => { document.getElementById('bar').style.width = '90%'; document.getElementById('once').classList.add('go'); }, 200);
setTimeout(() => { at.loop1 = anim('loop').currentTime; at.timeline1 = document.timeline.currentTime; }, 700);
// A transition restarted is taken over on the frame after it starts, whether or not a step is due: two frames after
// its width is set it is paused. Eight times, so that a cap taking over only on its steps (every fourth frame at 240 Hz)
// passes by chance once in about 65,000 runs.
const frames = n => new Promise(go => { const tick = () => (--n <= 0 ? go() : requestAnimationFrame(tick)); requestAnimationFrame(tick); });
async function restarts() {
  const states = [], el = document.getElementById('restart');
  for (let n = 0; n < 8; n++) {
    el.style.width = n % 2 ? '10%' : '90%';
    await frames(2);
    states.push(el.getAnimations()[0]?.playState ?? 'none');
    await new Promise(go => setTimeout(go, 120));
  }
  return states.join(',');
}
window.result = new Promise(done => setTimeout(async () => {
  const read = {
    loopState: anim('loop').playState, loopTime: anim('loop').currentTime - at.loop1, timeline: document.timeline.currentTime - at.timeline1,
    heldState: anim('held').playState, heldX: x('held'), barWidth: getComputedStyle(document.getElementById('bar')).width,
    lateState: anim('late').playState, lateTime: anim('late').currentTime, events: events.join(', ')
  };
  read.restarts = await restarts();
  done(read);
}, 1300));
</script></body></html>
"@
    'rate.html' = @"
<!DOCTYPE html><html><head><meta charset="utf-8"><style>$style
#loop { animation: spin 1s linear infinite; } #bar { width: 10%; transition: width 120ms linear; }
</style></head><body style="width:400px"><div id="loop"></div><div id="bar"></div>
<script src="frame-cap.js"></script><script>
setInterval(() => { document.getElementById('bar').style.width = (20 + Math.random() * 70).toFixed(1) + '%'; }, 100);
window.result = new Promise(done => {
  let frames = 0, loops = 0, widths = 0, lastLoop = null, lastWidth = null, start = null;
  function tick(now) {
    if (start === null) start = now;
    const loop = getComputedStyle(document.getElementById('loop')).transform, width = getComputedStyle(document.getElementById('bar')).width;
    frames++; if (loop !== lastLoop) loops++; if (width !== lastWidth) widths++; lastLoop = loop; lastWidth = width;
    if (now - start < 2000) requestAnimationFrame(tick); else done({ frames: frames / 2, loops: loops / 2, widths: widths / 2 });
  }
  setTimeout(() => requestAnimationFrame(tick), 300);
});
</script></body></html>
"@
}

# Loads a page in headless Edge and returns its window.result, read over the DevTools protocol once it settles.
function Read-Page([string]$Page) {
    $userData = Join-Path $work ('profile-' + [guid]::NewGuid().ToString('N'))
    $url = 'file:///' + (Join-Path $work $Page).Replace('\', '/')
    $arguments = @('--headless=new', '--disable-sync', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0',
        '--remote-debugging-address=127.0.0.1', "--user-data-dir=$userData", '--window-size=400,400', $url)
    Start-Process -FilePath $Edge -ArgumentList $arguments | Out-Null
    try {
        $portFile = Join-Path $userData 'DevToolsActivePort'
        # Edge creates the file before it writes the port: wait for the port itself, not only the file.
        $port = $null
        for ($i = 0; $i -lt 100 -and -not $port; $i++) {
            if (Test-Path -LiteralPath $portFile) { $port = @(Get-Content -LiteralPath $portFile -ErrorAction SilentlyContinue)[0] }
            if (-not $port) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $port) { throw "headless Edge gave no DevTools port for $Page" }
        $target = $null
        for ($i = 0; $i -lt 50 -and -not $target; $i++) {
            # In parentheses, so Windows PowerShell passes the list's items down the pipeline rather than the list.
            $target = (Invoke-RestMethod "http://127.0.0.1:$port/json/list") | Where-Object { $_.type -eq 'page' -and $_.url -like 'file:*' } | Select-Object -First 1
            if (-not $target) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $target) { throw "headless Edge showed no page for $Page" }
        $socket = [System.Net.WebSockets.ClientWebSocket]::new()
        $socket.ConnectAsync([uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
        # The page may still be loading when the target appears: wait for its result to exist, then for it to settle.
        $expression = 'new Promise(done => { const wait = () => window.result ? window.result.then(done) : setTimeout(wait, 50); wait(); })'
        $request = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expression; awaitPromise = $true; returnByValue = $true } } | ConvertTo-Json -Compress -Depth 5
        $bytes = [Text.Encoding]::UTF8.GetBytes($request)
        $socket.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
        $buffer = [byte[]]::new(65536); $text = ''
        do {
            $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), [Threading.CancellationToken]::None).Result
            $text += [Text.Encoding]::UTF8.GetString($buffer, 0, $received.Count)
        } until ($received.EndOfMessage -and $text -match '"id":1\b')
        $socket.Dispose()
        $reply = $text | ConvertFrom-Json
        if (-not $reply.result.result.PSObject.Properties['value']) { throw "$Page gave no result: $text" }
        return $reply.result.result.value
    } finally {
        Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$userData*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
function Check([bool]$Passed, [string]$What) {
    Write-Host ("{0} {1}" -f $(if ($Passed) { 'ok  ' } else { 'FAIL' }), $What)
    if (-not $Passed) { $failures.Add($What) }
}

try {
    Copy-Item -LiteralPath $cap -Destination (Join-Path $work 'frame-cap.js')
    foreach ($page in $pages.Keys) { Set-Content -LiteralPath (Join-Path $work $page) -Value $pages[$page] -Encoding UTF8 }

    $b = Read-Page 'behaviour.html'
    Check ($b.timeline -gt 500) "time moved on the timeline ($([int]$b.timeline) ms)"
    Check ($b.loopState -eq 'paused' -and [math]::Abs($b.loopTime - $b.timeline) -le 20) "an infinite animation is driven and keeps its timing ($([int]$b.loopTime) ms against $([int]$b.timeline))"
    Check ($b.heldState -eq 'paused' -and [math]::Abs($b.heldX - 25) -lt 0.5) "an animation the page paused stays where it is ($($b.heldX) px)"
    Check ($b.barWidth -eq '360px' -and $b.events -match 'transitionend bar') "a transition reaches its end and fires transitionend ($($b.barWidth))"
    Check ($b.events -match 'animationend once') 'a one-off animation fires animationend'
    Check ($b.lateState -eq 'paused' -and $b.lateTime -gt 0) 'a delayed animation is picked up'
    Check ($b.restarts -eq (@('paused') * 8 -join ',')) "a restarted transition is taken over on the frame after it starts, eight times ($($b.restarts))"

    $r = Read-Page 'rate.html'
    if ($r.frames -le 70) {
        Write-Host "skip the rate: this screen draws $($r.frames) frames a second, so there is nothing to cap"
    } else {
        Check ($r.loops -le 70) "an infinite animation changes at most 70 times a second ($($r.loops) at $($r.frames) frames a second)"
        if ($r.frames -le 80) {
            Write-Host "skip the restarting bars' rate: this screen draws $($r.frames) frames a second, no more than their limit"
        } else {
            Check ($r.widths -le 80) "bars whose transitions restart change at most 80 times a second ($($r.widths) at $($r.frames) frames a second)"
        }
    }
} finally {
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures.Count) { Write-Host "$($failures.Count) check(s) failed."; exit 1 }
Write-Host 'All frame-cap checks passed.'

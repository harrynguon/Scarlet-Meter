---
name: scarlet-smoke
description: Build Scarlet Meter and check its UI without the game - start the real app with a throwaway data directory, read and click its pages through WebView2 remote debugging, screenshot them, drag the overlay, press the hotkey, then close it and check the exit code and log. Use after any change to windows, pages, styles, settings or startup, and before opening a UI pull request.
---

# Smoke-testing Scarlet Meter's UI

UI Automation cannot see inside the WebView2 pages, so `scripts/Smoke-Meter.ps1` drives them through the Chrome DevTools Protocol instead. A rendered window never validates combat; say so when reporting.

## 1. Find the SDK and build

The .NET 10 SDK may not be on PATH. Check `dotnet --list-sdks`; if there is no 10.x, find a `dotnet.exe` that lists one (ask the player if none is found) and set, as absolute paths, for the session:

```powershell
$env:SCARLET_DOTNET = '<absolute path to dotnet.exe>'
$env:DOTNET_ROOT = '<its folder>'
& '<checkout>\scripts\Build.ps1'
```

`DOTNET_ROOT` is also what lets the framework-dependent Debug `ScarletMeter.exe` start.

## 2. Drive the meter

```powershell
& '<checkout>\scripts\Smoke-Meter.ps1' -Arguments '--demo' -Steps {
    param($m)
    $m.WaitFor('overlay', "document.querySelectorAll('li.row').length > 0", 20)
    $m.Eval('overlay', "document.querySelector('li.row').click(); true")          # opens details
    $m.WaitFor('details', "!!document.querySelector('.tab')", 20)
    $m.Eval('overlay', "document.querySelector('button[title=History]').click(); true")
    $m.Eval('overlay', "document.querySelector('button[title=Settings]').click(); true")
    $m.WaitFor('settings', "document.querySelectorAll('.tab').length", 15)
    $m.Capture('settings', '<absolute path>\settings.png')
}
```

- Window names are each page's `data-window` attribute: `overlay`, `settings`, `history`, `details`.
- `Eval` returns the expression's value (use `returnByValue`-friendly values: strings, numbers, booleans, plain arrays). Wrap statements as `(() => { ...; return x; })()`. A DOM node cannot be returned ("Object reference chain is too long"): wait on `!!document.querySelector(...)`.
- Read the text you check, not only counts: `innerText` of a tab body catches markup that renders literally (Razor shows `x@value` as text; `x@(value)` is the expression). A row count once passed while every buff read "x@buff.Applications".
- To seed settings, write `settings.json` into a fresh `-DataDirectory` before the run (a byte-order mark is accepted). With `-DataDirectory` the script does not delete the folder: remove it in a `finally`.
- Alerts: seed `"alerts": { "leadMinutes": 60, "events": ["shugo"] }` so a notice is always due; read `.alerts` and the hidden `.sr-only[role=status]` summary.
- The release check makes one real GET to api.github.com about 30 seconds after start-up (unless `"updates": { "checkUpstreamReleases": false }` is seeded); a build pinned below upstream's latest release shows `.update-note`.
- Click with `element.click()`; set a slider or input with `el.value = '...'; el.dispatchEvent(new Event('input', { bubbles: true }))` (or `'change'` for `@onchange` bindings).
- `--demo` (when the build has it) feeds a synthetic party into the engine so rows, details and history have data; `--no-capture` (the default) shows the empty state.
- `Capture` saves a page as PNG (no transparency, no other windows). For the real look over the desktop, capture the screen region of the window's UI Automation `BoundingRectangle` with `System.Drawing.Graphics.CopyFromScreen`.
- `Drag(dx, dy)` uses the real mouse on the overlay's header and returns the distance the window moved. It moves the player's cursor; don't run it while they're using the PC.
- Global hotkey: press it with `keybd_event` (user32) and read the overlay's state through UI Automation `WindowPattern.Current.WindowVisualState`. Tool windows are owned by the overlay, so they are not top-level UI Automation children.
- To check persistence across a restart, pass the same `-DataDirectory <absolute path> -KeepData` to two runs, then delete the directory.

## 3. Check the result

- The script prints `Exit code: N`; anything but 0 is a failure to investigate.
- With `-KeepData`, read `<data dir>\logs\scarlet-<date>.log` for `[WRN]`, `[ERR]` or `[FTL]` lines. Two `[INF]` lines about disposing the WebView2 control on the UI thread are expected on close.
- The script deletes its own data directory unless `-KeepData`; delete kept ones and any `%TEMP%\scarlet-*` leftovers when done. Never point it at a data directory holding real fights.
- Record what ran, and what did not (the game, file dialogs, DPI, multiple monitors), in `docs/WORKLOG.md`.

## Pitfalls seen

- Very long clone paths stop `e_sqlite3.dll` loading (0x800700CE); use a short worktree path.
- Bash heredocs above about 10 KB fail on this machine; write large files with the editor tool.
- A modal dialog (file picker) blocks a run; it cannot be driven through DevTools.
- Windows PowerShell's double-quoted here-strings eat backticks (Markdown code spans): build scripts that carry Markdown with single-quoted `@' '@` here-strings, saved as UTF-8 with a BOM so 5.1 reads non-ASCII text correctly.
- Synthetic key presses need scan codes (`MapVirtualKey`) or the page sees an empty `KeyboardEvent.code`; a JavaScript click does not activate the window, so use the real mouse (`ClientToScreen`) when focus matters.

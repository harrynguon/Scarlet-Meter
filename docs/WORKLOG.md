# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-25 — A boss-timers window for aion2timers.com

**What:** an hourglass in the overlay's header opens `Windows/BossTimersWindow`, a small window showing aion2timers.com's world-boss timers (`BossTimers/BossTimersSite.Start`). It is a plain WPF window with the WPF `WebView2` control, not a `ScarletWindow`: no Blazor, its own WebView2 environment and profile (`webview2-timers` in the data directory, where the site keeps its server and faction choice), locked down before its first navigation (no host objects, web messages, developer tools, saved passwords or form data; downloads, permission prompts, HTTP and client-certificate authentication and external protocols refused; SmartScreen on), and every top-level navigation, redirects included, through the pure `BossTimersSite.Decide`: the site's https pages show, another web page opens in the default browser only after the player's click, anything else is refused; new-window requests never open a second browser window. A bar above the page: Back to the timers, Reload, the site's name (or why the page stopped), "Keep on top" (`BossTimersSettings.KeepOnTop`, a new settings section, off by default) and "Open in browser". Not owned by the overlay, so it can sit behind the game; placement remembered under `WindowKeys.BossTimers` ("timers"); closed with the tool windows. Shared helpers: `ScreenPlacement.Beside` (from `ToolWindow`) and `DarkTitleBar`. `BossTimersSiteTests`, `RazorMarkupTests.TheBossTimersWindowIsKeptApartFromTheMeter`, `SettingsTests.TheBossTimersWindowStartsOffTheTopAndKeepsItsChoices` (and the two section-count reminders). ARCHITECTURE ("Windows", "Storage", tests, folders), SECURITY ("Network"), README (feature, Q&A, data table), HANDOFF (checks by hand), LESSONS.

**Why:** Harry asked to read the site's boss timers without being in game: "can a small window (browser i guess) be opened (with saved settings such as server and faction) so someone can clickl on a button and then view boss timers?", then "build the boss-timers window as its own PR: a small Scarlet window showing aion2timers.com, isolated and locked to that site, opened from a button". Reading the site from the meter was ruled out: its robots.txt disallows `/api/` for every crawler and bars AI agents by name (enforced server-side), so the player reads the page and the meter never does.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 476/476. Fail-first, each failing its test by name: the site allowed over http, hosts matched by substring, an empty "@" not caught, a script's navigation sent to the browser, redirects counted as clicks, developer tools on, downloads allowed, the lock removed, and the lock moved after the first navigation (the last two after the guard was tightened: removing the call first survived, since a missing call compared as position -1).

**Review (deep audit at high effort, on 8b285af and 6752036; Codex found nothing on 8b285af):** (1) after the page's browser process crashed, a toolbar click would have ended the meter: the control's `CoreWebView2` getter throws then, before `?.` helps, and the handler's exception reaches the dispatcher. Every button now acts through `OnPage`, which catches that; a browser exit disables them; the whole start sits inside one catch-all (a window closed meanwhile just stops). (2) "Only after the player's click" really meant during any user gesture, with no limit: a script on the site or an advert could turn one click into a burst of browser tabs, local addresses included. Now `BrowserLaunchGate` lets one page through every three seconds; `IsWebPage` refuses this PC and the local network; a popup reaches the browser only when the site's own frame asked (`DecideNewWindow`, from `OriginalSourceFrameInfo`); redirects are never clicks (`DecideNavigation`); the shell call runs outside WebView2's callback; the docs say "during the player's gesture". (3) Screen capture, Save as, file-save checks, dropped files, the context menu and the browser shortcuts were still open: all off now. (4) The smoke guard would not have stopped a proxied machine or "Open in browser": `--no-proxy-server` added, and the docs forbid automating that button. (5) The start's catch could miss an exception after Dispose: covered by (1). (6) The normalizer's null fallback was unguarded: `NullSectionsFromAFileTakeDefaults` covers `"bossTimers": null`. (7) The status now follows the failure's kind (a frame's, the GPU's or a utility process's failure is only logged) and clears when a page loads. (8) A site link that redirects elsewhere stops there: documented. (9) `WEBVIEW2_USER_DATA_FOLDER`, `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` and SmartScreen are named in SECURITY and the README. (10) Purpose comments added, the shell launch shared (`DefaultBrowser`, also used by `OpenReleasePage`), and the guards cover `Source=`, `SourceProperty`, `CreationProperties` and `Process.Start`.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 500/500 (`BrowserLaunchGateTests` and the new `BossTimersSiteTests` cases). Fail-first, 11 mutations, each failing its test by name: any opener sending popups, redirects as clicks, local hosts and private IPv4 allowed, no launch limit, every popup taken as a click (the audit's surviving mutation), Reload unguarded, the launch inside WebView2's callback, screen capture allowed, the start not fully caught, and the null section not defaulted (the audit's other survivor).

**Window check (Harry's go-ahead, out of game; demo, a throwaway data directory; `--no-proxy-server` and the site's host names mapped to "not found", so nothing was requested from aion2timers.com; UI Automation, no mouse; "Open in browser" never invoked):** the hourglass opened the window, not owned by the overlay and not topmost, 460 × 640 px, and created `webview2-timers`; it showed the browser's own "can't reach this page" (ERR_NAME_NOT_RESOLVED) under the dark title bar and the bar; a second click kept one window; closing the meter closed it, with exit code 0; the placement was saved under "timers" and restored on a second run. The first try of "Keep on top" through UI Automation's toggle did nothing: the switch listened for Click, which that toggle (and so a screen reader) does not raise. It now follows the checked state (guarded in `TheBossTimersWindowIsKeptApartFromTheMeter`), and the next run pinned the window (topmost, `"bossTimers": { "keepOnTop": true }` saved) and restored the pin on the second run. 500/500.

**Harry, on the README's demo GIFs:** "hide the event timers. since ina boss fight the event timers would be hidden automatcially". Re-recorded with the switches at their defaults: `demo.gif` in a boss fight with the notices held (it also shows the new hourglass), `alerts.gif` in the break after the kill, where they come back ("+2 more", all four, "Show less"), `background-picture.png` in a boss fight without notices, and `settings-alerts.png` with "Hide during boss fights" on. Alt texts say so.

**Not checked:** the real site by hand (HANDOFF): the page itself, the server and faction kept after a restart, links elsewhere.

**Next:** Codex and CI; Harry's check of the real site; merge with Harry's go-ahead; rebuild Harry's copy.

## 2026-10-25 — Event timers: a washed-out gold, never over the bars, nothing scrolls

**What:** (1) An upcoming world event's notice draws its pulsing dot and progress line in a new `--soon` colour (`#dcc68c`, a washed-out gold, the line at 65% opacity) instead of the "you" gold (`--me`, `#ffb703`); an open entry's stay teal, and nothing else that uses the "you" gold changes (`RazorMarkupTests.UpcomingNoticesUseTheSoftGold`). (2) Nothing in the overlay scrolls: the rows and the notice strip always take their full height (`flex: none`; the rows' overflow clipped, not scrollable, so a gliding row's transform cannot stretch the measured height), the expanded strip shows every due notice with no cap and no pinned button, and the notices always sit below the last DPS bar, so they never cover one. (3) When that content is taller than the player's window, the window grows to fit: `MeterPage` reads the panel's content height after each render (new `js/overlay-size.js`) and passes changes through `IMeterWindows.FitOverlayToContent` to `OverlayWindow.FitToContent`, which places the window by the new pure `Windows/OverlayFit` (downwards; up only at the bottom of the work area; capped at the work area). The window keeps the player's own top and height apart from the fitted ones (updated at `WM_EXITSIZEMOVE` after a drag or resize, and on an outside move), returns to them when the content fits, and saves them rather than the grown placement (`ScarletWindow.PlacementBounds`, which `WindowPlacementTracker` now saves through a bounds function). `ScreenPlacement.WorkAreaOf` is shared with `ToolWindow`. `OverlayFitTests`, `RazorMarkupTests.TheOverlayWindowGrowsToItsContent`, and `TheOverlayPanelFitsItsContent` rewritten for no scrolling. ARCHITECTURE ("Windows", "Overlay notices", tests), HANDOFF.

**Why:** Harry did not like the yellow ("i dont really like yellow ... or you can try a more washed out yellow. but show me"): four options rendered with the real styles in headless Edge (no window) were shown as a comparison, and Harry chose the washed-out gold. Then, from a demo screenshot of the default 260 px window: "i dont like how the event timers cover dps bars ... it should go below the bottom person's dps bar". A first version let the strip give way to the bars and scroll, or hide notices that did not fit whole, and Harry answered: "i dont like scrolling. dont make any scrolling involved. just make the event timers always visible (maybe 2 timers like now are visible at a minimum, rest can be expanded) if it even means the dps meter window needs to increase in size".

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 435/435; `OverlayFitTests` (4) and `TheOverlayWindowGrowsToItsContent` new. Fail-first, each failing its test: the rows scrolling again, the expanded strip capped and scrolling, the strip allowed to shrink, `OverlayFit` without the work-area cap, and `OverlayFit` never moving up. Headless Edge (no window, the real styles of main and of this branch, synthetic markup) compared the strip-gives-way version with main before the no-scroll rework: four rows and two notices at 260 px kept all four bars whole against two on main.

**Review (Codex on 5a49c7c, and a deep audit at high effort):** Codex (P2) and the audit (1) found that at the screen's height the content was cut with nothing left to scroll, so in a big field fight the notices and "Show less" could not be reached (measured: 40 rows with six notices expanded need 1403 px against a 1080p work area's 1040; 16 rows do on a 150% laptop). Fixed: once the window cannot grow further (or while the player resizes it smaller) the window layer says so (`IMeterWindows.OverlayCutsContent`), the page marks the panel `cut`, and the rows give way, the lowest first, with their bottom edge faded; the notices never do. Harry asked meanwhile for polish above all ("polish is key"), so the rows give way only then, never for the moment before the window grows, which would have squeezed them for a frame; and they are still measured at full height (`data-measuring`). The audit's other findings, each fixed: (2) a restore refitted inside WPF's own size handling, where the new height could be laid out at the old size and drawn cut: it now refits at `DispatcherPriority.Loaded`; (3) dragging a grown window that had moved up for the screen's bottom snapped after release, and a small upward drag snapped back: the dropped top is now the player's (and a click that moved nothing changes nothing), in the new pure `Windows/OverlayPlacement` with `OverlayPlacementTests`; (4) a save while a drag was held still stored the old placement: `PlacementBounds` is null during the drag (the tracker skips it) and the drag's end saves again (`ScarletWindow.SavePlacementSoon`); (5) an exception from the fit would have ended the process: it is caught and logged once; (6) Ctrl+wheel zoom would have grown the window to the wrong height: the overlay's WebView does not zoom (`WebViewZoomable`); (7) the window resized on every content change: it grows at once and shrinks two seconds after the content fits, unless it grows back; (8) four mutations to the window logic passed every test: the logic moved into the pure class, with tighter guards for the rest; (9) docs and LESSONS updated. (10, Top and Height set apart, one step near the bottom of the screen) is left as it is: either order shows one step, and a single native call would bypass WPF's own placement.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 442/442 (`OverlayPlacementTests`, 7, new). Fail-first, 13 mutations, each failing its test by name: a move keeping the old offset, a click taking the shown top, shrinking within half a pixel, an outside move setting the top, the rows never or always giving way, measuring the client height, fitting during a drag, never setting `applying`, refitting inside `StateChanged`, saving mid-drag, the overlay zooming, and the fit unguarded. Headless Edge (no window; the real `app.css`, `alerts.css` and `overlay-size.js`; synthetic markup): at the cap (40 rows in 1040 px, 16 rows in 688 px also at row size 1.25, 20 rows collapsed in 420 px) all notices and "Show less" sit whole inside the panel and the reported height is the full content's (1403, 755, 865, 743); before the window grows (8 rows at 260 px, not cut) the rows keep their full 224 px; the reported height is the same at every window height (no feedback loop); a rendered capped panel shows the last row faded above whole notices. README: a Q&A section (how it works, where data and logs are, what it requests, bans, why the meter grows, the shortcut, numbers against RATmeter, reporting a problem), and the world-event bullet says the notices sit below the bars and nothing scrolls.

**Review of 649bb03 (Codex, and a deep re-audit at high effort):** Codex (P1) and the re-audit (1, blocker) found that 649bb03 never grew the window: the page's fit condition had been rewritten from `< 0.5) return;` to `>= 0.5) { fit }`, and `reportedHeight` starts as NaN, so no height ever reached the window, and the cut rule then faded rows with the screen free. The claims above that the window grows did not hold for 649bb03 (LESSONS). Fixed: `OverlayFit.IsNewHeight` decides (the first report always, then half a pixel or more), unit-tested, and the page must call it. The re-audit's other findings: (2) nothing refitted on a display-scale, resolution or work-area change, and a window moved to a shorter monitor waited the 2 s shrink with its notices off screen: the window refits after layout on `WM_DPICHANGED`, `WM_DISPLAYCHANGE` and `WM_SETTINGCHANGE` (`SPI_SETWORKAREA`) and after an outside move, and a window that no longer fits the work area shrinks at once (`OverlayPlacement.WaitsToShrink`); (3) the README said the shortcut works while the game has focus, which HANDOFF has unchecked: now "a system-wide shortcut"; (4) "kept 14 days" is the 14 newest files, and the release check retries after ten minutes when it fails: both worded so; (5) the fade could linger one refresh after the window grew on its own: the window raises `Fitted` (`IMeterWindows.OverlayFitted`) and the page renders at once; (6) three mutations on the window-to-page path survived: now guarded.

**Checks after the re-audit (SDK 10.0.402):** `scripts/Build.ps1` 444/444 (`OverlayFitTests.TheFirstHeightAndRealChangesReachTheWindow`, `OverlayPlacementTests.AWindowThatNoLongerFitsTheScreenShrinksAtOnce` new). Fail-first, 10 mutations, each failing its test by name: the NaN bug put back in the helper and in the page, `OverlayCutsContent` always false, no render on a cut change, the shrink timer left running after close, no refit on a display change or after an outside move, an off-screen window waiting to shrink, `Fitted` never raised, and the page not listening for it.

**Window check at fce6bed (Harry's go-ahead, out of game; demo fights, a throwaway data directory, the real mouse):** the player's 260 px window grew to its content (311 px) at once and to 373 px with the strip expanded; after "Show less" it returned to 311 px, never below the player's 260; a drag of the grown window by 60 px kept it grown and saved the player's 260 at the new top; collapsing returned it at that top; with the strip expanded while minimized, a restore without activation (`SW_SHOWNOACTIVATE`) showed every notice inside the window; Ctrl+wheel did not zoom (device pixel ratio stayed 1). The demo's server offset was set to -3 h for the run so that four notices were due ("+2 more"). Two problems: (1) dragging the bottom edge 120 px up left the window at its 96 px minimum, and the save of that height was then dropped as unusable, so the overlay's whole saved placement vanished (`SettingsNormalizer` rejected sizes under 120 px while the window allows 96): fixed, `WindowPlacement.MinSize` is now both the overlay's `MinHeight` and the normalizer's floor, with `SettingsTests.TheSmallestOverlayAPlayerCanMakeIsKept` (LESSONS); (2) why the 120 px pull ended at 96 px, and why a later 150 px pull downwards left the window at 311 px, was not explained by that run (see the second window check below).

**Harry, after the run:** the pulsing dot on an upcoming notice was "too distracting": the dot is still now, and `RazorMarkupTests.TheNoticeStripNeverBlinks` keeps the strip free of looping animations (only a notice's one-time entrance moves). `scripts/Build.ps1` 446/446; fail-first: the pulse put back and the 120 px floor put back each fail their test.

**Harry, on the rows and the boss card:** "maybe three numbers on a row is too much". Four layouts were rendered headlessly with the real styles and made-up players (one line as now; two lines; DPS stacked over the total; DPS and share only), and Harry chose the stacked one with the total under the share instead, the share in bold white "so that the total damage number is more of a 'i should look here less' view". Each row now reads DPS, then the share (12.5 px, bold, `--text`) over the total damage (10 px, 55% white), in one 26 px line (was 24; the bar effects' pieces are centred or near the top, so they still fit). `RazorMarkupTests.APlayersRowShowsDpsThenTheShareOverTheTotal` replaces the old order test (fail-first: the total as large as the share, the share dimmed, a column before the share). The boss card keeps its icon, changed "a little" because the plain skull was too like another meter's: three options were rendered (a horned skull, a crown, a horned helm) and Harry chose the crown, also unlike the deaths' skull on a row. README and ARCHITECTURE updated. 446/446.

Harry then picked the crown for the boss icon (2 of the three options).

**Second window check, with the crown (Harry's go-ahead, out of game; demo, throwaway data, the real mouse):** a traced resize from the bottom edge followed the cursor step by step (319 px down to 199 px held: the page marked `cut`, rows giving way, notices whole; released: it grew back to its content's 319 px and saved the player's 199), and pulled 150 px down it stayed at 469 px and saved 469. The first check's odd resize (the window at its 96 px minimum after a 120 px pull) came right after a header drag: with a header drag first, the synthetic header drag sometimes does not move the window, and the edge drag after it then does nothing, on main's portable build (b34ad83) exactly as on this branch, so it is not this change's; whether a real mouse shows it is for Harry to try by hand (HANDOFF). The recording run, with the window checks that do not resize: the window grew to its content (319 px) and to the expanded strip (381 px), returned to 319 px after "Show less", every notice showed after a restore with the strip expanded while minimized, Ctrl+wheel left the zoom at 1, and the saved size stayed the player's 260 throughout. A first attempt at that run never started (a script error) and its log was an earlier run's; the results above are from the run at 11:54. The README's GIFs and stills were re-recorded from it: `demo.gif` (a boss fight with the notices below the bars, 694 KB), the new `alerts.gif` ("+2 more" expanding, the meter growing, "Show less", 465 KB), `background-picture.png` and `settings-alerts.png`, each framed on one dark canvas with a soft shadow (a scratch tool, not in the repository); demo players and a generated picture only.

**Not checked:** resizing right after a header drag with a real mouse, a display-scale change, a monitor's bottom edge, and anything in game.

**Next:** Codex and CI; merge with Harry's go-ahead; rebuild Harry's copy. Then, if Harry wants it, a boss-timers window (its own PR).

## 2026-10-25 — Spiritmaster, no white stripe, a margin under the rows

**What:** (1) Class names follow AION 2 Global: `Engine/Upstream/GlobalNames` maps upstream's "Elementalist" (and "Elementalist spirit") to "Spiritmaster" (whole words), applied to players' class names in `UpstreamMapping.ToPlayerRow` (the overlay, the details, history); upstream's `classes.json` stays unedited. Players' and summons' own names are left alone (a summon's is an entity name such as "Player_123", never the class's). The demo's player is "Demo Spiritmaster", and Scarlet's own code, comments, README, ARCHITECTURE, DEVELOPMENT and HANDOFF say Spiritmaster. (2) The player's own bar no longer carries a 2 px white stripe at its left edge (`.fill.me-mark`, now gone with its class): the row is marked by a ring round it instead, gold, or the bar effects' glow in the class's colour as before. (3) The overlay's panel has an 8 px bottom padding, so the last row (or the notices) no longer sits on the panel's edge. ARCHITECTURE (upstream folder, the overlay page, the panel, tests), LESSONS (two entries).

**Why:** Harry, from screenshots: "why is there a little white area to the left of the gladiator damage (and check other classes too)" (it was the "you" stripe, on the player's own bar whatever the class; the other bars had none, by their pixels); "rename Elementalist to Spiritmaster. We are on Aion 2 Global."; and "maybe add some permanent area below the last player's damage bar", since the fitted panel ended right under it.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 429/429: `GlobalNamesTests` (8 cases), `TheOwnRowIsMarkedByARingNotOnTheBar`, the class name asserted on the live row and the details (`PlayerDetailsTests`) and on a saved demo fight (`DemoModeTests`), and the panel's bottom padding in `TheOverlayPanelFitsItsContent`. Fail-first, each failing its tests: no bottom padding, the white stripe back, the class name unmapped in `ToPlayerRow` (four tests), and `GlobalNames` renaming nothing (six).

**Visual check (Harry out of game, the meter closed; `Smoke-Meter.ps1 --demo`, 400×260, throwaway data directories):** the rows read "Demo Gladiator, Demo Ranger, Demo Spiritmaster, Demo Cleric" and the class icons' titles "Gladiator, Ranger, Spiritmaster, Cleric"; the details window's header "Spiritmaster · Demo · CP 3,010"; after the kill, history's saved fight "Demo Spiritmaster · Spiritmaster · CP 3,010". The player's own row (Gladiator): with bar effects on, the effects' glow (`rgba(255,150,80,.55)` ring and a 10 px glow), with them off a gold 1 px ring, also with class colours off (its bar gold, as before, with the ring now too); the bar's own shadow `none` in all three, so no stripe. The last row ended 18 px above the panel's edge, the last notice 15 px. Screenshots looked right; exit codes 0; no log warnings.

**Audit (standard, approve with nits), and what changed:** (1) the comments said the padding gave "the same margin as the header and card above" and "an 8 px margin", but it adds 8 px to the rows' and notices' own spacing: corrected to the measured 18 and 15 px. (2) The summon-name mapping never changed anything (a summon's name is its entity's, such as "Player_123", never the class's) and could have renamed a nickname containing the word in the combat log only: removed; `GlobalNames` applies to players' class names alone. (3) The gold ring also shows when class colours are off, beside the gold bar: intended, and noted above. (4) The ring test also refuses any rule styling the own row's bar (`.row.me … .fill`). Codex found nothing on a433a9d. Checks after: `scripts/Build.ps1` 429/429.

**Not checked:** in game.

**Next:** CI and Codex on the fixes; merge with Harry's go-ahead; rebuild Harry's copy.

## 2026-10-25 — The overlay's panel fits its content

**What:** The overlay's panel (`.overlay` in `app.css`) no longer fills the window: it is as tall as its content, up to the window (`max-height: 100%`), and the rows take their own height (`flex: 0 1 auto`), shrinking and scrolling only when the window is too short. What the panel does not cover is see-through to the game. The window keeps the size the player set, which is the most the panel grows to. The background picture is laid out against the window (`background-attachment: fixed`), so it keeps its scale and place as the panel grows and shrinks. `RazorMarkupTests.TheOverlayPanelFitsItsContent` guards the three rules. ARCHITECTURE ("Windows", tests), a HANDOFF item.

**Why:** Harry, in game with the v0.1.0 build: the notices stepping aside in a boss fight is "great", but "there is a lot of empty space ... the area where the timers ends up being blank space". Of three ways offered (the panel fits its content, the window shrinks to fit, the rows grow to fill), Harry chose the panel fitting its content.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 420/420 after merging main; `TheOverlayPanelFitsItsContent` new; fail-first, each failing it: `height: 100%` back on the panel, `flex: 1` back on the rows, and the picture without `fixed`.

**Visual check (Harry out of game, the meter closed; `Smoke-Meter.ps1 --demo` in the default 400×260 window, throwaway data directories):** in the boss fight with the notices held the panel was 221 px tall, four rows unscrolled, and the 39 px below it see-through in the capture; with "Hide during boss fights" off, the two notices and "+1 more" took the rest (panel 260 px); expanded, the strip stayed within its cap; after the kill the panel shrank to 151 px (header, totals and the notices). With a picture at 50%, the picture layer computed as `fixed` and stood in the same place with the panel short and full. Exit codes 0; no log warnings. In the default 260 px window, two notices leave the four rows 73 px, so they scroll, and a 170 px window leaves them none: the same space as before the change (header, card and totals take 105 px, the strip 82, the rows the rest), not a regression; Harry's window is taller.

**Audit (standard, request changes), and what changed:** (1) with the rows keeping their own height, the expanded notice strip (a scroll area, so it may shrink) gave up space in proportion to them: measured by the auditor in headless Edge (synthetic markup, the real styles, 400×260, ten notices expanded), the strip was 127 px before the change, 93 px with 8 rows and 62 px (one notice) with 20 rows after it. `.alerts.expanded` now has `flex-shrink: 0`, which the auditor measured to restore 127 px while leaving every fit-to-content case as it was (between fights 64 px, held 162 px, shown 223 px; many rows in a short window still scroll inside the rows). (2) Click-through and edge resizing stay unchecked in game, as the docs say. (3) The guard now also pins `#app` and `html, body` at 100% (the panel's `max-height` is a percentage of them), refuses another rule giving the panel itself a height, and asserts the strip's `flex-shrink: 0`. (4) `min-height: 0` on the rows is redundant with their scroll area; kept, as explicit. LESSONS has the entry. Checks: `scripts/Build.ps1` 420/420; fail-first, each failing the guard: the strip allowed to shrink, `#app` without its height, and a `.overlay.fx` rule setting a height. The four-player demo cannot show the squeeze, so the auditor's headless measurement is the evidence for (1).

**Codex on a36f742 (P2), and what changed:** a strip that cannot shrink ran past the bottom of a short window's panel (for example 170 px, above the window's 96 px minimum), and the panel's `overflow: hidden` clipped "Show less", so the strip could not be collapsed. Valid: the strip now keeps its default shrink, and the rows instead give up their space first through a heavy shrink weight (`.rows { flex: 0 1000 auto }`; shrinking is weighted by size, so a plain 1 squeezed the strip in proportion). The guard asserts a weight of 100 or more and no `flex-shrink: 0` or `flex: none` on the strip. Measured in headless Edge (no window; the real `app.css` and `alerts.css`, synthetic markup with the page's classes, 400 px wide, the 45vh cap set in the same pixels) for main's rules, a36f742's and the final ones: 20 or 8 rows with ten notices expanded at 260 px, the strip 127 px with four notices whole and "Show less" inside, in all three; 4 rows expanded at 170 px, main and final a 57 px strip with "Show less" inside, a36f742 86 px with it clipped; notices held, main 260 px against 221 px fitted (a36f742 and final); between fights with two notices, 260 against 151; 20 rows held, the rows scroll in all three. At 120 px every variant clips the strip, as header, card and totals nearly fill the window: not new. Checks: `scripts/Build.ps1` 420/420; fail-first, each failing the guard: the rows' plain shrink weight, a strip that cannot shrink, and the strip as `flex: none`. LESSONS updated.

**Not checked:** in the game, whether clicks on the see-through part reach the game (a fully transparent pixel of a layered window normally passes them on) and resizing from the edges (the bottom edge is see-through while the panel is short, so it resizes from the top and sides then; Harry was told before choosing).

**Next:** audit, Codex and CI; merge with Harry's go-ahead; rebuild Harry's copy.

## 2026-10-25 — v0.1.0 released; the repository is public

**What:** #24 merged (333d168, main's tree identical to its head 55b66e5), then #23 brought up to date with it (only WORKLOG conflicted: both entries kept) and merged (13bf9b1, tree identical to its head aae3a53). `v0.1.0` tagged on 13bf9b1 and pushed; the Release workflow (run 40106082175) passed both jobs: `build` (pinned upstream v1.11.5.341, 419/419 tests, program zip 67.6 MB, source zip 6.9 MB) and `draft`. The repository was made public and the draft published as a pre-release: https://github.com/harrynguon/Scarlet-Meter/releases/tag/v0.1.0. Harry's portable copy was rebuilt from 333d168 with its `data` kept. SECURITY ("Public release", "Game terms") and HANDOFF record the dates.

**Why:** Harry asked for the release with the Windows SDK projection kept as RATmeter does: "Merge #24 then #23 once Codex and CI are clean, tag v0.1.0, check the draft, make the repo public and publish it".

**Checks:** before each merge, CI passed on the head (#24: 55b66e5; #23: aae3a53) and Codex's review state named that head (or, for #24, its last code commit bd1491a; 55b66e5 added only this log's visual check) as completed with no findings; no inline review comments. The draft's four files were downloaded to a scratch folder: both zips matched their `.sha256` files and the hashes the workflow logged (program 40a3165d…f692, source d95e0e07…8a86). The program zip held one `ScarletMeter` folder (47 files: the exe, its marker and manifest, `README.txt`, `LICENSE.txt`, `NOTICE.txt`, `THIRD-PARTY-NOTICES.md`, `licenses/` with the texts, the packages' and WPF's notices and the three runtime packs' files, `wwwroot`, `GameData`, `assets`), no `data` folder; the source zip held 448 files under `ScarletMeter-0.1.0-source/`, 259 of them RATmeter's under `upstream/`, `README.md` and `docs/images/demo.gif` included. Every extracted file, the exe included, was searched as ASCII and UTF-16 for home paths, the Windows account and machine name, the work domain, personal addresses, the SDK and worktree folders and Temp paths: the only hits were the public handle `harrynguon` (NOTICE, the notices, README.txt, the exe's company name, the docs' links). After publishing, the repository page, the release's files and the README's GIF answered without signing in (HTTP 200).

**Not checked:** the released exe itself in a window (Harry may have been trying the rebuilt copy in game; the same tree's portable publish ran `--demo` across every window earlier that day, WORKLOG below); SmartScreen on a downloaded copy.

**Repository rules (Harry's go-ahead):** ruleset "Protect main" (branch `~DEFAULT_BRANCH`, active, no bypass): no deletion, no force push, changes only by pull request (no approval needed, squash merges only), and the `build-and-test` check (GitHub Actions) must pass. Ruleset "Release tags" (`refs/tags/v*`, active): creating, moving and deleting such tags is refused to everyone but repository admins (role bypass), so a workflow's token cannot tag or retag a release. Actions: approval required before workflows run for any outside contributor's pull request (was first-time contributors only; the first `-f` form of the request left it unchanged, a JSON body set it). Read back through the API: both rulesets active, `main`'s rules deletion, non_fast_forward, pull_request, required_status_checks; the approval policy `all_external_contributors`. A real push to `main` was not tried, to keep from changing it if a rule were wrong; this pull request is the first change through the rules.

**In game (Harry's screenshot, the portable copy from 333d168):** after a dungeon boss's kill the boss stayed the target at 0 HP, the header read "Boss defeated", and the notices were back with "+1 more" (HANDOFF). Harry found the hiding "great" but the space the notices leave in a boss fight blank: a follow-up.

**Next:** merge this; delete the merged branches; the blank space in boss fights; in-game acceptance (HANDOFF).

## 2026-10-25 — Public release review and the release pipeline

**What:** The public-release review (SECURITY.md, "Public release") and what a public GPLv3 release needs. `LICENSE` is now the plain GPLv3 text (identical to gnu.org's `gpl-3.0.txt`, so GitHub recognises it); Scarlet's notice and RATmeter's credit moved to a new `NOTICE`, with an NCSOFT trademark and risk line. New `THIRD-PARTY-NOTICES.md` credits RATmeter, the fork, .NET, the schedule data sources and every NuGet package of the build, the table written by the new `scripts/Write-ThirdPartyNotices.ps1` from the app's restore; `ThirdPartyNoticesTests` fails when a package is missing or `LICENSE` stops being the plain text. The publish now carries `README.txt` (a player's guide: Npcap, unzip, run, risk), `LICENSE.txt`, `NOTICE.txt` and `THIRD-PARTY-NOTICES.md` beside the exe (`ScarletMeter.App.csproj`), and `Test-PublishLayout.ps1` requires them. New `.github/workflows/release.yml`: on a `v*` tag it checks the tag against `src/Directory.Build.props`' version and that `docs/release-notes/<tag>.md` exists, builds and tests, publishes the portable zip, checks its contents, and creates a draft (pre-release below 1.0) with `ScarletMeter-<version>-win-x64.zip` and its SHA-256; the job alone gets `contents: write`. `docs/release-notes/v0.1.0.md`. README ("Download", "Status", licences), DEVELOPMENT ("Releases"), ARCHITECTURE (updates), SECURITY (review, game terms), HANDOFF, the auditor's description.

**Why:** Harry asked for a release anyone can download, after the public-release review, and for the repository to be made public.

**Review findings (2026-10-25):** no credentials, home paths, work address, machine-specific paths, captures, databases, logs or binaries in any of the 91 commits' files (every commit searched; the search was shown to find known strings in old and new commits and a planted path); commits carry a dedicated GitHub address and the account's public name, and the docs name the player by first name. Licences: 62 packages in the app's restore (Microsoft.NET.ILLink.Tasks, a build-time tool, is the 63rd in a win-x64 restore and is not shipped), 51 MIT, 8 Apache-2.0, PacketDotNet MPL-2.0, WebView2 Microsoft's BSD-style licence, SQLite public domain, K4os LZ4 MIT; all GPLv3-compatible. Sources: RATmeter GPL-3.0 (public), the fork GPLv3 (public), taengu/A2Tools-Plugins GPL-3.0, Byanski/aion2timeroverlay MIT in `package.json`. Game assets (class icons, game data) are RATmeter's, shipped as RATmeter ships them; skill icons are fetched at run time.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 403/403, 2 new in `ThirdPartyNoticesTests`; fail-first: dropping PacketDotNet's row and appending text to `LICENSE` each failed their test. A local `Publish-Portable.ps1 -SkipTests -Zip` into a scratch root passed `Test-PublishLayout.ps1` and gave a zip of 30 entries with the four player files beside the exe; every file of that build, the exe included, was searched for the account name, home paths, the work address, the dedicated address and the worktree path: only the public handle in the notices and a "harry" inside .NET's Brotli dictionary in the exe. The release workflow itself has not run yet (it runs on the tag).

**README for players:** Harry asked for a README that says what the meter does and how, its features, what it has compared with other meters, how to download and use it, and its advantages (a custom background, no installation or administrator rights). Rewritten with those sections, an honest comparison with RATmeter (what Scarlet adds, and what only RATmeter has: buff and skill-cooldown overlays, the Stat Efficiency Calculator, the auto-updater), safety and terms, an FAQ, and building from source; three demo screenshots in `docs/images` (synthetic data, a test picture, PNGs with no metadata chunks). "No administrator rights" holds once Npcap is installed without its admin-only option; Npcap's own installer needs an administrator once, which the README says.

**Audit (deep, request changes), and what changed:** nothing in the history or PR text must stay private, so the repository can go public; but (1) binary copies must carry their licences' texts and notices: new `licenses/` (`MIT.txt` with each holder, the official Apache-2.0 and MPL-2.0 texts, and `packages/`, every licence and notice file the shipped packages carry, byte for byte, written by `Write-ThirdPartyNotices.ps1`), shipped in the zip with the .NET runtime packs' own licence and notices (a publish target copies them from the packs used, `licenses/runtime/<pack>`); .NET's links now point at `release/10.0`, and the MAUI (BlazorWebView) and Windows SDK projection notices are named. (2) GitHub's source archives leave RATmeter's submodule out: new `scripts/Write-SourceArchive.ps1` zips the commit with `upstream/`, CI runs it on every push, and the release attaches `ScarletMeter-<version>-source.zip`. (3) The release job held a write token while running third-party build code: now a read-only `build` job (`persist-credentials: false`) and a `draft` job that only uploads, actions pinned to release commits in both workflows. (4) The tag name was pasted into a script: it comes through the environment. (5) A tag on any commit built: it must be on `main` (protection and a tag ruleset need the repository public: HANDOFF). (6) The package list depended on the last restore (ILLink after a win-x64 restore): build-only packages (no compile, runtime, native, resource or runtime-specific assets) are left out in the script and the test alike, so 59 packages either way. (7) The guards were weaker than described: `LICENSE` is now checked by SHA-256, licence cells against each .nuspec, the package set exactly, each copy byte for byte; the script fails on a missing .nuspec. (8) SECURITY and HANDOFF said "public since 2026-10-25" before it was: now "to be made public once #23 merges", with a HANDOFF checklist. (9) to (16): the update steps say to extract into the folder holding `ScarletMeter`, `.\ScarletMeter.exe --demo` from a terminal, Windows 10 untried (WebView2 Runtime), file names as shipped (`LICENSE.txt`), PacketDotNet's source at its tag, DEVELOPMENT says what the release runs, README.txt mentions an installed copy's data folder, and `.gitignore` covers stray settings, captures and local agent settings. LESSONS has both lessons.

**Checks after the audit (SDK 10.0.402):** `scripts/Build.ps1` 405/405 (`ThirdPartyNoticesTests` now 4). Fail-first, each failing its test: a word changed mid-`LICENSE`, PacketDotNet's licence cell as MIT, its row dropped, a stale row, a notices cell emptied, a copied licence changed by one byte, the MPL text changed, and a stale file in `licenses/packages`. `Write-ThirdPartyNotices.ps1` rerun after a plain restore reproduces the table and copies exactly (59 packages, 8 files); after a win-x64 restore it gave the same 59. A scratch `Publish-Portable.ps1 -SkipTests` passed the extended `Test-PublishLayout.ps1`, with `licenses/runtime/` holding the NETCore, WindowsDesktop and AspNetCore packs' files and the manifest owning `licenses`. `Write-SourceArchive.ps1` gave a 5.4 MB zip of 514 entries, `upstream/` included, nothing outside its top folder. Not run: the release workflow (it runs on the tag), `Write-ThirdPartyNotices.ps1` under pwsh, and its missing-.nuspec failure.

**Re-audit of those changes (deep, request changes), and what changed:** CI and Codex were clean on ea18c6d, and the artifact hand-off, pins, exit codes, token split, publish target and source zip were confirmed by reading the actions at their pinned commits and by runs in a clone. (1) The app targeted `net10.0-windows10.0.20348.0`, a versioned Windows target, which bundled Microsoft's Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, 25 MB, and `WinRT.Runtime.dll`) under a licence known only by URL, though nothing in the meter uses WinRT: the app and tests now target plain `net10.0-windows`, the projection is gone from the build, the table's target name follows, and `ThirdPartyNoticesTests.TheProgramBundlesNoWindowsSdkProjection` fails if a versioned target or the SDK pack comes back. The target was RATmeter's UI project's, carried over without asking what it brings. (2) WPF's third-party notices were linked, not shipped (the Windows Desktop runtime pack carries only its licence): `licenses/dotnet-wpf/THIRD-PARTY-NOTICES.TXT` from dotnet/wpf v10.0.13, checked by hash, required by the layout and release checks. (3) `MIT.txt`'s permission text was unguarded: hashed from "Permission is hereby granted" to the end, the same text as .NET's and K4os's copies. (4) The README promised no administrator rights without saying Npcap's installer needs one once: it says so. (5) SECURITY said the review came "before the repository was made public": "is made public". (6) The draft job used paths relative to the workspace: `$RUNNER_TEMP/release`. (7) `Write-SourceArchive.ps1` now refuses uncommitted changes to tracked files in either repository, checks the output folder first, and keeps the submodule's file modes. (8) The pin comments name the exact releases (checkout v7.0.1, setup-dotnet v6.0.0). (9) ARCHITECTURE's release line no longer reads as if the data folder held the licences. LESSONS records the projection. (1) was then reverted: see the next entry but one.

**Checks after the re-audit (SDK 10.0.402):** `scripts/Build.ps1` with the retarget, 405/405 before the new test; no `WinRT.Runtime.dll` or `Microsoft.Windows.SDK.NET.dll` in the build output, and the restore names no Windows SDK pack; `Write-ThirdPartyNotices.ps1` changed only the table's target name. With the new test, `ThirdPartyNoticesTests`, `RazorMarkupTests` and `BuildInfoTests` 31/31. Fail-first, each failing its test: the MIT warranty text changed, WPF's notices changed, and the Windows SDK pack back in the restore. `Write-SourceArchive.ps1` refused this worktree's uncommitted changes and a missing output folder. Not run while Harry was in game: the retargeted meter in a window (WPF and WebView2 after the retarget), and a local portable publish (CI publishes and checks the layout on every push).

**The retarget reverted (after Harry was out of game):** a scratch `Publish-Portable.ps1 -SkipTests` of the retargeted build passed the layout check (exe 143 MB, no projection in its bundle), but the published exe crashed on start with `FileNotFoundException: Microsoft.Windows.SDK.NET` from `WebView2CompositionControl_Loaded` (event log, .NET Runtime): BlazorWebView draws the see-through overlay with WebView2's composition control, which needs the projection. The re-audit's "nothing uses it" held for Scarlet's code, not its dependencies. Back to `net10.0-windows10.0.20348.0`, with a comment on the target saying why; the guard is reversed (`TheProgramKeepsTheWindowsSdkProjectionAndNamesIt` fails if the target loses its Windows 10 SDK version or the notices stop naming the projection and https://aka.ms/WinSDKLicenseURL); THIRD-PARTY-NOTICES names the projection again, with why it is there and that RATmeter's builds bundle it too; `MIT.txt` lists C#/WinRT again; SECURITY says the GPL system-library question is unsettled and publishing with it is Harry's decision (HANDOFF). LESSONS rewritten: the lesson is that only a launch of the published exe showed it. Checks: `scripts/Build.ps1` 406/406; the reversed guard failed with the projection's framework reference renamed in the restore and with the licence link removed from the notices; the republished portable exe (161 MB) ran `--demo` in a throwaway data directory: overlay with the boss card, four rows and their effects, Settings (About: "Scarlet Meter 0.1.0", the data folder), History (the saved demo fight) and a player's details window all rendered; exit code 0; no log warnings.

**README images (after Harry was out of game, at Harry's request for a GIF):** the hero is now `docs/images/demo.gif`, 14 s of the demo's boss fight (Kill in, class effects, rows overtaking) at 30 fps, 400×260, 1.4 MB, looping: frames captured over DevTools from #24's build (DPS first, the one-line card) in a throwaway data directory, then encoded by a throwaway tool outside the repository (one 255-colour median-cut palette, see-through corners kept transparent, the translucent panel blended over a dark backdrop, each frame storing only its changed rectangle). Windows' own GIF decoder read all 426 frames, and replaying them matched the source frames at the start, middle and end (mean error 3 to 4 colour levels, no transparency mismatches). `settings-alerts.png` and `background-picture.png` are retaken from the same build (the new Alerts texts; a generated abstract picture at 50%); `overlay.png` is gone. No image has metadata chunks or comments, and every player in them is a demo player.

**Next:** Codex and CI, merge (after #24, so the release has its overlay changes); tag v0.1.0; check the draft's zips (checksums, contents, the same search); make the repository public; publish the release; then, with Harry's go-ahead, protect `main` and tags.

## 2026-10-25 — A calmer overlay: hideable event notices, "Kill in" on the name line, DPS first

**What:** A bell button in the overlay's header turns the world-event notices off or on in one click (the same `AlertSettings.Enabled` as Settings, Alerts, so it is saved; struck through and dimmed when off). A new "Hide during boss fights" switch (`AlertSettings.HideDuringBossFights`, on by default, in Settings, Alerts) makes the strip stand aside while a boss fight is under way (`Combat/BossFightHold`, `AlertStripLayout.HiddenForFight`); trash fights keep the notices. The target card is one line and a bar: "Kill in" moved onto the name and HP line, set apart by a hairline, and shows only once there is an estimate (no "--:--"). A player's row now reads DPS, total damage, share ("33.4K/s 2.95M 30.3%"), with the total in a fixed-width column too so the DPS column lines up. `AlertSettings`' hand-written equality includes the new switch. ARCHITECTURE ("Windows", "Overlay notices", tests), a HANDOFF item.

**Why:** Harry found the event notices "too much info" in a dungeon fighting bosses, and the meter "a bit convoluted", asking for "Kill in" on the same row as the boss's name and HP; then asked to swap total damage and DPS, "so 33.4K/s 2.95M 30.3%".

**Audit (deep, request changes), and what changed:** the first version hid the notices while a boss was the *current* target and its fight live or settled. (1) Settled has no end and upstream changes its target only on a hit, so after a wipe or with a boss left alive the notices stayed hidden until the next hit: `BossFightHold` now lets go after 60 seconds of still boss HP (not verified against the game's invulnerable phases) and holds again only when the boss's HP moves. (2) Upstream's target flips to adds and back, so the strip would have come and gone on every swap: the hold follows the boss, not the current target. (3) Turning the switch off was not checked to survive a save (a `[JsonIgnore]` passed every test): `HidingDuringBossFightsStartsOnAndATurnedOffChoiceIsSaved`, and since this is the second time (#22), `EveryOnOffSettingSurvivesASave` for every switch at once (LESSONS). (4) Unmounting the strip broke its own live-region rule and dropped "+N more" expanded: the strip is always mounted and held empty (`AlertStrip.Held`). (5) "Bring the meter back before an event" could restore a minimized meter mid-fight with its notice hidden: the restore now waits for the notices to come back, and happens then only if one is still due (`Alerts/AlertRestoreGate`, `IMeterWindows.HoldAlerts`). (6) On a narrow overlay the name was squeezed to an ellipsis: below a 330 px card the absolute HP gives way to the percent (a container query), and the name keeps at least 3em. (7) The page's wiring was unguarded: `RazorMarkupTests.TheOverlayHoldsItsNoticesByThePlayersSwitch`. (8) The bell's accessible name stays "World-event alerts" with `aria-pressed`. (9) "Kill in" appearing still moves the HP text over; kept, since a reserved empty slot would take room from the name. (10) ARCHITECTURE's test list.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 413/413: `BossFightHoldTests` (5), `EveryOnOffSettingSurvivesASave`, `BringingTheMeterBackWaitsForTheBossFightToEnd`, `HidingDuringBossFightsStartsOnAndATurnedOffChoiceIsSaved`, `APlayersRowShowsDpsThenTotalDamageThenShare` and `TheOverlayHoldsItsNoticesByThePlayersSwitch` new, `TheStripStandsAsideOnlyWhileABossFightIsUnderWay` reduced to the switch, and `EqualityCoversEveryProperty` caught the new property until its list and comparison were extended (as designed). Fail-first, each mutation failing its test: no hold timeout (3 hold tests), adds ending the hold, a gate that never waits, a gate that restores with nothing due, `[JsonIgnore]` on the switch (both settings tests), and the total column without a fixed width.

**Re-audit of those changes (deep, approve with nits), and what changed:** CI and Codex were clean on 308c9fa. (1) A clock stepped back while an add was the target kept the notices held for as long as the jump (the time since the boss's last move went negative): a clock that goes back now lets go whichever target is current, and the next hit on a boss holds again (`AClockThatGoesBackLetsGoUntilTheNextHit`). (2) The strip ignoring `Held`, the window layer skipping the gate, and a waited restore being dropped each passed every test: `TheOverlayHoldsItsNoticesByThePlayersSwitch` now also guards `AlertStrip.razor`'s held branch and silent live region and `MeterWindows`' two gate calls. (3) The gate test missed a gate that restores on every release and one that never forgets a waited restore: two more cases. (4) ARCHITECTURE and the strip's comment claimed slightly more than the code (a boss also holds again when it becomes the target again; an expanded strip stays expanded only while more than two notices are due; the 330 px is the card's content box, an overlay under about 370 px). (5) Settings, Alerts now says the notices come back about a minute after a wipe, and that bringing the meter back waits for a boss fight to end. (6) `EveryOnOffSettingSurvivesASave` fails if a section is ever not a class it can fill or holds a `bool?`. Checks: `scripts/Build.ps1` 414/414; fail-first, each failing its test: the old clock rule, a gate that restores on every release, a gate that keeps a waited restore, the window layer skipping the gate, and the strip announcing while held.

**Visual check (after Harry closed the meter and said checks could run; `Smoke-Meter.ps1 --demo`, throwaway data directory with every event alerted 60 minutes ahead):** during the demo's boss fight the card was one line and its bar (40 px), reading "Aultross | 136.44M / 148.79M · 91.7% | Kill in 01:10" six seconds in without overflowing, and the strip was hidden; with "Hide during boss fights" turned off in Settings, Alerts, two notices and "+1 more" came back; the bell turned them off (title "Show world-event alerts", `aria-pressed="false"`, struck through and dimmed, and the Settings switch followed) and on again; with the setting back on they stepped aside again, and after the kill ("No active target") they returned. Screenshots looked right; exit code 0; no log warnings. That check ran on the first version.

**Second visual check, on bd1491a (Harry out of game, the meter closed; same set-up, plus a run with a generated picture at 50%):** in the demo's boss fight the strip was held with its live region empty, the bell's accessible name stayed "World-event alerts" (title "Hide world-event alerts", pressed), and the first row read "dps=712.1K/s total=5.24M pct=32.3%"; the card read "Aultross | 140.01M / 156.24M · 89.6% | Kill in 01:05" without overflowing. With the page narrowed to the overlay's 300 px minimum, the absolute HP gave way ("Aultross | 89.0% | Kill in 01:05", the name 127 px wide) and came back at full width. Settings, Alerts showed the new texts for both switches. With "Hide during boss fights" off, two notices showed mid-fight and the live region read them ("Shugo Festival starts at 02:00; Dimensional Invasion starts at 02:30"); the bell turned them off and on; with the switch on again they were held; after the kill they returned and were announced. Screenshots looked right; exit code 0; no log warnings. The hold through adds and the 60-second release cannot be seen in the demo (one boss, no adds, no wipe): their tests cover them. At the 300 px minimum, player names shrink to "Dem…" beside their combat power, as the row's existing rule (the name gives way first) says; not changed here.

**Not checked:** in the game (HANDOFF); adds, wipes and the restore after a fight in a window; screen-reader behaviour.

**Next:** merge with Harry's go-ahead, before #23 so the release has these changes; then rebuild Harry's copy.

## 2026-10-24 — Picture opacity

**What:** A "Picture opacity" slider (5% to 100%, default 100%, the picture as before) under the background picture in Settings, Appearance, shown once a picture is chosen. It fades only the picture; "Background opacity" still fades the whole panel, and text and bars stay solid. In `app.css` a layer of the panel's own colour at 1 minus the picture opacity sits over the picture, under the dark veil: the same result as the picture at that opacity over the panel's colour, with no change when there is no picture. `AppearanceSettings.PictureOpacity`, clamped by the normalizer (non-finite values fall back to 100%). ARCHITECTURE ("Windows"), a HANDOFF item.

**Why:** Harry found the background picture "a bit too distracting".

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 401/401, with the normalizer test extended (an out-of-range value clamped and reported, infinity back to 100%) and the earlier-build settings test checking the default. Fail-first: dropping the clamp failed `OutOfRangeValuesAreRepairedAndReported`.

**Audit (pr-auditor, deep pass at high effort on 3b7677f): approve with nits**, applied: the value was never saved and read back in a test (`[JsonIgnore]` on it passed), and the upper bound was untested (a maximum of 100 passed), so the earlier-build test now saves and reloads 0.4 and the normalizer test checks 5 and infinity coming back as 100% with a repair (with those, `[JsonIgnore]` fails the earlier-build test and a maximum of 100 fails the normalizer test; 401/401 otherwise); the panel's colour moved to its own `background-color` declaration, so a bad value in the image layers (a comma decimal, say) drops only them instead of blanking the panel, and `--bg` notes its two hard-coded copies; ARCHITECTURE's settings sections line was stale. The auditor checked the compositing in headless Edge 155 against stacked reference elements (100%, 5%, 35% at 60% background, a half-transparent picture, no picture: equal within 8-bit rounding) and that `calc()` with `var()` works inside `rgba()` alpha in the installed WebView2 runtime. Kept: the picture's opacity survives removing the picture (the slider shows again under the next one); the repair message's wording and culture, which predate this. Codex: no findings; CI green on 3b7677f.

**Visual check (with Harry's go-ahead, not in a fight, the meter closed; `Smoke-Meter.ps1 --demo`, throwaway data directory seeded with a generated striped picture in saturated colours as the background, background opacity 90%):** the panel's style carried `--image-alpha:1`, then `0.5` and `0.15` as the slider was moved in Settings, Appearance; screenshots show the picture fully loud at 100%, clearly toned down at 50% and barely there at 15%, with the text, bars, target card and class effects solid throughout. The slider sat under the picture, read "100%" with its description, and was gone after Remove. Exit code 0; no log warnings.

**Next:** merge, rebuild Harry's copy.

## 2026-10-24 — Target card with "Kill in", class bar effects, overtake glide, combat power

**What:** The overlay's header now shows the fight's state ("Boss combat" or "Combat", a live dot, the duration); the duration left the totals line, which keeps party DPS and ping. In a fight a target card shows the name (a skull for a boss), HP and percent, a thicker HP bar and "Kill in", from a new `Combat/KillTimer`: remaining HP over how fast it fell in the last 20 seconds, nothing for the first 5 seconds, while not falling or beyond an hour, starting over on another target or rising HP. Each row shows the player's combat power beside their name ("Show combat power", default on). "Bar effects" (default on) gives every bar a gloss and a bright tip and each bar its class's animated effect (`Components/BarEffect`, `Formatting/BarEffectThemes`, `app.css` `.bar-fx.theme-*`): Gladiator slashes, Templar sheen and facets, Assassin poison smoke, daggers and bubbles, Ranger arrows, Sorcerer fire and ice, Elementalist spirits, Cleric rays and sparkles, Chanter rings, Brawler one-two punches, lightning for an unknown class, each with a shimmer in its colour; the player's own at full strength with a glow in the theme's colour round the row, the others' at 60% ("Effects on every bar", default on; off: only the player's). Rows glide to their new places, with a flash on the one that moved up, when someone overtakes (`wwwroot/js/overlay-fx.js`, FLIP through a `MutationObserver`, loaded by `index.html`). Windows' reduce-motion hides the moving pieces and stops the glide. Settings, Appearance has the three toggles. `Test-PublishLayout.ps1` requires the script. ARCHITECTURE ("Windows", tests), HANDOFF in-game items.

**Why:** Harry asked for bars that pop, with the blue bar as a lightning strike, a small move animation when one player overtakes another, "Kill in" and "Berserk in" timers, combat power on the bars (item level dropped: upstream does not decode it), and then an abstract animation theme per class ("sorcerers have fire and ice, assassin is poison/green/daggers").

**Not done: "Berserk in".** Upstream's `mobs.json` holds each mob's name, boss and dummy flags, dungeon, category and tier, and no field or code mentions enrage or berserk; nothing in the decoded traffic gives an enrage time, and neither reference repository Harry saved (A2Tools plugins, aion2timeroverlay) holds boss enrage timers; it needs a source first.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 393/393 on top of #20: `KillTimerTests` (8), `BarEffectThemesTests` (8), a settings test for a file from before these options, a markup test for the pages' scripts. Fail-first: dropping the 20-second window, the 5-second minimum and the one-hour cap each failed their test; dropping the rising-HP reset passed every test at first, so `AHealMeasuresOnlyTheFallAfterIt` was added, and it fails without it. Giving two classes one theme and listing a piece with no styles each failed; renaming a piece's style to a longer name (`.flushx`) passed the first version of the styles test, which matched a prefix, so it now matches whole names, and fails.

**Visual check (`Smoke-Meter.ps1 --demo`, throwaway data directory, while Harry was away with a go-ahead: "do what you need to do"; the meter and the game were closed):** the target card read "Aultross / 146.08M / 156.56M · 93.3% / Kill in 01:28" five seconds in, counting down after; combat power showed on all four rows; the player's Gladiator row carried the cleave effect with an orange glow (computed `box-shadow` read back), the others their own themes, fainter; an overtake was caught mid-glide with the row that moved up flagged; "Effects on every bar" off left only the player's effect, "Bar effects" off removed all of them and hid the second switch, "Show combat power" off removed the values. All ten themes were drawn on sample rows in a layer added beside the page (outside Blazor's root) and screenshotted four times each: the first pass showed most themes as a single faint spark and Assassin lost on its own green bar, so every theme got more, larger, brighter pieces and a shimmer in its colour, Assassin dark poison smoke with an acid core and outlined pieces, Gladiator a wider slash, Brawler its punch inside the bar's tip; the second and third passes showed each theme clearly, at full and at 60%. Exit code 0 every run; the log had no warnings.

**Audit (pr-auditor, deep pass at high effort on f6b9c01): approve with nits**, applied: "Kill in" restarted its warm-up whenever upstream's current target (the one last hit) flipped to an add and back, now the last four targets keep their samples; the clock-going-back reset had no test (removing it passed), now `AClockThatGoesBackStartsOver`; the glide's saved positions were measured from the panel and went stale on a row-size change, so a later reorder glided and flashed wrongly, now `.rows` is positioned, positions are measured again when the overlay's style or class changes, a row mid-glide starts from where it is drawn, and only a row that passed another flashes; the effects ran on every row and after the fight, now only the top eight bars and the player's own animate, none once the target's HP has been still for 10 s or is 0 (`Combat/FightState`, which also dims the header's dot and says "Boss defeated"); the script guard missed a `<script defer src=...>` and an inline script, now every `<script>` tag is checked; the styles guard passed with a piece's base rule gone, now it needs a rule for exactly the selector; four doc lines were behind; `--hp` unused. Not changed: the gloss painting over the effects (lifting the effects would put them over the row's text), the self-row glow clipped by the list's side padding.

**Checks after the audit:** `scripts/Build.ps1` 401/401 (`KillTimerTests` 12, `FightStateTests` 4 new). Fail-first, each restored after: removing the clock-back reset, the target cap, the rising-HP reset, the settle rule, the fight state's clock-back reset or the defeated rule each failed its test; renaming `.theme-mantra .ring` and adding a `defer` or an inline script each failed the guards. A stale-target pruning loop in `KillTimer` turned out to change nothing (trimming each target's samples to the window already starts an unseen target over), so it was removed rather than tested. Second visual check (same conditions, Harry still away): at row sizes 0.75 and 1.5, set mid-fight, each reorder glided from the rows' old places (row positions read in the list's own units, 2, 29, 56, 83 at both sizes) and the transforms cleared after the glide; every flagged row had passed another (a second flag was a still-running flash from a swap a moment earlier). Across a demo kill the engine drops the target and the rows at once ("No active target", no rows, no effects), so the demo never shows "Boss defeated"; "Kill in" read 01:10 five seconds into a fight that ended at about 75 s.

**Not checked:** in the game, over the game's picture, and the cost in frames with a full party (HANDOFF); whether the game keeps a dead boss as the target at 0 HP (then "Boss defeated" shows) or drops it as the demo does; reduced motion (no way to emulate it from the smoke script).

**Next:** Codex and CI, merge; rebuild Harry's portable copy once the meter is closed.

## 2026-10-24 — Expandable "+N more" in the alert strip

**What:** The overlay's alert strip still shows two notices, but "+N more" is now a button: hovering it lists the hidden notices, one per line with their location and local start or close time (clock times, so the tooltip does not go stale while open); clicking it expands the strip to every due notice with its countdown and progress bar, and "Show less" collapses it. Expanded, the strip takes at most 45% of the meter's height and scrolls beyond, so the rows stay visible; it collapses by itself once no notice is hidden, so it starts collapsed the next time more are due. The rules live in a new pure helper, `Alerts/AlertStripLayout` (shown list, hidden count, whether it stays expanded, the tooltip, one notice's description, now also used for each notice's own tooltip); `AlertStrip.razor` keeps only the expanded flag. The button carries `aria-expanded`. ARCHITECTURE ("Overlay notices", tests) and a HANDOFF in-game item.

**Why:** with a longer "Warn this long before", more events overlap and Harry could not see the one behind "+1 more".

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 375/375, 3 new in `AlertTests` (`TheStripShowsTwoNoticesUntilExpandedThenAll`, `AnExpandedStripCollapsesOnceEveryNoticeFits`, `TheMoreTooltipListsEachHiddenNoticeWithItsClockTime`). Fail-first: always taking two, staying expanded regardless of the count, and listing every notice in the tooltip each failed its test (the test class run alone with the xunit runner's `-class` option after each change; the helper restored after). After the audit's fixes, 375/375 again, with the three-notice case and the collapse hint added to the tests.

**Audit (pr-auditor, standard pass on b7f0d6d): request changes**, applied. Major (from the CSS, not rendered): expanded, each notice could shrink below its height instead of the strip scrolling, because a flex item with `overflow: hidden` has no minimum height; fixed with `flex: none` on `.alert` (LESSONS.md). Nits, applied: the "Show less" tooltip hard-coded "two" (now `AlertStripLayout.CollapseHint`, from the constant); the three-notice case was untested; the test fixture's countdowns implied different current times (now all from one instant). It checked as sound: state across the overlay's refreshes, the collapse rule, `@key`, the click not reaching the header drag or the rows, `aria-expanded`, the unchanged announcement, the button's styles, and the docs. Codex raised the same shrinking finding (P2 on b7f0d6d), answered as fixed.

**Visual check (with Harry's go-ahead, out of game; `Smoke-Meter.ps1 --demo` on a throwaway data directory with all nine events alerted 60 minutes ahead, the server offset moved to 11 through the settings page so four were due; the 260 px default overlay):**

- First run, on 6f64b13: collapsed, the strip was 82 px with two 26 px notices and "+2 more" (`aria-expanded="false"`); its tooltip held "Watcher Kaira · Abandoned Island · ... · starts 22:00" and "Shugo Festival · Shugo Pavilion · ... · starts 22:00" on separate lines (read from the `title`; the native tooltip itself was not screenshotted). Expanded: 123 px visible of 140 px, scrolling, four 26 px notices, rows still 73 px, `aria-expanded="true"`, tooltip "Show only the first 2", still expanded after the overlay's refreshes, collapsed again on "Show less". Exit code 0; the log's only warning was the hotkey already held by Harry's own running meter.
- The screenshots showed two more things to fix: expanded, "Show less" sat below the strip's scroll fold, so collapsing needed a scroll first; and a half-scrolled notice met the half-cut damage row with no divider. Fixed: "Show less" is sticky at the bottom of the expanded strip, on an opaque background so scrolled notices do not show through, and the expanded strip has a top line (`alerts.css`).
- Re-run after the fix: "Show less" visible at the bottom without scrolling, the divider above the strip, notices 26 px, 126 px visible of 143 px; collapsed and expanded states as before; exit code 0. Scrolled, a half-hidden notice still sits right under the divider next to the half-cut row above it, as at any two scroll areas' shared edge; left as is.

**Not checked:** the native tooltip's rendering; in the game (HANDOFF).

**Next:** Codex and CI, merge; rebuild the portable copy with `Publish-Portable.ps1` once the meter is closed.

## 2026-10-24 — One portable copy, updated in place; repository renamed Scarlet-Meter

**What:** `scripts/Publish-Portable.ps1` keeps one ready-to-run copy, `<checkout>\portable\ScarletMeter`, instead of a new stamped folder and zip per build. It builds into `ScarletMeter.staging`, checks it, lists the build's top-level files in a manifest (`.scarlet-portable-files`), adds the marker, then swaps it in by renames that carry everything of the player's over: the data folder and anything else the manifest does not list (live to `.old`, the player's entries into the new build, new build to the live name, `.old` deleted). Only files a manifest lists are ever deleted, and a delete removes the markers last and refuses a folder holding anything of the player's. A clash with a new build file is refused before anything moves; a failed rename is undone, and an undo that fails leaves the copy's name free for the next run to finish, with a message saying where everything is; a run cut short is finished or undone at the start of the next; only folders with its markers (or empty ones) are touched, and links are refused. It refuses while the meter runs from the copy (before building and before the swap), while another run updates the same output root (a named mutex), and an output root that is relative, inside a portable copy or elsewhere in the checkout; a drive root stays `C:\`. `-Zip` writes `ScarletMeter-win-x64.zip` after the swap (only the manifest's files, the manifest and the marker), and without it an older zip of ours is removed; builds earlier versions left under stamped names are removed when they hold only a build's files, and their zips when they are ours. `InstallFolders.ps1`'s `Remove-OwnedFolder` takes the markers to keep until last. The folder rules are a new library, `scripts/PortableFolders.ps1` (on `InstallFolders.ps1`'s path helpers, whose running-meter check now takes the retry wording), checked by the new `scripts/Test-PortableFolders.ps1` on throwaway folders. `Update-Meter.ps1` refuses up front while the meter runs from the copy and updates it in place; Settings, About says to close the meter, run the command, and start it again from the same exe. `PortableLayoutTests` checks the library's marker and data folder names against the app's. CI runs the folder rules and builds the copy twice (the second over a planted settings file). README, DEVELOPMENT, ARCHITECTURE, SECURITY (never hand on the live folder, never `git clean -x`), HANDOFF, the scarlet-upstream-update skill and two lessons entries.

The GitHub repository is renamed from `ScarletMeter` to `Scarlet-Meter` (Harry wants a divider; GitHub names cannot hold spaces). The old URL redirects; the checkout's `origin` now points at the new one, and the docs, the scarlet-ship skill and the scarlet-auditor agent use `harrynguon/Scarlet-Meter`. The local checkout folder keeps its name.

**Why:** Harry asked for the build to be unzipped, with no copies kept, only the latest build, so there is one exe to click.

**Checks (SDK 10.0.402, Windows PowerShell 5.1; no window opened):**

- `scripts/Test-PortableFolders.ps1`: 50/50 (first copy; an update replacing the build's files, dropping one only the old build had, and keeping `data`, a `data - Copy` backup and notes; a clash refused with nothing moved; a folder without the marker and a copy holding a junction refused, the junction's target untouched; a drive root kept as the root, an output root inside a copy and a relative one refused; a cut-short update undone from `.old`, and finished from `.staging` with the rest of the player's files following and the staging marker removed; leftovers holding the player's files refused and kept, a staging folder holding data never deleted; someone else's folder at the staging name refused; a delete stopped by an open file keeping its markers and finished by the next run; stamped builds with only a build's files removed, with data or a file of the player's kept, a look-alike untouched, our stamped zip removed and a look-alike zip kept; the zip holding exactly the build, its manifest and the marker; a hidden ping.exe renamed `ScarletMeter.exe` running from the copy refused; a second run at once refused from a child PowerShell process and allowed after; a failed step 1 changing nothing; a step 3 failure with a file held open undone with everything back; an undo whose move back fails, leaving the name free and finished by the next run; a failed last undo, put back by the next run). Fail-first, each guard removed in turn and the library restored byte for byte after: the marker-last delete, the stranded-undo branch, the drive-root separator, the player-files rule, the lock and the staging-marker removal each made their cases fail.
- After the second audit, `Test-PortableFolders.ps1`: 59/59, adding a recovery stopped by a held-open file in `.old` (says not to start the meter, keeps the staging marker) and resumed by the next run; a recovery clash stopped with both copies kept; a manifest listing `data.` not trusted and no zip written from it; a device-path output root refused with a clear message; an earlier build whose inner folder is a junction kept with its target untouched. Fail-first: dropping the staging marker before the rest follows (the old behaviour) failed the resume case with the very refusal Codex described; deleting through the inner junction, accepting `data.` and accepting a device path each failed their case.
- `scripts/Test-InstallMeter.ps1` (for the `Remove-OwnedFolder` change): passes.
- `Publish-Portable.ps1` twice into a scratch output root outside the checkout: the first (with the tests, 372/372, and `-Zip`) made the copy, with the manifest `assets, GameData, ScarletMeter.exe, ScarletMeter.staticwebassets.endpoints.json, wwwroot` (the same entries as the stamped builds on the main checkout), and the zip; with a stamped build and its zip copied from it, `data\settings.json` and `data - Copy\settings.json` planted, the second (`-SkipTests`) replaced the exe, kept both, said "Kept your files beside it: data, data - Copy.", removed the older zip and the stamped build and zip, and left only `ScarletMeter` with one `ScarletMeter.exe`. (The first version of this change passed the same two runs before the audit.)
- `Update-Meter.ps1 -Tag v1.11.5.341` said it was already on that release; `-Tag v1.11.4.334` was refused as older. All five scripts parse.
- Repository rename: `gh repo view harrynguon/Scarlet-Meter` answers, `git ls-remote origin` through the new URL lists `main`.

**Audit (pr-auditor, deep pass at high effort on 12b2bea): request changes**, all applied above. Major: anything in the copy other than `data` (a `data - Copy` backup) was deleted on update, and a nested copy's data could be; a step 3 failure whose undo also failed said "as they were" while the data sat in `.staging`; deletes removed the portable markers in directory order, so a delete stopped by an open file left a folder every later run refused (Codex raised this too, P2 on `PortableFolders.ps1`); a drive root as output root resolved to the shell's current directory. Minor: no lock against two runs at once and no data guard in the delete itself; zips deleted by name only. Nits: the staging marker left after a finished restore, `.old` kept silently, Update-Meter claiming data was kept on a first copy, a raw error for a step 1 failure, the zip written before the swap was allowed. The auditor reproduced the first three and the drive root with probe scripts on throwaway folders; LESSONS.md has the entry.

**Second audit (pr-auditor, deep pass at high effort on 5dc9178): request changes**, all applied. It confirmed every first-audit finding fixed (each guard removed made its tests fail). Major, also Codex's P2 on 5dc9178: a recovery dropped the staging marker before the rest of the player's files had followed from `.old`, so a recovery stopped there (a held-open file) left a copy without its data that every later run refused; reproduced. Minor: the earlier-build clean-up deleted through an inner `ScarletMeter` folder that was a junction. Nits: a manifest line `data.` was accepted (Windows opens it as `data`), no instructions for moving data out of a kept earlier build, `Exit-PortableLock` could mask an error, a device-path root gave an obscure error, two doc lines out of date. Fixed by keeping the staging marker on the copy until the rest has followed (a resume branch in `Restore-InterruptedPortableUpdate`, with a clear message when it stops or clashes), `Remove-PortableFolder` refusing a folder that is a link, manifest names with a leading space or a trailing dot or space refused and `Write-PortableZip` checking the name Windows opens, the kept-build warning saying what to do, `try`/`finally` in `Exit-PortableLock`, a clear refusal for device paths, and the docs. The auditor judged staging without a manifest, every cut point in step 2, case-insensitive clashes and each delete caller sound.

**Deployed (before merge, with the player's go-ahead):** `Publish-Portable.ps1 -SkipTests -OutputRoot <main checkout>\portable` from this branch at 5dc9178 made `portable\ScarletMeter\ScarletMeter.exe` and removed the two stamped builds and their zips (neither held data); the main checkout's `git status` stayed clean. The meter's code is main's; only the scripts differ.

**Not checked:** the meter started from an updated copy (it needs a window; HANDOFF has the in-game item); whether WebView2 helper processes outlive the meter and block step 1 (the error says what to close); a full `Update-Meter.ps1` run to a new release (none is out); pwsh locally (CI runs the scripts under pwsh); an elevated and a non-elevated run sharing the mutex.

**Next:** Codex and CI on the fixes, merge; Harry tries the meter in the game (HANDOFF).

## 2026-10-24 — Ask before checks that open windows

**What:** AGENTS.md "Review and merge", the scarlet-smoke skill and a lessons entry now say to ask the player before any check that opens windows on their desktop or uses the mouse.

**Why:** the smoke runs for the screenshot tour and the share bars opened the meter's windows over Harry's game, took focus, and got Harry killed in AION 2.

**Checks:** Markdown only: Codex's review and CI.

**Next:** follow it.

## 2026-10-24 — Clearer share and uptime bars

**What:** The bars in the details window (skill damage, buff uptime) and the saved fight's party table use a red gradient like the overlay's instead of a flat 32% red, over a faint full-width track; the saved fight's share bars take the overlay's class gradients when "Class colours" is on (`ClassColors.CssClass`, now shared with the overlay's rows); labels over a bar get a shadow and a lighter secondary colour. A lessons entry.

**Why:** Harry found the share bars in the saved-fight and details views hard to see.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 372/372, 5 new (`FormattingTests.ClassIdsMapToTheirGradientOrTheNeutralOne`). `Smoke-Meter.ps1 --demo`: the details Skills tab showed bright gradient bars with readable labels; the saved fight's four share bars carried `fill class-11`, `class-16`, `class-14` and `class-17` with their gradients (computed style read from the page) and drew in blue, teal, green and gold (screenshots, not committed).

**Audit (pr-auditor, standard pass on 42aecf2): approve with nits**, applied: each bar was sized against the whole cell while the new track was 12 px narrower, so a 50% share reached about 54% of the track and anything over about 92% looked full; the bar now sits inside the track (`.cell-track`), and in the running app every bar measured exactly its label's share (58.1% of the track for 58.1%, 31.3% for 31.3%). Overlay rows, `.fill` scoping, specificity, stacking and the settings refresh were checked by reading and found unchanged or correct. 372/372 after the fix.

**Next:** Codex's review and CI on the final head, then merge.

## 2026-10-24 — One-command update of a portable copy

**What:** `scripts/Update-Meter.ps1` finds RATmeter's latest release on GitHub (or takes `-Tag`), stops when already on it, and otherwise, in a temporary worktree on a local branch `update/upstream-<release>`, moves the pin with `Update-Upstream.ps1` (Debug build and tests), commits it and builds the portable zip with `Publish-Portable.ps1` (Release build and tests) into `<checkout>\portable`. It removes the worktree, and a branch it created without committing to. Settings, About shows the one command for a portable copy; README, DEVELOPMENT, ARCHITECTURE, the scarlet-upstream-update skill and a lessons entry.

**Why:** Harry asked for one command that moves the upstream pin to the latest release, runs the tests and builds the new portable zip.

**Checks (SDK 10.0.402, Windows PowerShell 5.1):**

- On a checkout pinned to v1.11.5.341: "Already on RATmeter v1.11.5.341; nothing to update." (one request to GitHub's latest-release API).
- On a throwaway clone with the pin moved back to v1.11.4.334: it found v1.11.5.341, moved the pin in a worktree under the temp folder, built and passed 367/367 in Debug and again in Release, and wrote `ScarletMeter-3d14bcf-upstream-v1.11.5.341-win-x64.zip` (65 MB) to the clone's `portable`. The clone's own pin stayed v1.11.4.334 with a clean status; the branch held the pin move (aa3ffc6); no worktree or temp folder was left. That run was the second: the first was cut short by the test command (LESSONS.md), and the second reused the branch the first had committed.
- Refusals: `-Tag v9.9.9.9` (not a release) stopped before building, said so, and left no branch, worktree or temp folder; `-Tag v1.11.4.334` on the newer checkout was refused; a malformed tag failed parameter validation.

**Not checked:** a release that really breaks the engine's build (the failure path is the same `catch` the missing-tag case went through); pwsh locally (CI does not run this script, since it needs GitHub and minutes of builds).

**Review of #16:** Codex (one P2) and pr-auditor (deep pass: request changes) both found that a reused branch was built as it was, so it could miss commits made since; the audit also found the current release was read from `upstream/`'s working tree (stale after a plain `git pull`, in which case the closing message named a branch the run had just deleted), git's raw stderr replacing the script's message under Windows PowerShell 5.1 with output redirected, docs saying "the checkout is untouched" though a branch is added, a tag pattern looser than the app's (case, Unicode digits, a trailing newline), and the skill not saying to check the branch holds only the pin move. All fixed: a reused branch must contain HEAD; the release comes from `HEAD:upstream` (unknown releases warn and go ahead); git failures carry git's last lines in the script's message; the branch line prints only when the branch exists; both scripts validate tags with `-cmatch '\Av[0-9]{1,9}(\.[0-9]{1,9}){3}\z'`; docs, skill and a lessons entry corrected.

Checks after the fixes, on a throwaway clone under Windows PowerShell 5.1: with HEAD pinning v1.11.5.341 but `upstream/`'s files left at v1.11.4.334 it said "Already on RATmeter v1.11.5.341"; a stale `update/upstream-v1.11.5.341` made from an older commit was refused, untouched, with nothing left behind; a fresh run from v1.11.4.334 with the GitHub lookup moved the pin, passed 367/367 in Debug and Release, wrote a 65 MB zip, left the clone's files untouched and the branch holding the pin move (and HEAD), printed the branch line, and left no worktree or temp folder.

**Next:** CI, Codex's review of the fixes, and merge.

## 2026-10-24 — Portable build, like RATmeter

**What:**

- `scripts/Publish-Portable.ps1`: builds and tests Release, publishes the self-contained single-file build into a new folder, checks the layout, adds a `ScarletMeter.portable` marker and zips it with `ScarletMeter` at the top. It never deletes or overwrites: a taken name gets a counter.
- `PortableLayout`: with the marker beside the executable, the data directory is the `data` folder beside it instead of `%LocalAppData%\ScarletMeter`; `--data-dir` still wins. A portable copy that cannot create `data` stops with a message naming it.
- Settings, About shows the portable update commands (extract the new zip over the folder) when running portable.
- CI builds the portable zip and checks it holds the exe, the marker and `wwwroot\index.html` under `ScarletMeter/` and no `data` folder; the install smoke now also checks an installed build carries no portable marker. `portable/` is git-ignored.
- README "Run it" leads with the portable zip; DEVELOPMENT "Portable build", ARCHITECTURE "Storage" and "Updates", SECURITY, HANDOFF and a lessons entry.

**Why:** Harry asked whether it could be portable, like RATmeter, instead of needing an installation.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`: 367/367, 5 new (`PortableLayoutTests`).
- `Publish-Portable.ps1` with the tests, into a scratch folder: a 165 MB folder and a 66 MB zip (RATmeter v1.11.5.341's zip is 114 MB).
- The zip extracted elsewhere and `ScarletMeter.exe --demo` started from it with `DOTNET_ROOT` and `SCARLET_DOTNET` removed and the working directory set to the Windows folder: the overlay opened ("Scarlet Meter"); `ScarletMeter\data` filled with the logs, the WebView2 profile and the demo history; `%LocalAppData%\ScarletMeter` did not exist before or after; it closed with exit code 0 and no warnings in the log.
- The zip extracted again over that copy (`Expand-Archive -Force`): the data folder, its history and the settings saved on close were kept.

**Not checked:** the message for a portable folder where `data` cannot be created (it is the start-up error path every other start failure uses); the portable copy in the game; Windows Explorer's own "Extract all" over a copy (it asks before replacing files, and the zip holds no `data`).

**Audit (pr-auditor, deep pass on ba769fe): approve with nits**, applied. It also checked, on a clean clone under Windows PowerShell 5.1, the publish (165 MB, 32 entries) and the "cannot create data" message (it names the path and the way out). Fixes:

- The zip, meant to be handed on, held the builder's Windows account path (in the debug directory of the exe) plus five PDBs and three WebView2 XML docs. The publish now uses `DebugType=none`, `ContinuousIntegrationBuild` and no documentation files: 24 entries, 65 MB, and no file names the account or the checkout path (searched as ASCII and UTF-16). SECURITY.md says the zip is not reviewed for public release.
- "Extract over this folder" was ambiguous; the README, the About tab and the docs now say to extract into the folder that holds `ScarletMeter`, replacing files.
- An output root elsewhere inside the checkout is refused (a copy run there could leave history and logs where git sees them); the README says to unzip outside the checkout.
- `Install-Meter.ps1` refuses an install, `.previous` or `.retired` folder holding `ScarletMeter.portable` or `data`: an install would move that data aside and delete it on the next install. New Test-InstallMeter case.
- Zip entries are written one by one with forward slashes (Windows PowerShell 5.1's `CreateFromDirectory` wrote backslashes); "-dirty" now counts untracked files as Install-Meter does; the docs name `%TEMP%.netScarletMeter`, where single-file apps unpack native libraries.

Checks after the fixes: `Build.ps1` 367/367; `Test-InstallMeter.ps1` all cases ok, the new one included; the inside-checkout refusal; `Publish-Portable.ps1 -SkipTests` (24 entries, forward slashes, no PDB or XML, no account or checkout path in any file); the extracted copy started with `--demo` without `DOTNET_ROOT`, filled `data` beside the exe, left `%LocalAppData%ScarletMeter` absent and exited 0.

**Next:** Codex's review of the fix commit, CI and merge.

## 2026-10-24 — Requirements and how to run, in the README

**What:** README "Requirements" separates running (Windows, Npcap in WinPcap API-compatible mode with administrator-only access left off, the WebView2 runtime, no .NET install since the installed build is self-contained) from building (.NET 10 SDK, `SCARLET_DOTNET` when it is not on PATH, Git). "Run it" says where the installed exe is (`%LocalAppData%\Programs\ScarletMeter\ScarletMeter.exe`, and the Start menu with `-Shortcut`), the `--demo` command, and what a real start shows. DEVELOPMENT.md's opening matches. A lessons entry records why this was missing.

**Why:** Harry asked where to run the exe, and for Npcap and any .NET requirement to be written down.

**Checks:** the claims against the code: `Install-Meter.ps1` publishes `--self-contained true` for win-x64 and installs to `%LocalAppData%\Programs\ScarletMeter`, `-Shortcut` creates "Scarlet Meter.lnk" in the user's Start menu Programs folder; the engine's capture-start error asks for WinPcap API-compatible mode because SharpPcap loads `wpcap.dll` (`UpstreamCombatEngine`); `GetDpiForSystem` needs Windows 10 1607 or later. Not checked: Windows 10 itself, and the Npcap administrator-only option (Npcap's own installer documents it).

**Next:** Codex's review and CI, then merge.

## 2026-10-24 — Codex reviews of PRs #1 to #3, and a lessons log

**What:** Codex's review of #1, #2 and #3 never finished when those PRs were opened, so it was requested again (`@codex review`); it reviewed each PR's merged commit. #3: no findings. Three findings, all still true on `main` (871702e), are fixed here:

- #1, P1: the boundary test's helpers `IsVisibleOutsideAssembly` and `InheritedTypesOf` had no purpose comments (AGENTS.md, "Structure and comments"). They now say what they count and why protected members do.
- #2, P2, `SettingsStore`: with `settings.json` missing, a backup that was damaged, from a newer build or locked still left saving on, so the second save's `File.Replace` would have overwritten the only recoverable settings. Such a backup is now protected as a main file would be: a damaged one is kept aside, one from a newer build is used read-only (as a newer main file is), and saving is switched off when it cannot be opened or moved.
- #2, P2, `WindowPlacementTracker`: a saved placement was checked against the virtual screen's bounding box, so after a layout change it could restore into the empty corner of an L-shaped layout, off every monitor. `ScreenPlacement` now checks each monitor (`EnumDisplayMonitors`, at the system DPI that SystemParameters uses), falling back to the virtual screen if none can be listed.

Also docs/LESSONS.md: the mistakes found so far, why they got through and what now guards them; AGENTS.md "Lessons" asks for an entry with every fix.

**Why:** Harry asked for Codex to review #1 to #3 and for anything still applicable to be fixed, and for mistakes to be learnt from and documented as work goes on.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` Debug and Release: 362/362, 8 new (`SettingsTests` four, `ScreenPlacementTests` four). The settings tests failed with the old missing-file behaviour put back (an early return to defaults), then passed.

**Not checked:** a real L-shaped or mixed-DPI monitor layout (this machine has the layout it has; the check is tested on synthetic rectangles). A per-monitor-DPI layout uses system-DPI units, as `SystemParameters` and the old check did.

**Audit (pr-auditor, deep pass on aed44d6): approve with nits**, applied: a backup from a newer build is now used read-only, as a newer main file is, rather than set aside (the player keeps their settings and nothing is overwritten), with its version logged; the `ResetToDefaults` comment no longer says both files were kept aside; the locked- and unmovable-backup tests assert their notices, so each pins its branch; the monitor-listing test says it only proves the Win32 calls run. Checks after the fixes: 362/362 Debug and Release.

**Next:** Codex's review of the fix commit, CI and merge.

## 2026-10-24 — Codex review comments on PRs #1 to #11

**What:** Every comment the Codex bot (chatgpt-codex-connector) left on PRs #1 to #11, checked against `main` at b76c148, and the two that still held fixed. AGENTS.md now has a Security section (no API keys, tokens, passwords or other credentials anywhere in the repository; no paths that depend on the starting directory) and makes reading every PR review comment, Codex's included, part of review and merge; the scarlet-ship skill and the scarlet-auditor agent say how.

**Why:** Harry asked for every Codex comment on the eleven PRs to be reviewed and fixed, for PR comments to be checked from now on, and for the security rules to be written down.

**The findings** (Codex reviewed each PR at the commit it was opened with):

- #4, P1, `Install-Meter.ps1`: a junction in the install path's ancestors could alias the data directory or the checkout. Already fixed in #4 (7903008, b5ea0b2): the nearest existing ancestor is resolved with `GetFinalPathNameByHandle` before the overlap checks, with Test-InstallMeter cases for junctions to the data directory and the checkout.
- #4, P2, README: promised a release notice the UI did not show yet. Fixed by #10, which shows it.
- #5, P2, `HistoryQuery`: a huge page number wrapped upstream's 32-bit offset. Already fixed in #5 (`MaxPage` 1,000,000, with tests).
- #6, P2, `AlertMonitor`: the next-event line skipped a rule whose current occurrence was still running (Shugo for four minutes after its entry closed, Invasion for ten). **Fixed here:** it takes each rule's `Next`; `AlertView.Next` is now an `UpcomingEvent` (rule and start). Test `TheNextEventLineKeepsAnEventWhoseCurrentOccurrenceIsStillRunning`, Codex's 01:04 UTC case and an Invasion one, fails with the old filter.
- #8, P2, `DamageChart`: buff-lane icons ignored "Skill icons". Already fixed in #8 (2cb7ba7), also found by the audit.
- #8, P2, `PlayerDetailsPage`: a late saved-fight load could relabel the window. Already fixed in #8 (2cb7ba7).
- #9, P2, scarlet-smoke skill: the `--demo` example did not run on the main of that moment. Resolved when #5 and #8 merged; the example runs now (used in this session's smoke runs).
- #10, P1, Settings, About: the update commands were relative (`scripts\Update-Upstream.ps1`), so they only worked from the checkout. **Fixed here:** `& "<checkout>\scripts\Update-Upstream.ps1" -Tag <tag>` and `& "<checkout>\scripts\Install-Meter.ps1"`, as DEVELOPMENT.md writes them. New `RazorMarkupTests.ScriptCommandsShownToThePlayerAreRootedAtTheCheckout` fails with the old line put back.
- #7 and #11: no findings (thumbs-up). #1, #2 and #3: Codex's review never finished (its summary still says "Running"), so those PRs have no Codex findings at all.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`: 354/354; each of the two new tests failed with its bug put back, then passed.
- Smoke run built briefly against the older pin (upstream checked out at v1.11.4.334 in the worktree, then restored to aa3ffc6), so the real release check found v1.11.5.341: the overlay line appeared, and Settings, About read `& "<checkout>\scripts\Update-Upstream.ps1" -Tag v1.11.5.341` and `& "<checkout>\scripts\Install-Meter.ps1"`; exit code 0.

**Not checked:** the next-event line in the running app at the exact moment an entry closes (the computation is tested; the strip renders whatever `Next` holds).

**Review of this PR (#12):** Codex reviewed 8bca709 with no findings (thumbs-up). pr-auditor, standard pass: approve with nits, applied: AGENTS.md no longer says docs-only changes need "no review beyond CI" (they need no audit, but still get Codex's review), and the markup guard matches only `scripts<name>.ps1` commands, so a web asset path cannot trip it (it still fails with the old line put back; 354/354).

**Next:** Codex's review of the fix commit, CI and merge. Offer to ask Codex to review #1 to #3 again.

## 2026-10-24 — Agent tooling: auditor and skills

**What:** `.claude/agents/scarlet-auditor.md`, an independent read-only pre-merge reviewer (standard or deep pass, verdict with evidence), and three skills in `.claude/skills/`: `scarlet-ship` (worktree, checks, docs, PR, audit, CI, squash merge pinned to the tested commit), `scarlet-smoke` (driving the real app's pages through WebView2 remote debugging with `Smoke-Meter.ps1`, including `--demo`, seeded settings, alerts and the release notice) and `scarlet-upstream-update` (moving the upstream pin to a release and what to check). AGENTS.md "Review and merge" now names the auditor and the skills.

**Why:** Harry asked for agents and skills that will be useful again. They capture the workflow used for PRs #2 to #8 and the pitfalls met on the way (DOM nodes in `Eval`, Razor's e-mail rule, PowerShell quoting, long paths, stacked PRs and docs conflicts).

**Checks:** Markdown only, so no audit (AGENTS.md); CI. The skills' commands and selectors are the ones used in this session's smoke runs.

**Next:** use them; correct a skill when a step it describes turns out wrong.

## 2026-10-24 — Update notice in the overlay

**What:** The upstream release check reaches the player (ARCHITECTURE.md "Updates", "Notice").

- `UpdateNotice` asks `UpstreamReleaseChecker` 30 s after start-up and whenever its cached answer expires.
- A newer release shows one quiet line above the alert strip ("RATmeter v… is out", "Release notes", a cross that hides it until the next release).
- Settings, About: a switch (on by default), the release the build is on, the last result, and the update commands when a release is out.
- `UpdateSettings` (`settings.json` `updates`): the switch and the dismissed release, validated as a release tag.
- `MeterWindows.OpenReleasePage` opens only an https page under upstream's releases on github.com.

**Why:** Harry asked for an automatic updater "if it's even possible, and reasonable"; the meter is for Harry's own use and has no published binaries, so the reasonable form is a notice plus a one-command reinstall (PR #4). This is the notice.

**Checks (SDK 10.0.402, on the update-check branch at fb24e0a):**

- `scripts/Build.ps1`: 284/284, 16 of them new (`UpdateNoticeTests`), and `SettingsTests.EqualityCoversEveryProperty` extended for the new section.
- Smoke run (`Smoke-Meter.ps1`, `--no-capture`, throwaway data directory). This branch was still pinned to v1.11.4.334, so the real check, one GET to api.github.com, found v1.11.5.341:
  - no line at start; about 30 s later "RATmeter v1.11.5.341 is out | Release notes", its tooltip naming v1.11.4.334;
  - Settings, About read "v1.11.5.341 is out (published 24 Oct); this build is on v1.11.4.334 (checked 06:24)" and gave `scripts\Update-Upstream.ps1 -Tag v1.11.5.341`, then `scripts\Install-Meter.ps1`;
  - the cross removed the line and saved `"dismissedRelease": "v1.11.5.341"`; the checker logged its result once; exit code 0.

**Not checked:** "Release notes" opening the browser (not clicked in the smoke run, so no browser was launched); the line over the game.

**Compatibility:** no version bump, as the `updates` section is additive. A file without it, or with `"updates": null`, loads with checking on. An older build ignores the section and drops it on its next save, so a player who turned checking off finds it on again after going back and forth; a settings file reset to defaults does the same. SECURITY.md says so.

**Audit (pr-auditor, deep pass on 3243ad0): approve with nits**, applied after merging main (#4, e30cbc1):

- Minor: a timer callback already queued when the switch went off or the meter closed could still call the checker. `CheckAsync` now returns before asking when the switch is off or the notice is disposed.
- Minor: tests missed the privacy paths (the auditor's mutations survived). New: a check in flight when the switch goes off books no other, one in flight when the meter closes sees its token cancelled and books no other, other settings changes neither ask nor redraw, dismissing asks nothing, switching off redraws.
- Minor (docs): the opt-out can revert (above, and SECURITY.md).
- Nits: the release-page check's comment and ARCHITECTURE say what it does (Uri resolves dot segments; the `..` test only catches what Uri leaves); a fragment or any `@` is now refused too, with test rows; a refused address is no longer logged; only a change to the `updates` section re-renders or re-checks.

Checks after the fixes: `scripts/Build.ps1` 291/291 on the merged tree (upstream v1.11.5.341).

**Next:** CI and merge.
## 2026-10-24 — Player details and history windows

**What:** The UI for the engine's details and history (ARCHITECTURE "Player details and history windows").

- **Details window:** clicking a player in the overlay opens it (one window, re-targeted by `DetailsSelection`).
  - The header shows RATmeter's stats.
  - Skills tab: RATmeter's columns, with expandable summon groups.
  - Buffs tab: uptime.
  - Graph tab: an SVG chart of cumulative DPS and per-second damage, with buff lanes.
  - Log tab: filters by skill and flags, virtualized.
- **History window:** the new header button opens it.
  - Boss-name and date filters, 50 fights per page.
  - A saved fight's party, with RATmeter's Copy DPS line.
  - Players open in the details window from the saved data.
- **Icons and settings:**
  - Class icons by id.
  - Skill and buff icons only from NC's CDN path, behind a new "Skill icons" setting (on by default).
  - `views.css` (also checked by `Test-PublishLayout.ps1`).
- **The engine's final contract:** a saved fight the engine cannot read says so (in history and in details), a player missing from a saved fight's details reads "missing from the saved fight, or could not be read", and an incomplete history page (`IsComplete` false) shows a notice that some fights are left out.

**Why:** Harry asked for RATmeter's per-player details (skill breakdown, buffs, damage graph, combat log) and a history browser for past boss fights, with heavy inspiration from RATmeter. The Explore survey of upstream's UI gave the columns, filters, Copy DPS format and chart series.

**Checks (SDK 10.0.402).** Final, on main 40206be (world-event notices, upstream v1.11.5.341) merged with the engine's final head a7777cb:

- `scripts/Build.ps1` Debug and Release: 235/235, 12 of them new for this PR (`DetailFormatTests`, `RazorMarkupTests`).
- `Smoke-Meter.ps1 --demo` (Debug), with alerts seeded for Shugo and the rift at a 60-minute lead: four demo rows and "Aultross"; the strip read "Spacetime Rift · in 44:10" and "Shugo Festival · in 44:10" (both at 07:00 local); details with Skills (five rows), Buffs, Graph (both series and buff lanes) and Log ("12 of 12 hits, newest first"); history listed the fight and opened its party of four. No warnings in the log; exit code 0.
- **Found in that smoke:** the Buffs tab showed the literal text "x@buff.Applications", and the chart's lane tooltips "x@lane.Count": Razor reads a letter followed by `@` as an e-mail address and does not evaluate it. Fixed with `x@(...)`; the new `RazorMarkupTests` scans every `.razor` file for the pattern (it failed with the bug put back, and passes). The first smoke runs below counted buff rows without reading them, which is how it got through.

Earlier, on main plus the engine branch before its review fixes:

- `scripts/Build.ps1`: 203/203 tests, 11 of them new (`DetailFormatTests`):
  - lanes merge, clip and sort;
  - chart points;
  - Copy DPS for the top ten through the masking delegate;
  - skill-icon URL validation, including lookalike hosts, dot segments and plain HTTP;
  - icons switch off;
  - hit tags.
- Smoke runs through `Smoke-Meter.ps1 --demo` on the Debug build:
  - **Overlay:** four demo rows with class gradients and icons, and the boss "Aultross" with its HP.
  - **Opening details:** clicking the top row opened details for "Demo Ranger" (Live).
  - **Skills tab:** four rows, for example "Rapid Fire 6 hits, 50% crit, avg 440.1K, max 990,664, 2.64M 50.5%". Skill icons loaded from the CDN for two of the four skills.
  - **Buffs tab:** three rows.
  - **Graph:** the cumulative-DPS path and three buff lanes.
  - **Log:** the skill filter, the flag chips, "13 of 13 hits, newest first", and summon hits labelled with the pet.
  - **History, during the first fight:** the list loaded.
  - **History, after the fight completed (73 s):**
    - a search listed "Aultross · 01:13 · 148.15M · 2.00M/s · 4" as completed
    - opening it showed the party
    - Copy DPS put "Demo Gladiator : 619.7K DPS (45.51M dmg), Demo Ranger : …" on the clipboard
    - clicking a player opened details with the saved-fight badge ("Aultross · Sat 24 Oct 05:40") and five skills
  - Only `demo-history.db` was written; no warning in the log; exit code 0. Screenshots are not committed.

**Not checked:**

- Real players and real skill icons in the game.
- A saved fight with tens of thousands of hits (the log is virtualized and the fight loads off the UI thread, but load time is unmeasured).
- Hover tooltips on the chart.

**Audit (pr-auditor, deep pass on 7d4a41a): request changes**, now addressed:

- Major: the "Skill icons" setting did not stop the graph's buff-lane icons (`DamageChart` called `SkillIcons.Url` with its default `enabled = true`), so turning it off still sent requests to NC's CDN. `DamageChart` now takes `IconsEnabled` from the page, and `SkillIcons.Url` has no default.
- Minor: "Show deaths" was ignored in the details header and the saved fight's party; both follow it now.
- Minor: a slower saved-fight load for an earlier click could label the details window with the wrong fight; the load goes into a local and applies only if the selection is still current.
- Minor: the history window applied results in arrival order; a request counter keeps only the latest search and fight load, going back to the list drops a fight still loading, and closing the window cancels.
- Minor: Log filters and expanded summon groups carried over to another player; the tab body is keyed by the selection, so they reset.
- Nits: a page past the end (retention shrank the history) is replaced by the last page; the chart's empty text no longer says "yet" for saved fights. Left as is: the live details loop keeps polling while the window is minimized (500 ms, acceptable for now); `NewestFirst` relies on the engine's order.

Checks after the fixes: `scripts/Build.ps1` 235/235. `Smoke-Meter.ps1 --demo` with `showDeaths` off: the Graph showed two lane icons, and after switching "Skill icons" off in Settings it showed none and the page had no image at all; a Log filter ("Combustion", 3 of 16 hits) reset to all skills (19 of 19) when the details window was re-targeted to another player from the overlay; exit code 0. Deaths were not exercised (the demo party did not die), so hiding them is checked by reading the code only.

**Next:** CI; merge after the engine PR (#5), then CI on the merged result.

## 2026-10-24 — Engine: player details, history, demo mode

**What:** The engine side of the per-player details and history browser, plus a demo mode for UI work. The pages are being built separately; the app gains only the `--demo` switch and its one-line mapping in `App.xaml.cs`.

- `ICombatEngine.GetPlayerDetails(playerId)` returns a `PlayerDetails` (player row, fight duration, hit rates, skills by damage with summon groups, buffs by uptime, damage graph with buff lanes, the newest 1,000 hits), composed from `CombatSessionManager`'s per-player getters. A read that straddles a change of fight is taken again, and after three such reads gives up with null (see the audit below).
- `GetHistory(HistoryQuery)` maps `GetHistoryPage`. `HistoryQuery.Validate` enforces page 1 to 1,000,000, page size 1 to 200, a trimmed name of at most 100 characters and `From` ≤ `To`, and makes times local. `GetHistoryFight(id)` returns every player's row and details, built by the same `UpstreamMapping` code as the live view. Saved fights are read from the history store directly, outside the session lock. Damaged saved data is cleaned or left out, never thrown (see the audit below).
- Upstream's store cannot delete a single fight, so there is no `DeleteHistoryFight`.
- Every skill and buff icon is upstream's string, unchanged; ARCHITECTURE.md ("Icons") says what they look like. Class icons are not exposed (see the audit below).
- `CaptureMode.Demo` and `--demo`, which excludes `--no-capture`. `DemoCaptureDevice` stands in for upstream's capture device and runs `DemoCombatFeed` on its own thread. Four "Demo …" players of different classes (the Gladiator is the user) and an Elementalist's fire spirit fight Aultross, Fediv Wraith and Transcendent Bakarma in turn. Their skill, buff, class and mob codes are real upstream game data. The feed calls only upstream's public API, as its processors do. Each fight ends in a kill, then `CompleteAndPersistActiveSessions` and a flush, and so becomes one history entry; the next starts 10 s later. Demo history goes to `demo-history.db`.
- Tests: 24 to 58 in the first commit. New: `PlayerDetailsTests`, `HistoryTests`, `HistoryQueryTests`, `DemoModeTests` and a shared `SyntheticCombat` helper; `EngineOptionsTests` and `LaunchOptionsTests` were extended. With main merged in the suite counted 192; the audit fixes add 14 (206), with a shared `CapturingLoggerProvider`.
- Docs: ARCHITECTURE (data flow, contract, player details, history, icons, demo mode, storage, tests, update table), DEVELOPMENT and README (`--demo`), HANDOFF (state, two acceptance items, icon decision).

**Why:** Harry asked for per-player details and a history browser. Upstream already computes both, so the engine maps them into Scarlet records and keeps upstream types out of the app. Demo mode lets the UI be built and tested without the game or Npcap. It drives upstream's own fight tracking rather than fake records, so the UI sees what capture would produce.

Design decisions:

- Upstream never ends a fight on a kill, only on a 45-second idle timeout, a respawn or `CompleteAndPersistActiveSessions`. The demo uses the last: it is the public call that persists, and the one the engine's shutdown already uses.
- Demo history has its own database so synthetic fights never enter the player's history.
- Saved fights bypass `GetHistorySession`, which deserialises every hit under the session lock. That would stall the capture thread while someone browses history mid-fight.
- Buffs are ordered by uptime; upstream orders them by application count.
- The combat log's `Second` counts from the fight's start. The graph keeps upstream's origin, the player's first hit. Both are documented on the records.
- `CombatLogEntry` gains `SkillId`, which the spec did not have, so the log can be filtered by skill: names repeat across skills.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug) and `scripts/Build.ps1 -Configuration Release`: restore with audit as errors, build with 0 errors and no diagnostics from Scarlet's projects (upstream's own 6 warnings unchanged), 58/58 tests passed in each configuration.
- The Release test executable, three more full runs: 58/58 each.
- `%TEMP%` held 20 `scarlet-tests-*` directories before and after every run: the auditor's leftovers of 2026-10-23, still there. These runs added none.
- Six mutations to the new code; each failed at least one test:
  - buffs ordered by applications;
  - the log cap removed;
  - back and front rates swapped;
  - unspecified database times left unmarked;
  - summon children shown without a group (caught only after a single-skill summon case was added);
  - the demo's `CompleteAndPersistActiveSessions` removed (all three seeds).
- Demo fight length: 60 simulated fights (seed 7) lasted 69.0 to 84.3 s with HP for 70 to 82 s of expected damage. The earlier 65 to 82 s window gave a 63 s fight in the first smoke run, so it was narrowed.
- First smoke run: the Debug `ScarletMeter.exe --demo --data-dir <scratch>`, closed after 100 s with `CloseMainWindow`.
  - Fight 1 against Aultross (135,477,000 HP) ended after 63 s and was saved with exactly that damage.
  - Fight 2 (Fediv Wraith) started 10 s later.
  - Exit code 0. Disposal saved the interrupted fight 2.
  - The data directory held `demo-history.db`, `logs` and `webview2`, and no `combat-history.db`.
  - The log showed the known WebView2 off-thread disposal fallback.
- Second smoke run, on the final fight-length window and otherwise the same:
  - Fight 1 against Aultross (147,442,000 HP) ended after 73 s and was saved.
  - Fight 2 started 10 s later.
  - Exit code 0. The interrupted fight was saved.
  - The data directory held only `demo-history.db`, `logs` and `webview2`.
- The smoke runs' data directories and a throwaway game-data probe lived in a session scratch folder, outside the repository, and were deleted afterwards.

**Not checked:**

- The window was not looked at. `MeterPage` shows only the snapshot, and the new pages do not exist yet.
- Live capture and the game. Demo mode does not exercise upstream's packet decoders.
- Whether real summons, buff descriptions and icons arrive as the demo models them. The demo follows upstream's processors as read in v1.11.4.334 (HANDOFF.md, acceptance).
- `GetHistory` under concurrent capture load (the database query under the session lock).

**CI:** PR 5 at 9cf4835 passed: push run 40002328567 in 2m22s and pull_request run 40002336279 in 2m16s, including the published-layout check.

**Upstream quirks, new candidates to offer upstream:**

- `GetHistoryPage` and `GetHistorySession` query, decompress and deserialise SQLite under the session lock that every hit takes.
- No fight ends on a boss kill, so a fight reaches history only 45 s after its last hit, and only once another target is hit (or the meter closes).
- `BuffStatisticsCalculator` orders buffs by application count.
- The buff decoder always leaves `Description` empty.
- `GetHistoryPage` computes its skip as `(PageNumber - 1) * PageSize` in an int, so a huge page wraps round to the first.

**Audit (deep pass, PR 5, head 903a523): request changes.** The branch was first brought up to date with main: the overlay and settings PR (#2) and the world-events PR (#3) were merged in as df7af76, with the docs conflicts resolved and 192/192 tests. The findings, and what changed:

- **Major: saved rows were not sanitised.** The auditor inserted synthetic rows and broke both history calls:
  - a hit `{}` (no time) made `GraphDataCalculator` throw `OverflowException` in `GetHistoryFight`;
  - two hits 50 days apart made a 4,233,661-point, 474 MB graph;
  - a saved uptime of 1e300 seconds made `TimeSpan` overflow;
  - text in `SessionEnd` made `GetHistory` throw, so the whole list was unusable.

  Changes:
  - `SavedFightSanitizer` cleans every saved snapshot before it is mapped. It checks the start and end are usable (set, in order, at most 12 h apart; otherwise rows only, no details). It drops null entries, hits with no skill or negative damage, hits more than a minute outside the fight, and buff events over an hour or outside the window. It re-sorts hits newest first.
  - Buff uptime is clamped to the fight; a non-finite figure counts as 0.
  - The fight total saturates instead of overflowing.
  - A player whose details still fail is left out of `Details`, logged.
  - `GetHistoryFight` logs and returns null for an unreadable row.
  - When upstream's list query fails, `GetHistory` reads the list again with `SavedHistoryReader`: every list column as text, damaged rows skipped and counted. The page comes back with the new `HistoryPage.IsComplete` false and without the running fight.
  - A probe showed upstream's query fails only on the pages that hold the damaged row, while counting it on the others. So the engine keeps reading that way until a read finds no damaged row, and every page agrees. If even that read fails, the page is empty and incomplete.
- **Huge pages (3).** `Page` is capped at `HistoryQuery.MaxPage` (1,000,000), so upstream's int skip cannot wrap round to the first page.
- **Retry overstated (4).** A read that still straddles a change of fight after three attempts now returns null instead of mixed parts. The docs (ICombatEngine, ARCHITECTURE) now say the check is by identity, so a switch away and back between the two checks goes unnoticed. An internal `BetweenDetailsReads` seam lets two tests force a straddle once (the second read wins) and every time (null).
- **Clock jumps (5).** One step an hour late took 10.4 s: 3,601 pings and 42 fights, longer than the 10 s stop timeout.
  - When the earliest event is more than `MaxClockJump` (5 s) behind or ahead of the step's time, `DemoCombatFeed` moves the whole schedule so the earliest falls due now. A jump back no longer freezes the demo either.
  - `Step` takes the stop token and checks it between events.
- **Class icons (6).** `PlayerRow.ClassIcon` is removed, with its test and docs. It exposed a path into upstream's WPF project, which Scarlet does not ship; the app shows its own copies by `ClassId`.
- **Nits:**
  - (7) The docs now say that after the feed gives up (ten failures in a row), `Start` does nothing until `Stop`.
  - (8) Comments now cite `DemoModeTests.RosterResolvesInUpstreamsGameDataWithSyntheticNames`, not a nonexistent `DemoRosterTests`.
  - (9) A new test pins the store-first lookup: a saved fight loads while a held save keeps upstream's session lock taken, which a read through the session manager waits for. The docs now call it a performance choice.

Audit checks (SDK 10.0.402):

- New tests in `HistoryTests` damage rows through upstream's own `CombatHistoryDbContext` (JSON fields, `SessionStart` and `SessionEnd`, text in a date column, a dropped table). `PlayerDetailsTests`, `HistoryQueryTests` and `DemoModeTests` gained tests for the straddle, page bound, clock jump and stop request. 206 tests pass in Debug on df7af76 plus these changes.
- Twelve mutations to the fixes, each failing at least one test (a mutant that only failed to compile was reworded until it built):
  - sanitizer: hit filter off; buff-event filter off; window check off;
  - mapping: uptime clamp off;
  - history: the list fallback off; damaged mode off; the fight read throwing; the session manager asked first;
  - details: mixed parts returned on the last attempt;
  - demo: realignment off; the stop request ignored;
  - query: the page bound off.
- `scripts/Build.ps1` Debug and `-Configuration Release` on 0662631: 206/206 each, no diagnostics from Scarlet's projects.
- main at 45ecab3 (the world-event notices PR #6) was then merged in, with README and WORKLOG conflicts kept from both sides. On the merged result, `scripts/Build.ps1` Debug and `-Configuration Release` passed 222/222 each, with no diagnostics from Scarlet's projects.
- `%TEMP%` held the same 20 old `scarlet-tests-*` directories before and after every run.

**Second audit (deep pass, PR 5, head a7777cb): approve with nits.** What changed:

- **Damaged mode could outlast the damage (minor).** The row-by-row reader was stricter than upstream's own reads: text in a number column reads as 0 upstream, a numeric date as a Julian date, and any `State` number is accepted. Damaged mode also ended only when a read skipped nothing. The auditor's probe:
  - damage row 902 (`SessionEnd` set to 'not a date') and row 903 (`PlayerCount` set to 'x');
  - repair 902;
  - upstream's `GetSessionList` now read all three rows, yet `GetHistory` stayed incomplete with a total of 2 until restart.

  Changes:
  - `SavedHistoryReader` now reads each row with the same SQLite provider calls EF Core makes for upstream's store, so it skips exactly what upstream cannot read, and reads the rest the same way.
  - While damaged, each `GetHistory` first asks upstream's store to read every row (`GetSessionList`, outside the session lock). Once that succeeds, damaged mode ends and the same call already goes through upstream's query.
  - The probe is now the test `DamagedModeEndsWhenUpstreamCanReadEveryRowAgain`. It also checks that the rows both reads can read come out identical.
  - ARCHITECTURE and the reader's comment name the two remaining differences: dates compared and ordered as read rather than as stored text, which only rows upstream never writes can show.
- **Name filter (nit).** The reader now matches upstream's SQL: the stored name lowered ASCII-only by SQLite, the search lowered in the current culture, compared ordinally. `AnUnreadableRowLeavesTheRestOfTheHistoryReadable` saves a Fediv Wraith fight among the Aultross ones and filters on both names.
- **Untested window guards (nit).** `AFightSavedWithAnUnusableStartOrEndShowsItsPlayersOnly` adds:
  - a reversed start and end;
  - an hour-long fight near `DateTime.MinValue`, which only the margin rejects;
  - the same near `MaxValue`.

  The per-player catch is now commented as defence in depth that no test drives.
- **Log wording (nit).** The message for a failing upstream list query no longer assumes a damaged row. It says the history is read row by row until upstream can read it again.

Second-audit checks:

- 223 tests pass in Debug on a7777cb plus these changes.
- Six mutations, each failing a test:
  - the old exit rule (no upstream probe);
  - a strict number read;
  - the name filter off (reworded once to compile);
  - the reversed check off;
  - the MinValue margin off;
  - the MaxValue margin off.
- main at 40206be was then merged in: the upstream move to v1.11.5.341, PR #7. Only WORKLOG conflicted, and both sides were kept. `git submodule update` put `upstream/` at aa3ffc6. On the merged result, `scripts/Build.ps1` Debug and `-Configuration Release` passed 224/224 each, with no diagnostics from Scarlet's projects. The demo roster still resolves in the new release's game data.
- `--demo` smoke through `scripts/Smoke-Meter.ps1` with a throwaway data directory (deleted afterwards):
  - The overlay showed Aultross with its HP falling, and the four "Demo …" rows, "Demo Gladiator" as the user. Ping read 43 ms. A screenshot was taken and not committed.
  - Fight 1 ended after 77 s and was saved with exactly its 155,291,000 HP as damage.
  - Fediv Wraith came up 10 s later.
  - Closing through the window gave exit code 0 and saved the interrupted fight. The data directory held `demo-history.db` and no `combat-history.db`. The log had no warnings or errors.
- `%TEMP%` again held only the 20 old `scarlet-tests-*` directories.

**Next:** CI on the PR and Harry's go-ahead; the UI pages on top of these contracts (treat a missing `Details` entry and `IsComplete` false); the icon-serving decision for skill icons (HANDOFF.md); the new in-game acceptance items.

## 2026-10-24 — Upstream release check and installer

**What:**

- Build stamp: `src/ScarletMeter.App/Updates/BuildStamp.targets`, imported by the app's project only, stamps `AssemblyMetadata("ScarletUpstreamRelease")` (the submodule's exact tag, else its short commit, else `unknown`) and `AssemblyMetadata("ScarletCommit")`. `BuildInfo` reads them.
- `UpstreamReleaseChecker` (`ScarletMeter.App.Updates`): one anonymous GET to GitHub's latest-release API for upstream. It validates the answer, compares versions numerically, caches the result, requests at most once per six hours and never throws. `AddUpstreamReleaseChecker` registers it; it is deliberately not registered in `App.xaml.cs`, because the UI rewrite in progress wires it into the overlay and settings.
- `scripts/Install-Meter.ps1`: Release build with tests, self-contained single-file publish into `<install>.staging`, layout check, then a reversible folder swap that keeps `<install>.previous`. `-Shortcut` adds a Start menu shortcut.
- CI fetches the pinned upstream release tag, which its shallow submodule lacks, and fails if the pin is not a release. It now runs `Install-Meter.ps1 -SkipTests` under pwsh in place of the bare publish and layout steps.
- Tests, 24 to 108 (84 new cases): `BuildInfoTests`, `UpstreamReleaseCheckerTests`. The test project gains Microsoft.Extensions.TimeProvider.Testing 10.10.0.
- Docs: ARCHITECTURE "Updates", DEVELOPMENT "Install and update" and CI, README, SECURITY "Network", HANDOFF.

**Why:** Harry asked for an automatic updater, if possible and reasonable, for a meter only Harry uses. A RATmeter-style self-updater (download a release, replace the exe) does not fit: Scarlet has no published binaries, and swapping one in would skip the build and its checks. What matters is knowing when upstream releases, since game-patch parser fixes arrive that way, plus one command to rebuild and reinstall.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set, Windows PowerShell 5.1):**

- `scripts/Build.ps1` Debug and Release: 0 warnings, 0 errors, 108/108 passed.
- Mutations, each caught by the new tests, then reverted:
  - no cache: 4 failures;
  - `$` instead of `\z` in the tag pattern: 2;
  - no page-path check: 1;
  - text instead of numeric comparison: 3;
  - no timeout: 2 timeout tests failed by their wait limit;
  - no read cap: 1.
- Stamp:
  - Generated assembly info carried `v1.11.4.334` and `9495922`, and both values are in the bundled exe.
  - A temporary local commit, undone afterwards, was restamped by an incremental build, and the reset stamped the old value back.
  - With the pinned tag deleted from this worktree's submodule clone, the stamp fell back to `57974f8`. The tag was then re-created at the same commit.
  - With git removed from PATH: `unknown` for both, build exit 0.
- CI checkout simulated in the scratch directory (the same fetch, then `git submodule update --init --force --depth=1`):
  - The submodule held only `v1.11.5.341`, the tag at upstream master's tip. `describe --exact-match` failed, so CI would have stamped the commit.
  - The CI step's script, run locally, fetched `v1.11.4.334` at depth 1; after that `describe` matched and the env file held the tag.
- One manual live check, outside the tests, through `AddUpstreamReleaseChecker` and the production handler: `NewerAvailable`, v1.11.5.341, published 2026-10-23T17:19:58Z, page on upstream's repository. The second call was served from the cache.
- `Install-Meter.ps1` against a temporary install directory in the scratch directory, never the default location:
  - Fresh install with `-Shortcut` and tests: layout passed, the shortcut pointed at the installed exe.
  - The installed exe started with `--no-capture --data-dir <temp>` and showed its window.
  - While it ran, the script refused before building. It then closed with exit 0.
  - A second install moved the first to `.previous`.
  - With a file held open in the install, the rename failed and was undone: install and `.previous` unchanged, the new build left in staging, no `.retired`.
  - A rerun replaced `.previous` and cleared staging.
  - With a wrapper failing only `dotnet publish`, staging was removed and the install left untouched.
  - Refusals before any build: relative path, drive root, the data directory, a folder inside it, its parent, a folder in the checkout, a foreign folder, a file, a foreign `.previous`. The foreign files were kept.
  - `%LocalAppData%\ScarletMeter` and `%LocalAppData%\Programs\ScarletMeter` do not exist afterwards.
  - Cleanup: the shortcut, the temporary install and the scratch projects were removed.

**Not checked:**

- pwsh: it is not installed here. CI runs `Install-Meter.ps1` and the tag step under pwsh.
- CI itself: pending the PR.
- An elevated meter: refused by design as "location unreadable", but not run.
- The UI wiring: notice, settings switch and registration are left to the UI rewrite.
- The default install location: never installed there.

**Audit (deep pass, PR 4): request changes.** Merged origin/main first (#2 overlay and settings, #3 world events), keeping both sides in README, ARCHITECTURE, WORKLOG and the app project. CI keeps its steps plus the tag step, and the layout check still runs on the published output inside `Install-Meter.ps1`. Then:

- Major: `Install-Meter.ps1` accepted any folder holding a `ScarletMeter.exe`. The auditor's Downloads-like folder (a dummy exe, `important.txt` and a junction) was renamed to `.previous` by one install and deleted by the next. Fixed:
  - A checked publish now gets an install marker, `.scarlet-install` with fixed text, which stays in the install.
  - The install, `.previous` and `.retired` are renamed or deleted only when they carry that marker or are empty. Staging is accepted with the staging marker or the install marker.
  - Any folder that is or holds a link or junction is refused; the tree is walked without following links.
  - New `-CheckOnly` runs every check and stops before building.
  - New `scripts/Test-InstallMeter.ps1` runs the refusals against throwaway folders, and CI runs it. It checks the auditor's folder stays byte for byte as it was.
  - The "a mistyped path cannot move other files" claims are corrected.
- The overlap guards compared strings. Now:
  - Only local drive paths are accepted (no UNC, admin shares, `\\?\` or `\\.\`), and no names Windows would change (trailing dot or space) or cannot create.
  - The parent of the install, the data directory and the checkout are compared by their final paths from `GetFinalPathNameByHandle`, which resolves links, junctions, 8.3 names and substituted drives. A path resolving to a network share is refused.
  - The process check resolves the running exe's folder the same way.
- Rollback: each undo rename has its own try/catch; failures are collected, and the error reports every folder's state. A leftover `.retired` is deleted only while an install exists, otherwise refused up front, since it may be the only copy.
- Checker: Network and Timeout results stand for `RetryInterval` (10 minutes); answered failures (rate limit, 404, invalid) stay six hours. Disposing mid-check, or calling after disposal, gives `Unknown(Cancelled)` with no warning.
- Nits:
  - Tests for an answer of exactly 1 MiB (read) and 1 MiB + 1 (rejected), with and without a length.
  - A gzip answer inflated by the production handler, plus a gzip answer over the cap once inflated, both through a one-shot server on 127.0.0.1.
  - Dispose mid-check.
  - CI retries the tag fetch like `ls-remote` and no longer sleeps after the last attempt.
  - `Get-GitLine` requires the `.git` entry, as BuildStamp.targets does.

Checks after the fixes (SDK 10.0.402, Windows PowerShell 5.1):

- `scripts/Build.ps1` Debug and Release: Scarlet's projects had 0 warnings (the six are upstream's), and 252/252 tests passed.
- `Test-InstallMeter.ps1`: every case passed. The short-name case was skipped because this volume has no 8.3 names. No temp folder was left.
- Five installer mutations, each failing the named cases:
  - any folder counted as owned;
  - no final-path resolution;
  - no link walk;
  - UNC allowed (the admin share was still refused, by the final-path layer);
  - no `.retired` guard.
- Five checker mutations, each caught:
  - `>=` in the read cap;
  - `>=` in the length cap;
  - no retry window;
  - the retry for every failure;
  - dispose reported as Unexpected.
  - (A first form of the dispose mutation, `when (false)`, did not compile under warnings-as-errors and was replaced.)
- Install-Meter end to end, in a uniquely named scratch folder:
  - Two installs left the install and `.previous`, both marked, with no staging or `.retired` left.
  - With a file held open, the swap rolled back and the error listed the folders: install and `.previous` intact, the checked build left in staging (marked).
  - The running installed meter was refused under `-CheckOnly`; it then closed with exit 0.
  - A rerun recovered.
  - The folder was removed afterwards. `%LocalAppData%\ScarletMeter` and `%LocalAppData%\Programs\ScarletMeter` do not exist.

Not checked:

- A failing undo (no way to inject one).
- pwsh locally; CI runs both scripts under pwsh.

A local dry-run of CI's tag step made this worktree's own submodule clone shallow; `git fetch --unshallow --tags` restored it (334 commits, 38 tags, still v1.11.4.334), and the CI comment now warns about it.
**Second merge of main (45ecab3, #6 event notices):** conflicts in README, HANDOFF and WORKLOG were resolved keeping both sides. Main's `App.xaml.cs` registers `TimeProvider.System` itself; `AddUpstreamReleaseChecker` only try-adds it, so the two agree. Checks: `Build.ps1` Debug and Release 268/268, `Test-InstallMeter.ps1` passed, and one `-SkipTests` install of the merged tree into a scratch folder passed the layout check (now including `alerts.css`) and carried the marker; the folder was removed.

**Second audit (deep pass, head fb24e0a): approve with nits.** Merged origin/main first (40206be, #7: upstream v1.11.5.341, aa3ffc6). HANDOFF and WORKLOG were resolved by keeping both sides. `git add -A` had restaged the old submodule commit over the merge, so main's gitlink was put back with `update-index` before committing. HANDOFF's pin mentions now say v1.11.5.341; ARCHITECTURE and SECURITY already did. Fixes:

- Deletes could strip the marker. `[IO.Directory]::Delete(x, true)` goes in directory order, where `.scarlet-install` sorts first, so a delete stopped by an open file left an unmarked folder the next install refused as foreign. A new `Remove-OwnedFolder` deletes every entry but the markers, then the markers, then the folder. It is used for the leftover `.retired` before the swap (whose failure now reports the folder state and swaps nothing), for `.retired` after the swap, and for stale and failed staging.
- The folder rules moved to the dot-sourced library `scripts/InstallFolders.ps1`: markers, final paths, ownership and link checks, the delete, the swap. That lets `Test-InstallMeter.ps1` run the delete and swap paths on fake installs without building. New cases:
  - a `.retired` with a file held open in a subfolder: the swap is blocked with nothing moved, the marker survives, `-CheckOnly` still accepts the folder, and once the file is released a rerun completes the swap;
  - a failed rename is undone;
  - a foreign `.staging`, and an install with a foreign `.retired`: both refused, all foreign folders byte for byte unchanged.
- The loopback gzip tests used the production `SocketsHttpHandler`, which honours `HTTP_PROXY` and the system proxy. They now use its settings with `UseProxy = false`, set in the tests, so the public API is unchanged. ARCHITECTURE's "nothing leaves the machine" now holds.
- An existing install folder that is not a link is resolved through `GetFinalPathNameByHandle` too, not only its parent, so an 8.3 name for it matches the running-meter check and gives the siblings real names. Docs say so.
- The ownership checks and the `.retired` guard run again just before the swap, like the running-meter check.
- DEVELOPMENT notes that the trailing dot or space check is defence in depth behind `GetFullPath`, and that subst'd drives are resolved but untested.

Checks:

- `Build.ps1` Debug and Release, with upstream at v1.11.5.341: Scarlet's projects had 0 warnings, and 269/269 tests passed.
- `Test-InstallMeter.ps1` under Windows PowerShell 5.1: all 36 cases passed (the short-name case is skipped on this volume), no temp leftovers.
- Library mutations, each caught:
  - a plain recursive delete: the marker was lost and the next install refused the folder;
  - no staging ownership check;
  - no `.retired` ownership check;
  - no undo.
- With `HTTP_PROXY` and `HTTPS_PROXY` at `http://127.0.0.1:9`, the checker tests passed 71/71. With the proxy line removed they failed 2 (both loopback tests).
- Two `-SkipTests` installs of the merged tree into a uniquely named scratch folder passed the layout check and left the install and `.previous`, both marked. The folder was removed afterwards.

Not checked:

- A subst'd drive.
- pwsh locally; CI runs both scripts under it.
- The checks repeated just before the swap run after the build, so no test reaches them; code reading only.
- The install folder's own 8.3 resolution: under Windows PowerShell 5.1, .NET Framework's `GetFullPath` already expands short names, so removing it fails nothing there; only pwsh needs it, and no case covers it.

**Third audit (deep pass on the refactor, head b5ea0b2): approve with nits.** The merge 2c3e237 brought exactly main's change and kept the submodule at aa3ffc6; the library preserves every refusal and its order, `-CheckOnly` still touches nothing, dot-sourcing uses `$PSScriptRoot`, and `Remove-OwnedFolder` deletes links inside an owned folder without following them (probed with junctions). Applied: the undo case now fails, rather than printing "ok" and crashing, when a rename is not undone (`Test-Unchanged` treats a missing folder as changed); the two untested paths above are listed.

**Next:** CI on the PR, Harry's go-ahead and a squash merge. The overlay notice and settings switch for the checker follow in their own PR (#10).

## 2026-10-24 — Upstream v1.11.5.341

**What:** `upstream/` moves from release v1.11.4.334 (57974f8) to release v1.11.5.341 (aa3ffc6), published 2026-10-23, with `scripts/Update-Upstream.ps1`. Upstream's changes that reach Scarlet:

- Boss data: "Fixed incorrect reading of some bosses max hp". `MobPacketProcessor` replaces its byte-scanning heuristic with a structured parse: HP and max HP are 64-bit varints (the new `PacketReader.ReadVarInt64`), still handed on as `long`, which the engine maps whole (`TargetInfo.HpCurrent`/`HpTotal`, a new test above the `int` range). Taken as is.
- The same rewrite changes how a summon's owner is read (a `u32` from the structured parse instead of a scanned `ushort`, feeding `EntityTracker.RegisterSummon`), no longer requires current HP to be at least max HP before a mob is created, and stops creating target entries for player summons. A layout it does not expect now throws `PacketParseException`, which upstream's dispatcher logs at Error level and drops that packet only.
- "My Style" colours for the player's own name and bar: eight new `IAppSettingsService` members used only by upstream's UI. The interface change stopped the engine compiling, as designed; `UpstreamSettingsBridge` implements them as inert in-memory values with upstream's defaults.
- Not used here: upstream's UI changes, and the `appsettings.user.json` it removed from its repository.

**Why:** a release with a parsing fix; Scarlet takes upstream releases only, never `dev` (AGENTS.md).

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 174/174 before the audit, 175/175 after (`BossHpBeyondTheIntRangeIsMappedWhole`). Release shows five compiler warnings, all in upstream's own code (`ServerMap`, `FilePacketCaptureDevice`, `DotDamagePacketProcessor`, `PartyProcessor`), none in Scarlet's.
- A self-contained single-file publish (the CI command) and `Test-PublishLayout.ps1`: complete.
- The published `ScarletMeter.exe` with `--no-capture` and a throwaway data directory (`Smoke-Meter.ps1`): the overlay opened ("Capture is off"), upstream registered its 15 opcode processor mappings, no warnings in the log, exit code 0.

**Not checked:** the boss HP fix and the rewritten summon-owner parsing, which need the game (HANDOFF.md, in-game acceptance). After the first session, look for `PacketParseException` errors in `logs/scarlet-*.log`: each unexpected mob packet logs one.

**Audit (pr-auditor, deep pass): approve with nits.** Pin, release and an unmodified submodule confirmed; the HP path has no narrowing; no new registration, game data, package or capture-to-disk change reaches Scarlet. Applied: the summon parsing change above and an in-game item for it, a test for boss HP above the `int` range, and the example tag in `Update-Upstream.ps1`. Left as a follow-up: upstream now starts WebView2 with background and occlusion throttling turned off (`ConfigureWebView2Arguments` in its UI); whether Scarlet's overlay needs the same is untested.

**Next:** CI and merge.

## 2026-10-24 — World event notices in the overlay

**What:**

- **Notices:** `ScarletMeter.WorldEvents` is wired into the overlay. A slim strip at the bottom of the meter (`AlertStrip`) shows:
  - "Soon" before an enabled event starts, with a countdown and a bar filling over the lead time.
  - "Portal open" or "Entry open" while it can be entered.
  - At most two notices; optionally, the next event as one dim line when nothing is due.
- **`AlertMonitor`** sleeps until the plan's next change, at most a minute, and signals each event occurrence once, when its Soon notice appears. Failures are logged and retried a minute later, never thrown into the timer thread.
- **`MeterWindows`**, if the player opts in, brings a minimized meter back once per occurrence without activating it (`SW_SHOWNOACTIVATE`), so the game keeps focus.
- **Settings:** `AlertSettings` covers on/off, lead time 1–60 minutes (default 5), events (default: world events and bosses, not resets), server UTC offset (+9), the next-event line, and bringing the meter back (off). The new Alerts tab lists each event with its schedule and its state in local time ("next Sun 22:00", "entry open now", "running, entry closed"), and carries the accuracy warning.
- **Settings file:** `SettingsStore` accepts a UTF-8 byte-order mark. Found in the smoke run: a `settings.json` written by Windows PowerShell (or saved as "UTF-8 with BOM") was treated as unreadable and set aside, so a hand edit lost every setting.
- **Rules:** AGENTS.md and ARCHITECTURE now say the app references the engine and the dependency-free world-events module, never upstream.

**Why:** Harry asked to be told about five minutes before a Spacetime Rift, Shugo Festival or another world event, "sleek and subtle in the overlay, doesn't have to be a popup". The schedule engine came first in its own PR.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 174/174 tests, 16 of them new:
  - `AlertTests`: Soon at the lead time, Open until entry closes, the lead time from settings; nothing when alerts are off or nothing is chosen; the next-event line (and none while a notice is due), the server offset; normalizing alert settings and comparing them by value; wording for countdowns, schedules and each phase of the settings list, in two time zones.
  - `AlertTests`, the monitor on a fake clock: one signal per occurrence (none for Open, none again after switching the event off and on or changing the lead time, one for the next occurrence), the wait capped at a minute and otherwise the next change plus 20 ms; a throwing subscriber and a failed computation stay inside the monitor, which empties the view and retries a minute later; starting twice subscribes once, starting after disposal does nothing.
  - `SettingsTests`: equality varies every `AlertSettings` member; a file from before alerts loads their defaults with no repair notice and restoring off; `"events": null` takes the defaults; a file with a byte-order mark loads.
- `EqualityCoversEveryProperty` failed until `MeterSettings.Equals` and its test included the new section, as intended.
- Smoke run after the audit fixes (`Smoke-Meter.ps1`, Debug, a seeded file written with a byte-order mark: lead 60 minutes, offset 9.0166, Nahma, rift, Invasion and Shugo):
  - The file loaded (before the store fix it was set aside as unreadable and defaults were used).
  - The strip showed Shugo and Invasion counting down; the only live region was the hidden summary "Shugo Festival starts at 05:59; Dimensional Invasion starts at 06:29" (1 by 1 px, clipped, not drawn).
  - The Alerts tab read "next 05:59", "next Sun 22:59" for Nahma and "next Wed 18:00" for the weekly reset; the offset field showed "9.02". No warnings in the log; exit code 0.
- First smoke run, before the audit, with a seeded lead time of 60 minutes and Shugo only:
  - The strip read "Shugo Festival · in 27:08" and ticked down second by second.
  - The Alerts tab rendered all events with schedules and next local starts (screenshots, not committed).
  - With the meter minimized by the real shortcut, switching on Dimensional Invasion (its entry was open at that moment) brought the overlay back to Normal on its own; the strip then read "Dimensional Invasion · Entry open · 0:03 left" above Shugo.
  - The saved events were `invasion,shugo`; no warning in the log; exit code 0.

**Not checked:**

- Real event times in the game (HANDOFF.md).
- Whether the restored meter really leaves the game's focus alone in a fullscreen or borderless game.
- Readability over the game.
- A single-file publish of this branch, which CI runs with the layout check.

**Not done:** per-event time overrides in settings (HANDOFF.md).

**Audit (pr-auditor, standard pass with a closer look at threading and windows): approve with nits.** Addressed:

- Major: by default a minimized meter came back about four times an hour (Soon and Open of Shugo and Invasion each restored it), and editing the event list restored it again. Decision: restore once per event occurrence, on its Soon notice only, kept until the occurrence starts; and default "Bring the meter back" to off, so hiding the meter keeps it hidden unless the player opts in. Existing files have no `alerts` key, so nobody is opted in by upgrading.
- Minor: only `ArgumentException` was caught on the timer thread; now every failure is caught and logged, and handlers are invoked inside the guard. `Start` is idempotent and does nothing after `Dispose`.
- Minor: the Alerts tab's countdowns never ticked and "on now" showed after entry closed; it now shows local times and phase labels only, and re-renders when the monitor wakes.
- Tests for the gaps the auditor's mutation runs left (listed under Checks). Nits: the `RefreshAt` doc, the live region on the per-second countdown, the raw offset double in the field, and `alerts.css` in `Test-PublishLayout.ps1`.
- Downgrade: no version bump, as the section is additive. An older build ignores `alerts` and drops it on its next save.

A second pass over the fixes (66dc773, including the byte-order-mark change, which the first pass had not suggested) approved with nits, now applied: the hidden status region is always rendered, so the first notice is announced; a settings-list case where the local and UTC dates differ; the `Changed` doc says it fires on every refresh.

**Next:** CI and merge; then the details and history UI on the engine PR.

## 2026-10-24 — World event schedules

**What:** New module `src/ScarletMeter.WorldEvents`, a net10.0 class library with no project, upstream or package references, added to the solution and referenced by the tests. It ports the schedule engine of the harrynguon/AIon2-Dps-Meter fork's World Companion (JavaScript, at 4feb72b) and adds the alert planner the overlay will use. No UI and no app wiring.

- `EventRule`, `EventKind`, `ScheduleBasis`, `EventIds`: immutable rules whose hours and days are copied, de-duplicated and sorted, and compare by value.
- `EventCatalog`: `Legacy` (the fork's `defaults`), `Global` (its Global preset, Scarlet's `Default`), `Ids`, `DefaultServerOffset` (UTC+9) and `TryApplyOverrides` (the fork's `validateState` merge).
- `ScheduleValidation`: `ValidateRules` and `ValidateServerOffset` return lists of `ScheduleError` (a catalog id or null, a constant message).
- `EventSchedule`: `Occurrence` and `Timeline`, in UTC throughout, with the current instant and the server offset as parameters.
- `AlertPlanner.Plan`: Soon within the lead time (default 5, valid 1–60 minutes), Open while a portal or entry window is open, ordered by urgency, plus `NextChange`, the next instant at which the alerts change.
- Tests: 88 new, in `tests/ScarletMeter.Tests/WorldEvents/`. `UpstreamBoundaryTests.ScarletProjectDependencies` is now internal so `WorldEventsBoundaryTests` reads the same build graph.
- Docs: ARCHITECTURE.md ("World events", the sources table, tests), SECURITY.md ("Provenance"), HANDOFF.md (wiring notes, in-game verification list), README layout.

**Why:** The player wants a subtle overlay notice about five minutes before world events such as the Spacetime Rift and the Shugo Festival. The overlay, built separately, needs a pure and tested schedule. Porting the fork's engine, rather than re-deriving it, keeps Scarlet and the fork on the same instants, so evidence gathered with either applies to both. `NextChange` lets the overlay re-plan only when something changes, not every second.

**Deliberate differences from the fork:**

- `End` and `EntryEnd` belong to the occurrence at `Start`. The fork's `end` belonged to the most recent start, which is in the past for an upcoming event; its UI never read it.
- Durations are whole minutes and the server offset is a whole number of minutes. The fork accepted any finite number; whole minutes keep the offset usable as a `DateTimeOffset` offset, and +5:30 or +5:45 still pass.
- Validation lists every problem instead of throwing at the first, and never copies input text into an error. An unknown id is reported with a null id.
- Rules normalise hours and days when constructed; the fork did so only in `validateState`.
- Not ported: the fork's file-version checks, which belong to the host's settings format, and its non-finite and string values, which the types cannot hold.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug): restore with audit as errors, build 0 warnings and 0 errors, 112 tests passed (24 existing, 88 new).
- `scripts/Build.ps1 -Configuration Release`: 0 errors, 112 tests passed. Its 6 warnings are all in `upstream/` (CS8600, CS0067 twice, CS8601, CS9113, CS0168), which `TreatWarningsAsErrors` does not reach; Scarlet's projects have none.
- Mutations: a scratch script (not committed) applied 23 single-line changes to the module, one at a time, and ran the suite. Every one failed at least one test. They covered:
  - the inclusive and exclusive boundaries (lead time, start, window end, entry end);
  - Open for resets, both halves of the Open next-change and the Soon next-change;
  - the urgency and timeline order;
  - a weekday off by one, an ignored server shift, the rift's entry default and a shortened day scan;
  - the rift activity requirement, the entry limit, and the offset range and whole-minute rule;
  - merge metadata and activity, one Global preset value, de-duplication and value equality.
  Sources were restored and rebuilt.
- Independence guard: adding a ProjectReference to the engine, and then a PackageReference (Microsoft.Extensions.Logging.Abstractions), to the module each failed `ModuleDeclaresNoDependencyInTheBuild`. With the file restored, the suite passes again.
- The fork's A2Tools reproduction table (TIMER-COMPARISON.md: Asia, UTC+9, nine cases) passes against the Global preset.
- The eight-day `NextChange` walk passes for both presets, with lead times of 1, 5 and 60 minutes.
- Planning with no alert allocated 0 bytes over 100 calls (`GC.GetAllocatedBytesForCurrentThread`).
- CI on 20029bc (PR 3): push run 40000400784 and pull_request run 40000450148 passed.

**Not checked:**

- The game: the times are not verified against Global Asia / Nezekan (HANDOFF.md, "Next: world-event times in game").
- The overlay, which is not built here.
- Instants next to `DateTimeOffset`'s limits. Correction (audit): this was not checked when the entry was first written. The test added during the audit (below) shows which limits throw.
- Regions with daylight saving: the offset is fixed, as in the fork.

**Audit (PR 3, standard pass with a deeper look at validation):** approve with nits. Applied:

- M1, merge coverage: mutating the merge to take the catalog's days or minute left every test green. `CustomScheduleValuesAreKept` now changes every editable field (basis, days added and removed, hours, minute, duration, entry minutes, and the rift's active minutes) and asserts each survives. With the test extended, both mutations fail it; they were then reverted.
- M2, bounded validation: a list of 1,000,000 entries used to return 1,000,009 errors. `ValidateRules` now returns a single "too many entries" error for more than `MaximumEntries` (18, twice the catalog), and at most `MaximumReportedErrors` (50) errors, the last saying more exist. `HostileInputGetsABoundedAnswer` covers both bounds, and removing either cap fails it. HANDOFF.md now says the host must bound the settings file before deserialising; the `overlay-settings` branch's `SettingsStore` refuses a `settings.json` over 1 MB.
- N1, range limits: the XML docs and ARCHITECTURE.md now list `ArgumentOutOfRangeException` for instants outside `DateTimeOffset`'s range. The new test `InstantsOutsideDateTimeOffsetsRangeThrow` shows when it happens:
  - a `now` within a week of `MaxValue` throws, because the next start cannot be represented;
  - both presets work at `MinValue`;
  - a custom window that opened before `MinValue` and is still running throws as well.
  This last case is slightly wider than the audit's note that `MinValue` does not throw.
- N2: `Timeline`'s id tiebreak is documented as ordinal. The fork uses `localeCompare`, which orders the catalog's lowercase ASCII ids the same way.
- N3: HANDOFF.md now says an overlay waiting for `NextChange` must cap the wait (for example at a minute) and re-plan after resume from sleep or a clock change.

Re-run after the fixes:

- `scripts/Build.ps1` Debug: 0 warnings, 0 errors, 114 tests passed (90 new).
- `scripts/Build.ps1 -Configuration Release`: 0 warnings, 0 errors, 114 passed. The build was incremental, so upstream was not recompiled and its 6 known warnings did not reappear.

**Next:** CI on the audit fixes, the player's go-ahead and a squash merge. The overlay change then wires `AlertPlanner` into the app and updates AGENTS.md, where the app references the engine only (HANDOFF.md, "Next: world-event notices"). Then settings persistence for the enabled ids, lead time, offset and overrides, and in-game verification.
## 2026-10-24 — Overlay, settings and hotkey

**What:** The first of the feature PRs Harry asked for (details, history, settings, fight options, an updater and world event alerts).

- **Window layer:** a shared `ScarletWindow` replaces the XAML `MainWindow`.
- **Overlay:** chromeless and transparent. It is dragged by its header (posted `WM_NCLBUTTONDOWN`), resized from its edges (`WindowChrome`), and has reset, settings, minimize and close buttons and a ping indicator.
- **Tool windows:** dark-framed and owned by the overlay; the settings window is the first.
- **Hotkey:** global, `Ctrl+Shift+D` by default as in RATmeter; it minimizes or restores the meter.
- **Window positions** are remembered.
- **`settings.json`:**
  - validation in `SettingsNormalizer`
  - atomic save with a `.bak` of the previous save
  - an unreadable file is kept aside, never deleted
  - a file from a newer build is never overwritten
  - live application, with fight options mirrored into `EngineSettings`
- **Appearance:**
  - background opacity (background only, so text stays solid)
  - background picture (imported into the data directory, served through a WebView2 virtual host)
  - RATmeter's class gradients and class icons (copied from upstream's UI at build time, never edited)
  - relative bars, deaths, RATmeter-compatible name masking, row size
- **Security:** a Content-Security-Policy on the page; the EF Core SQL log lowered to Warning.
- **`scripts/Smoke-Meter.ps1`:** drives the built meter's pages through WebView2 remote debugging, for UI checks without the game.
- **Docs:** ARCHITECTURE "Windows", "Settings", "Image hosts".

**Why:** Harry asked for RATmeter's settings screen, fight switches and window handling in Scarlet, with heavy inspiration from RATmeter. An Explore survey of upstream's UI supplied its exact behaviour: gradients, masking, hotkey default and toggle semantics, ping bands. Two deliberate differences from RATmeter:

- Opacity fades only the background. RATmeter fades the whole content.
- The background picture is actually displayed. RATmeter's setting is stored but never rendered.

The overlay is the base the alerts and other windows build on.

**Checks (SDK 10.0.402):**

- `scripts/Build.ps1`, Debug and Release: 0 warnings in Scarlet's projects (6 in upstream's, as before), 61 tests passed (37 new settings and formatting tests).
- Smoke runs through `Smoke-Meter.ps1` on the Debug build:
  - The overlay rendered "Capture is off"; clicking ⚙ opened the settings window with its four tabs.
  - A real mouse drag on the header moved the overlay by exactly (120, 60) and (−150, 40).
  - Clicking the Class colours row and moving the opacity slider to 40%:
    - the overlay's `--panel-alpha` became 0.4 live
    - `settings.json` held `classColors: false` and `backgroundOpacity: 0.4` within a second
  - A second run on the same data directory restored the overlay at the saved position and size (1370, 606, 400×260) and opacity 0.4.
  - Real `Ctrl+Shift+D` key presses minimized and then restored the overlay (UI Automation window state).
  - Closing through the page's ✕ with the settings window open:
    - exit code 0
    - no warning or error in the log
    - the UI-thread WebView2 disposal fallback logged
  - A screen capture at 50% opacity showed the desktop through the panel (not committed).
- Release self-contained single-file publish:
  - `Test-PublishLayout.ps1` passed; class icons are now checked.
  - In the published exe, `https://assets.scarlet.example/classes/11.png` and a background picture placed in `<data dir>\backgrounds` loaded (40 px wide).
  - `--panel-image` pointed at the data host.
  - An image from `https://example.com` was blocked by the policy.

**Not checked:**

- The file picker dialog itself (`BackgroundImages.Import` is unit-tested; the dialog was not driven).
- The game: the overlay over a borderless game window, the hotkey while the game has focus, and DPI scaling or multiple monitors.
- Rows with real players (the demo mode comes with the engine PR).

**Audit (deep pass, PR 2, head 3acbde5):** request changes, with two major defects reproduced in the built meter. Both are fixed:

- **Recording a shortcut did not unregister the current one.** `SuspendHotkey` reset its "registered" marker to the same empty string it then wanted, so nothing changed; pressing Ctrl+Shift+D while recording minimized the meter.
  - Fix: the decisions moved into `HotkeyBinding` (a null marker forces the first registration), with `HotkeyBindingTests` on a fake target. Recording also ends after 15 seconds as a backstop for a lost blur.
- **Closing the overlay while the picture dialog was open killed the process** (0xE0434352, `ObjectDisposedException` inside the dispatcher's handler). This is the crash the deferred close exists to prevent.
  - Fix: while the picker is open every meter window is disabled (`EnableWindow`) and refuses to close (`ScarletWindow.CloseBlocked`), and the hotkey and minimize do nothing. The settings page ignores a picker result after it has been disposed.

The minor findings and nits, all applied:

- **Settings store:** a damaged file or backup that cannot be renamed aside, or a backup that cannot be opened, now switches saving off. Without that, two saves destroyed the damaged file. A missing file with a usable backup restores it. Unreadable files keep unique names.
- **Picture names:** the stored-name regex uses `\z`, so a trailing newline is rejected.
- **CSP:** tightened to `script-src 'self'`, `object-src 'none'`, `base-uri 'self'`, without `data:`.
- **Boundary docs:** ARCHITECTURE "Upstream boundary" names the class-icon copy as the one build-time contact with upstream's UI files; the "What breaks" table has a row for it.
- **Closing guard:** `MeterWindows` refuses to open windows once the overlay is closing.
- **Placement:** tool windows use the overlay's monitor's work area.
- **Comments:** fixed for hidden names and for tool-window placement.
- **Hotkeys:** shortcuts Windows or most programs use (Alt+F4, Ctrl+C and similar) are refused.
- **Settings page:**
  - save errors re-render it at once (`SaveStatusChanged`)
  - the retention field shows the clamped value
  - in read-only mode the picture is neither imported nor deleted
- **Smoke script:** picks a free DevTools port and drives only pages on BlazorWebView's origin, `https://0.0.0.1/`. The earlier fixed port could drive another running meter.
- **Masking test:** checks upstream's exact masked value ("Ab**3**").

**Checks after the fixes:**

- `Build.ps1`, Debug and Release: 68/68 tests (7 new).
- The auditor's hotkey reproduction (`keybd_event` while the field records): the overlay stays Normal.
- A full real-mouse and real-key flow:
  - opened settings, the Overlay tab and the shortcut field with real clicks
  - pressing the current Ctrl+Shift+D was recorded, and the overlay stayed Normal
  - recording Ctrl+Alt+K saved it
  - Ctrl+Alt+K then minimized and restored the overlay
  - the old Ctrl+Shift+D did nothing
- The auditor's dialog reproduction: with the picture dialog open, clicking ✕ did not close the meter (it was still running after 3 s). After the dialog was cancelled, ✕ closed it with exit code 0 and only the usual WebView2 disposal lines in the log.
- Under the tightened CSP, a class icon loaded from the assets host and an image from another host was blocked.
- No `%TEMP%\scarlet-*` leftovers.

**Next:** CI on the fixes and a squash merge, then the engine, events and update-check PRs, then the details, history, alerts and update-notice UI on top of this.

## 2026-10-23 — Review fixes

**What:** Applied the bootstrap review: 15 findings, 8 confirmed by independent verifiers and 7 more judged real when verification was cut short by a usage limit.

- Engine: `Reset` calls `CombatSessionManager.Reset` only. `Start` tells "no eligible adapter" apart from "Npcap missing". The contract docs say adapter open failures are only logged by upstream.
- App: the WebView2 profile lives in `<data dir>\webview2`. Every startup failure ends in a dialog and exit code 1, and is logged once the host exists. Unhandled exceptions are logged at Critical. `MeterPage`'s refresh loop survives a failed tick, logging the first failure of a run.
- Scripts and CI: `scripts/Test-PublishLayout.ps1` checks a publish, and CI runs it after the publish step. `Update-Upstream.ps1` passes `--quiet` to `git fetch` and `git checkout`.
- Tests, 14 to 24: the fork's six `DeferredWindowCloseTests`; the `Live` graph resolves without starting; reset keeps summon ownership; both capture-start messages. The boundary guard covers every Scarlet project in the dependency graph, plus base types, interfaces and generic constraints, with self-checks. `TestEngineHost` waits for upstream's history cleanup; `TestDataDirectory` retries deletion, then fails.
- Docs and licence: `LICENSE` ends with Scarlet Meter's own notice naming upstream's; SECURITY.md attributes the SQLite override to Scarlet's overlay; README and DEVELOPMENT commands use absolute `<checkout>` paths; ARCHITECTURE covers storage, threading, startup failures and the tests.

**Why:** Upstream's `IPacketService.Reset`, which upstream's own UI never calls, cleared the summon-to-owner map and the TCP reassembly buffer from the UI thread while the capture thread wrote them. After a reset, a pet's hits landed on an unnamed player, and frames were dropped until the stream re-synchronised. The bootstrap smoke run's WebView2 profile had gone to `%LocalAppData%\ScarletMeter.WebView2`, so `--data-dir` did not isolate a run. A startup exception outside the two guarded calls crashed with no dialog and no log. The CI publish step proved only that publish exited 0. The remaining findings were documentation and test gaps.

**Checks (SDK 10.0.402, `SCARLET_DOTNET` and `DOTNET_ROOT` set):**

- `scripts/Build.ps1` (Debug): restore with audit as errors, build 0 warnings and 0 errors, 24 tests passed.
- With the old `Reset` restored, `ResetKeepsSummonOwnership` failed at the owner assertion (it got the pet's id). The fix was then restored.
- Temp-directory leaks: before `TestEngineHost`, whole-class and whole-suite runs left `scarlet-tests-*` directories behind (21 in 10 runs), each holding a 0-byte or 8 KB `combat-history.db` written after deletion. No test leaked when run alone. Waiting for the store's cleanup log entry removed them: 0 leaked in 10 full runs, all passing. This confirms the history store's fire-and-forget cleanup as the cause.
- Release, self-contained single-file publish to a scratch directory: exit 0. `Test-PublishLayout.ps1` passed; only `.pdb`, `.xml` and `ScarletMeter.staticwebassets.endpoints.json` sit beside the exe. The script rejects a relative path.
- Published exe with `--no-capture --data-dir <scratch>`:
  - Rendered "Capture is off", 00:00, 0/s, -- ms and Reset (screenshot, not committed).
  - Closed through the window's close (UI Automation), exit code 0.
  - The data directory held `combat-history.db`, `logs/` and `webview2/`. Neither `%LocalAppData%\ScarletMeter.WebView2` nor `%LocalAppData%\ScarletMeter` was created.
  - The log showed the UI-thread disposal fallback succeeding.
- Failure dialogs, each exit code 1:
  - `--data-dir` under an existing file: "Cannot create ...". No log, by design.
  - A publish copy with `GameData` renamed: "Classes data file not found: ...\Data\classes.json", and `[FTL] Startup failed.` in the log.
  - `--capture`: "Unknown argument".
- CI: the bootstrap commit 1de352f passed (run 39920164346, 1m57s).
- Cleanup: removed 31 `scarlet-tests-*` temp directories (7 from the bootstrap, the rest from diagnosing the leak) and the bootstrap smoke's `%LocalAppData%\ScarletMeter.WebView2`.

**Not checked:**

- `Update-Upstream.ps1`: not run, since there is no newer release; `--quiet` is untested in ISE or VS Code.
- The unhandled-exception handlers and `MeterPage`'s refresh-failure path were not triggered (no fault injection).
- Live capture and the game.
- CI on this branch (pending the PR).

**Upstream quirks, new candidates to offer upstream:**

- `IPacketService.Reset` clears `EntityTracker` summons and `TcpStreamBuffer` without a lock.
- `CaptureDevice.StartCapture` logs adapter open failures and then reports capturing with no adapter open.
- `CombatHistoryStore` starts its cleanup fire-and-forget in its constructor, and the task can re-create the database after its host is disposed.
- `GameDataProvider` falls back silently from `GameData\Assets` to `Data\`, so a missing data folder is reported as `Data\classes.json`.

**Audit (deep pass, PR 1, head c73b555):** approve with nits. CI passed on that head (pull_request run 39953445631, push run 39953403158; "Verify published layout" passed, 24/24 tests). The auditor ran mutations; the guards caught an upstream ProjectReference in the app, a public `NoCaptureDevice`, the old `Reset`, the removed adapter catch and a broken cleanup-log match. Its nits, all applied:

- The adapter message no longer points to log details upstream never writes, and now covers Npcap listing no adapters (for example, Npcap restricted to administrators).
- `MainWindow.OnClosing` tolerates a constructor that threw after WPF registered the window, so that failure ends in the dialog rather than a crash (traced, not run).
- `TestEngineHost` waits only when the history database exists and reports a timeout together with any disposal failure. `TestDataDirectory` deletes even when `ClearAllPools` throws.
- The surface guard also scans protected members. A probe type with a protected upstream-typed property failed it, then was removed.
- `MeterPage` logs and retries a cancellation unrelated to disposal.
- Wording fixes in `MeterPage`'s summary and DEVELOPMENT.md.

The auditor's own failed runs (a long clone path stopped `e_sqlite3.dll` loading) left 20 empty `scarlet-tests-*` directories in the user temp directory; they are left for Harry to remove. After the fixes: `scripts/Build.ps1` Debug and Release, 24/24.

**Next:** CI on the fixes, Harry's go-ahead and a squash merge; then in-game acceptance (HANDOFF.md).

## 2026-10-23 — Bootstrap

**What:** New private repository for a meter with its own UI on upstream RATmeter's capture and decoding. Upstream is a git submodule pinned to release v1.11.4.334 (the same commit the harrynguon/AIon2-Dps-Meter fork tracks). `ScarletMeter.Engine` wraps upstream behind `ICombatEngine`/`CombatSnapshot`; `ScarletMeter.App` is a WPF window hosting a Blazor WebView page that polls the engine. Tests guard the boundary and the mapping. `scripts/Build.ps1`, `scripts/Update-Upstream.ps1` and CI are in place. The root `Directory.Build.props` overlays upstream's Services project with the fixed SQLite bundle (ARCHITECTURE.md, "Upstream overlay"), because the pristine release pulls the vulnerable native package and the audit-as-error restore rejects it; that override is a fork-only edit upstream never took.

**Why:** Keep upstream updates a pin change, keep Scarlet's code free of upstream internals, and give the UI full freedom. Submodule rather than subtree so upstream code is never edited and its provenance stays explicit; rather than a copy so it keeps receiving upstream's parser fixes.

**Checks (SDK 10.0.402 through `scripts/Build.ps1` with `SCARLET_DOTNET`):** restore with NuGet audit as errors passed once the overlay was in place; build 0 warnings, 0 errors with `TreatWarningsAsErrors`; `dotnet test --solution` 14 passed (3 boundary, 5 engine registration and mapping, 2 options, 3 launch switches, 1 SQLite runtime). Smoke: the built `ScarletMeter.exe --no-capture --data-dir <temp>` rendered the waiting state (header, 00:00, 0/s, -- ms, Reset), closed through the window's close with exit code 0, and left `combat-history.db` and `logs/scarlet-<date>.log` in the temp directory (correction, see the review fixes above: it also created the WebView2 profile `%LocalAppData%\ScarletMeter.WebView2`). The log showed BlazorWebView's off-thread WebView2 disposal failing (`InvalidCastException`) and the UI-thread fallback in `MainWindow` succeeding. The screenshot is not committed.

**Not checked:** live capture with Npcap and the game (HANDOFF.md); CI (first run after the push); the Release single-file publish (CI's smoke step); DPI scaling and multiple monitors.

**Upstream quirks found, candidates to offer upstream or the fork:** `AddPacketProcessingRouting` registers every type in its processor namespace, including nested records and compiler-generated iterators, so `ValidateOnBuild` fails (the tests leave it off). `PacketPipelineService.Start` sets `isRunning` before discovering adapters, so a failed start cannot be retried in-process (the app exits instead). BlazorWebView 10 disposes the WebView2 composition control off the UI thread; the fork's companion logs show the same warning, and `MainWindow.ReleaseBlazorContentAsync` holds the fix.

**Next:** push, watch the first CI run, then in-game acceptance.

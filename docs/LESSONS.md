# Lessons

Mistakes found in this repository's work, why they got through, and what now catches them. Read this before starting a change; add an entry with every fix for a mistake (AGENTS.md, "Lessons"). Prefer a guard (a test, a check, a rule) to a note. Newest first within each section.

## In the product

**A note box with a sensible width pushed the roster's last columns out of its window** (2026-10-27, seen in a headless render before commit).
- What: a 160 px, then 130 px, least width for the new note box under each name pushed the Last played column and the remove button past the roster's 920 px window: the table had no room to spare, since #50 had already widened the window for the Kina column.
- Why: a column's least width was chosen for the note's sake, without measuring what the rest of the table needed at the window's width.
- Guard: the box takes the name column's width and no more (a long note scrolls in it, and pointing at it shows it whole: `RosterPage.NoteTitle`), and the roster's renders at its window's width are part of any change to its columns. Before giving a table column a least width, render the table at its window's width with the widest likely values.

**A Kina change just before a switch would have gone to the next character** (Codex and the deep audit on #50, fixed before merge).
- What: the roster's reader kept the inventory's Kina across a login's list when it had been read within ten seconds before it, meaning to keep the new login's listing wherever it falls. But the time it kept was that of any inventory record, changes included, so a Kina change of character A just before switching to B (whose listing was missing) was reported as B's, saved, and counted twice in the totals. The same audit found a moved item counted twice (slots summed) and a listing with no recent self record given to whoever was named.
- Why: the rule was designed around one record (the listing) but keyed on a time every inventory record set; the tests sent only listings around a login, never a change before a switch.
- Guard: a whole list keeps the Kina only when a listing (only a listing sets that time) came within the burst (`EntryListTests.AChangeJustBeforeASwitchIsNotTheNextCharacters`), a listing with no self record just before it waits (`AListingWithNoSelfRecordJustBeforeItWaits`), Kina is one slot per item (`KinaThatMovesSlotIsNotCountedTwice`); each shown to fail with its guard removed. Codex's third review found the same rule open across connections: a listing read just before a move still counted for the next login's list, so a quick switch with the new listing lost gave A's Kina to B; a move now ends the listing's claim (`AListingFromTheConnectionLeftIsNotTheNextLogins`); its fourth found a listing held for a self record that never came surviving the move away from its connection and going to the character named on the next one, and a move now forgets a held listing (`AHeldListingIsForgottenWhenItsConnectionIsLeft`); its fifth found the same for a change read with no character confirmed, which a move now forgets too (`AnUnconfirmedChangeIsForgottenWhenItsConnectionIsLeft`). The three were one gap found piece by piece: what was read while no character was confirmed had no state of its own. Guard for such a rule: list every kind of record against every owner state (named, awaiting the self record, unconfirmed after a move, none) and every way out of it (a self record naming the same or another, a whole list, a move), and test each cell. When a rule is about one kind of record, key it on that kind's own time and connection, and test every other kind of record, and a move to a new connection, against it.

**A switch whose first records were lost would have put one character's counts on another** (deep audit on #48, fixed before merge).
- What: the roster's reader kept the character named last across a new connection, since counts are the character's. But a switch is a new connection too, and upstream can lose a new connection's first records, the login's list and the self record among them; the new character's updates would then have been reported as the last one's, and the roster would have shown them on the wrong row until that character logged in again. Separately, a tutorial character's placeholder name (`$` and a code) became a character, since upstream's decoding drops the `$`.
- Why: the earlier lesson ("The map probe kept a map after the game changed server") was read as about values that belong to where the player is; the owner of the counts was taken as belonging to the character, without asking what else a new connection can be. The name came from upstream's decoded identity, and A2Tools' placeholder rule, beside the owner rule that was ported, was not.
- Guard: a new connection leaves the character unconfirmed and holds the reports until a self record names it again, the same one getting them and another dropping them (`EntryListTests.ANewConnectionHoldsReportsUntilTheCharacterIsNamedAgain`); placeholders name no one (`ARecordWithoutARealNameNamesNoOne`, and through upstream `UpstreamsNameForATutorialCharacterIsNotACharacter`); each shown to fail with its guard removed. For any value tied to a who or a where, test it across a new connection whose first records are lost, and port a source's rules for what a record means beside its layout.

**The first Odyle port read an entry's id as a slot and a key** (found 2026-10-27, porting A2Tools' whole entry list for the character roster; in 0.3.0).
- What: `OdyleEnergyRecords` read the energy as kind `0c`, "slot 01" and a varint "key" `87 93 03`, as A2Tools' PR #63 described the bytes it saw. They are the entry's u32 id, 60000001, little-endian: every entry of the list is a kind, a four-byte id and its values. So the reader could read no other entry, found the list by a "slot 2 with the same key" follower that only held by chance, and turned away as "look-alikes" bytes that were real entries (`0c 01 00 00 00 …` is id 1, Shugo Festival, with its counts), guarded and tested as if they were noise. It read the energy correctly, so nothing looked wrong.
- Why: the port modelled the format on one PR's description of the bytes, not their structure, and when look-alikes turned up, guards were added against them instead of asking why the format kept producing them. The earlier lesson's check (list the source's commits since the pinned one) was run on the ported functions' names; A2Tools' reading by id (b79969a, 2026-10-25) came under new names and was not looked at until a feature needed more of the list.
- Guard: `EntryListRecords` reads the list by id, as A2Tools now does, with `EntryListTests` in its documented shapes (each kind, time entries, the list found where it ends, an id-1 entry read as a count). When a parser needs guards against bytes the format itself sends, decode them under the source's newest reading before guarding against them; when re-checking a ported reader, search the source's history by what it reads (the opcode, `0b 61`), not by the ported function's name.

**Cutting the bar area at its last whole row first clipped the player's own ring** (2026-10-26, seen in a headless render before commit).
- What: hiding the sliver of the next bar below the fifth, the first hard cut took the full 9 px of padding and gap, which also took the bottom line of the player's own gold ring: a `box-shadow` spread 1 px outside the row, so outside the row's box that the cut was measured from.
- Why: the cut was worked out from the row boxes' geometry, and a ring drawn outside a box is not part of it.
- Guard: the cut is 8 px, and `RazorMarkupTests.TheBarAreaScrollsWhenItCannotShowEveryRow` derives it from the padding, the gap and the ring's spread (with effects on as well) and checks the ring is narrower than the gap. When clipping or masking near a ringed, outlined or shadowed element, zoom into a render of its edge before trusting the box arithmetic.

**Entering the Abyss would have thrown away a boss fight that had not been saved yet** (deep audit on #42, fixed before merge).
- What: the Abyss pause reset upstream's fight on entering (`CombatSessionManager.Reset`) to start from nothing. Upstream saves a finished fight only at a later hit (its idle check), a new try or exit, and its reset saves nothing, so a boss killed just before going to the Abyss never reached History. Closing the meter while paused did the opposite: the exit's save kept the Abyss's last boss hits (Codex).
- Why: the reset was treated as dropping only what was on screen; nobody asked where upstream saves a fight, and the tests asserted the clear without a fight open from before it, or an exit while paused.
- Guard: entering completes and saves the open fights first, as at exit, and the engine drops the Abyss's fight before its exit save; `AbyssPauseTests` checks the fight from before saved on entering and nothing of the Abyss saved by a sweep, on leaving or at exit. Before resetting or replacing upstream's sessions, find when upstream saves (here `TargetEntry`'s idle check, a new try and `CompleteAndPersistActiveSessions`) and test a fight left open across the change.

**The map probe kept a map after the game changed server** (found by Harry with a test copy, 2026-10-26; never released).
- What: after leaving a dungeon the probe showed no map, and after leaving the Abyss it still showed the Abyss. A server change moves the game to a new connection; upstream reads a new connection only from its first keep-alive on and drops what comes before (PacketAccumulator), and the map record often comes before it (entering the Abyss it came after and was read; leaving a dungeon and the Abyss it did not). The probe trusted the last map it had read, which a future Abyss switch would have read as "still in the Abyss".
- Why: every test brought the stream into step (`Synchronise`) before sending the map record, so a record that opens a new connection was never tried; the accumulator's quirk was already documented for the Odyle tap ("a new stream is synchronised only by a keep-alive left from an earlier chunk"), but nobody drew its consequence for the records that start a connection. The probe was built on the source's claim ("sent on every zone load") without asking whether every load reaches the meter.
- Guard: `MapProbeTests.UpstreamDropsTheMapRecordThatOpensANewConnection` pins upstream's loss (it fails if a release reads new connections from their start); the record tap tells its readers when the game moves to a new connection (`IRecordReader.ConnectionStarted`) and the map is forgotten there (`TheEngineForgetsTheMapWhenTheGameChangesConnection`), so it is unknown, the safe side, rather than wrong. For any reader whose value belongs to where the player is, test the record arriving as the first thing on a new connection, not only on a stream already in step.

**Other players' training-dummy fights filled the overlay and History** (Harry, 2026-10-26: "if i afk there then it'll pollute the dps logs with randoms").
- What: standing by a training dummy other players were hitting, the meter showed their fight and saved it to History. Upstream counts every dummy as a boss (so its fights are saved), and once the user's own last hit is five seconds old its `ActiveTargetResolver` makes the target with the most recent hits the active fight, whoever dealt them.
- Why: the engine took upstream's choice of "the fight" and its boss list as given. The demo and every test had one party on one target, so nobody asked what upstream does beside players who are not in the party.
- Guard: `DummyFightRule` (pure), applied by `DummyFightFilter` to the overlay, the details and the history list and by `DummyFightHistoryStore` to saves (ARCHITECTURE "Training dummies"); `DummyFightTests` put a stranger beside the user through the real graph, and 19 mutations each failed their test by name. When the meter shows or saves upstream's idea of the current fight, test it with someone outside the party hitting something nearby.

**The overlay's saved place was dropped again, at no rows** (deep audit on #37, fixed before merge).
- What: with the window hugging its content, it saved its shown height, which at no rows and no notice due is 77 px, below the 96 px floor `SettingsNormalizer` keeps a saved size to, so the whole placement was dropped and the meter came back centred: the same failure as "A small overlay lost its saved position" below.
- Why: the floor was set for a window the player sized; when the window became as short as its content, nothing re-checked the smallest content against it, and the earlier guard tested only the player's smallest size.
- Guard: the floor is 48 px, `OverlayFitTests.TheEmptyMeterIsAboveTheOverlaysFloor` pins it below the measured empty meter, and the window saves no height below it. When what a window's size follows changes, re-check every bound its saved size meets.

**A drag of the meter's top edge past its most moved the whole window up** (found by Harry with the portable copy, 2026-10-26; fixed before the PR).
- What: during a resize the overlay held its height with WPF's `MinHeight` and `MaxHeight`, which WPF enforces by keeping the top and moving the bottom. Dragged by the bottom edge that stops the edge; dragged by the top past ten rows, the top went on following the cursor and the bottom moved up with it.
- Why: every check dragged the bottom edge only (a `SetWindowPos` that kept the top), the case where WPF's limits look right; the other edges were never tried.
- Guard: the window holds the dragged rectangle itself in `WM_SIZING`, by the edge being dragged (`OverlayFit.HoldDraggedEdge`); `OverlayFitTests.AnEdgeIsHeldWhereItIsDragged` covers all eight edges and corners, and the demo check drags the top edge both ways. When a resize has limits, try every edge, not only the one the default size grows from.

**A parser was ported from a merged PR without the source's later fixes** (found by Harry as `0 (+0)`, 2026-10-26; Odyle energy is in no release yet).
- What: `OdyleEnergyRecords` followed A2Tools' PR #63 as merged (94dda50). Within a day A2Tools fixed it twice (2723bff, 29e7193): the entry has three kinds, a zero left out, and updates that start `00` carry an entry. The port, made ten days later, read only the two-value kind and trusted the count, so a player with no basic or no charged energy got nothing, or a look-alike could read as `0 (+0)` (which one Harry's game sent is not known). Its fix then widened what an update accepts (three kinds at slot 1) without re-checking the update path, which had no key check, so another counter's one-value entry would have read as the energy; and some new test bytes were copied from A2Tools' published samples while the tests claimed to be synthetic (both found by the deep audit on #36).
- Why: the source was taken from the PR that introduced it, not from the source's current branch, and the history of the ported functions since that PR was not looked at. Our synthetic fixtures came from the PR's samples, so they confirmed the PR.
- Guard: THIRD-PARTY-NOTICES pins every fix commit ported; `OdyleEnergyTests` covers each kind, updates that start `00`, the zero look-alikes, the count kept and the list's key ruling updates, with fixtures modelled on the samples' shapes, not their bytes. When widening what a parser accepts, re-check every path that relied on the narrower match. When porting, read the ported code on the source's default branch and list its commits since the pinned one (`gh api repos/<owner>/<repo>/commits?path=<file>`), and do the same first when a ported reader misbehaves.

**The row glide measured in content coordinates inside a list that now scrolls** (deep audit on #35, fixed before merge).
- What: capping the player list made it a scroll container, but `overlay-fx.js` still compared each row's `offsetTop`, which ignores scrolling. Chromium moves a scroll container's position by itself as rows reorder (scroll anchoring at the view's edge, clamping at the end of the list when a row above the view goes), so a reorder while scrolled turned that shift into `translateY(±29px)`: every visible row, the pinned own row included, jumped a row and slid back. The cut state's 26 px fade also beat the capped list's own fade on specificity, half fading the pinned row.
- Why: the headless check looked at the pinned row at rest (scrolled, then measured), never at a reorder while scrolled, and the script's assumption that a row's position is its offset in the list held only while nothing in the overlay scrolled. The fade rules were each checked alone, not together.
- Guard: rows are measured in the list's view (`drawnTop`, its offset less the list's scroll), again on scroll and on a resize; the capped list has `overflow-anchor: none` and its own fade while cut; `RazorMarkupTests.TheBarAreaScrollsWhenItCannotShowEveryRow` (`TheRowsScrollPastTheirCap` then) and `TheOverlayPanelFitsItsContent` assert each, and a headless reorder-while-scrolled page (swaps at the view's edge, a row leaving at the end, a row moving past the view, the pinned row at either edge) showed the old script jump and the new one keep every untouched row in place (WORKLOG). When something starts to scroll, re-check every script that measures positions inside it, with the content changing while it is scrolled.

**Synthetic records were modelled on our own writer, not on the documented sample** (deep audit on #34, fixed before merge).
- What: every Odyle list in the tests gave the energy entry a two-value follower, the shape `OdyleEnergyRecordWriter` happened to write, while the game's list as A2Tools documented it has a one-value follower; a change that required the two-value kind would have stopped the meter reading the login list with every test green. The same audit found two catches each hidden by the other in the tests, and a guard tested on one bit of four.
- Why: the fixtures were built from the parser's own understanding of the format, so they confirmed it rather than the source; the tests were checked by mutating the code that was written, not the shapes and failure paths the source and the upstream code allow.
- Guard: the writer writes the documented shape, and `OdyleEnergyTests` covers both follower kinds, the documented trailing bytes, every overflow bit, and each catch on its own (a test seam for the bundle opener). When porting a parser, derive the synthetic fixtures from the source's documented samples (their shape, never their captured bytes), and mutate toward plausible "tightenings", not only removals.

**The background picture was scaled to the window, not the panel** (found by Harry after #26 to #31, fixed in #33).
- What: when the panel became only as tall as its content (#26), its picture was laid out against the window (`background-attachment: fixed`) so it would not rescale as the panel changed height. But the window is often taller than the panel (the player's own size), and the picture was then scaled to the window's height and centred on it: the panel showed a zoomed-in slice, the middle of the picture often below it. A test pinned the `fixed` rule as intended.
- Why: every render, GIF and window check of the picture had a window about as tall as its panel, where the two layouts look the same; the case that differs, a window taller than its content, was never drawn.
- Guard: the picture covers the panel (`RazorMarkupTests.TheOverlayPanelFitsItsContent`: `center / cover`, no `fixed` anywhere); HANDOFF's picture check names a window taller than its rows. When a layout depends on a size relation (window and panel, container and content), render each side of it, not just the usual one.

**A sticky table header left a gap that scrolled rows showed through** (found by Harry, fixed in #33; since the details window).
- What: the details and history tables' header sticks at `top: 0` inside the tab body, which had 6 px of top padding; a sticky element stops at its scroll container's padding edge, so a 6 px strip above the header showed the rows scrolling under it.
- Why: the tables were only looked at unscrolled, where the padding reads as spacing.
- Guard: the tab body has no top padding and a spacer that scrolls away (`RazorMarkupTests.StickyTableHeadersSitFlushUnderTheTabs`), measured in headless Edge: the header's offset from the top of the scroll is 0 when scrolled. Check a sticky element scrolled, not only at rest.

**The app icon came with a loader WPF already provides** (deep audit on #32, fixed before merge).
- What: every window set its icon from a second, embedded copy of the .ico through a cached loader (`Windows/AppIcon`), but WPF already gives a window without an `Icon` the exe's own, so the loader changed nothing on screen while adding ways to fail: exceptions it did not catch, a failure the cache would replay on every window's construction, and a decoder tied to one thread. Its docs also described a fallback ("the default") that was the same icon. Its tests read file names and strings: a hook commented out, a new window without one, swapped or broken entries in the .ico all passed.
- Why: the window code was written from the assumption that each window needs its icon set, without checking the framework's default for a window that sets none; the tests were again written for the code in mind rather than for the claim ("every window uses it").
- Guard: no window sets an icon (`AppIconTests.NoWindowSetsItsOwnIcon`, over every source file with comments dropped), the built exe and assembly carry all nine sizes (read from their resources), and the .ico's entries are checked against their own PNG headers and decoded; six mutations, each failing its test, and a commented-out setter passing. Before adding code for a platform behaviour, check what the platform does when the code is absent.

**The one-bar event strip's guards were narrower than their comments** (deep audit on #30, fixed before merge).
- What: the strip's bar was said to be "as tall as a notice" but was 2 px shorter (a button is border-box, a notice is content-box), the source guards did not pin what the closed bar shows (five mutations survived: the wrong notice named, no "+N", no progress line, a soft shadow under `.fx .row`, `--row-edge` turned off later), and the shadow guard only read selectors that start with `.row`.
- Why: the change was looked at as a picture in headless Edge, not measured, and each guard was written for the mutation in mind rather than for the claim in its comment.
- Guard: `RazorMarkupTests.TheNoticeStripIsOneBarThatOpensAndCloses` pins the closed branch and the bar's height against the notice's height and border; `ARowsTextSitsOnACrispDarkEdge` reads any selector naming a row, with comments dropped, and `--row-edge` declared once. When a comment or a doc states a size, measure it in the headless render (computed style or bounding box) before writing it; when a guard's comment says "nothing", mutate the case its regex does not read.

**The first boss-timers window trusted WebView2's words more than its docs** (deep audit on #29, fixed before merge).
- What: the window treated "user-initiated" as "the player clicked this link", but WebView2 counts any script running from a click, so an advert could turn one click into a burst of browser tabs, local addresses included; and a toolbar click after the page's browser process crashed would have thrown out of the handler and ended the meter, since the control's `CoreWebView2` getter throws then. Screen capture, Save as, dropped files and the context menu were also still open.
- Why: the lock was designed from the event names and the obvious cases (a link, a download, a permission), not from the SDK's own documentation of each property and of the control's failure modes; the tests covered the pure rule, and its callers only as source text.
- Guard: the window's decisions are pure and tested (`DecideNavigation`, `DecideNewWindow`, `IsWebPage` refusing local hosts, `BrowserLaunchGate`), every button acts through a guard, the start catches everything, the remaining capabilities are refused, and each is in the source guard; 11 mutations, each failing its test. Before hosting third-party content, read the SDK's documentation of every event and property relied on, and list what a hostile page could still do.

**A small overlay lost its saved position** (PR #28's window check, fixed before merge; older than #28).
- What: the overlay could be resized down to its minimum height of 96 px, but the settings normalizer treats any saved size under 120 px as unusable and drops the whole placement, so the next save of a short overlay erased the player's position and size, and the next start used the default centre.
- Why: the two limits were set in different files with no test tying them together, and no check had ever resized the overlay below 120 px.
- Guard: one constant, `WindowPlacement.MinSize`, is both the overlay's `MinHeight` and the normalizer's floor; `SettingsTests.TheSmallestOverlayAPlayerCanMakeIsKept` and a guard on the overlay's `MinHeight`, each shown to fail. When a window limit and a settings limit describe the same thing, share one constant.

**A rewritten comparison against NaN switched the window's growth off** (Codex P1 and the re-audit on #28, fixed before merge).
- What: while wrapping the page's fit in a guard, `if (Math.Abs(height - reportedHeight) < 0.5) return;` became `if (Math.Abs(height - reportedHeight) >= 0.5) { fit; reportedHeight = height; }`. `reportedHeight` starts as NaN and every comparison with NaN is false, so the first height never reached the window, nothing ever set `reportedHeight`, and the window never grew; the new "cut" rule then faded rows with the whole screen free. The commit's docs, its WORKLOG checks and the message to Harry all said the window grows.
- Why: the condition lived in the page, guarded by a source-text test that pinned the new text (bug included); the 13 mutations exercised everything around it but not the condition itself; and "inverting a test" was treated as a refactor, when with NaN `!(a < b)` is not `a >= b`. The headless measurement checked the CSS, not the window.
- Guard: the decision is `OverlayFit.IsNewHeight`, unit-tested with the first (NaN) report; the page must call it and may not write the comparison itself; the bug put back in either place fails a test. Never rewrite a comparison by negating it when either side can be NaN; and a claim that a window does something waits for a check in a window.

**Growing instead of scrolling left a dead end at the screen's height** (Codex and the deep audit on #28, fixed before merge).
- What: the overlay was changed to grow instead of scrolling, capped at the monitor's work area. Past that cap the panel clipped with nothing left to scroll, so in a big field fight (40 rows on a 1080p screen, or 16 rows with the notices expanded on a 150% laptop) the notices and "Show less" were cut off and could not be reached. The same audit found the window's placement rules unproven: a grown window moved up for the screen's bottom snapped after a drag, a restore refitted inside WPF's own size handling, a save during a held drag stored the old placement, an exception from the fit would have ended the process, Ctrl+wheel zoom would have grown the window to the wrong height, and four mutations to the window code passed all 435 tests.
- Why: the change was designed for the request in front of it (show everything, grow if need be) and its arithmetic was tested at the cap, but nobody asked what the page does once the window cannot grow; and the window's logic lived in the WPF class, with only source-text guards.
- Guard: at the cap the rows give way (`.overlay.cut .rows`, faded) and the notices never do, measured headlessly at three caps with 16 to 40 rows (WORKLOG); the placement rules are in the pure `OverlayPlacement` with `OverlayPlacementTests`; a restore refits after layout, saves wait for a drag's end, the fit is caught and logged, the overlay's WebView does not zoom; 13 mutations, each failing its test. When a feature adds a limit, ask what the player sees at the limit, and test a window's logic apart from the window.

**Class names were upstream's, not AION 2 Global's** (Harry, 2026-10-25).
- What: the meter showed "Elementalist", the name in RATmeter's game data (`classes.json`), for the class AION 2 Global calls the Spiritmaster, in the overlay, the details, the history and the demo.
- Why: names were taken from upstream's data as they come, and nobody compared them with the Global client the meter is for.
- Guard: `Engine/Upstream/GlobalNames`, applied where the engine maps players' class names, with `GlobalNamesTests` and checks on the live row, the details and a saved fight; upstream's data stays unedited. When a name reaches the player from upstream's data, check it against Global's.

**Two markers for "you", one of them read as a glitch** (Harry, 2026-10-25).
- What: the player's own bar carried a 2 px white stripe at its left edge (`.fill.me-mark`, from before the bar effects); when the effects added a glow ring round the player's row (#21), the stripe stayed, and on its own it looked like a rendering fault.
- Why: the new marker was added without removing or revisiting the old one, and screenshots were checked for the effects, not for leftovers at the bar's edge.
- Guard: one marker, a ring round the row (gold, or the effects' glow), and `RazorMarkupTests.TheOwnRowIsMarkedByARingNotOnTheBar`. When adding a visual cue, look for the one it replaces.

**Fitting the panel to its content squeezed the expanded strip, and the first fix clipped it** (audit and Codex on #26, fixed before merge).
- What: changing the rows from `flex: 1` (they gave up their space) to `flex: 0 1 auto` (they keep their own height) made the expanded notice strip, a scroll area that may shrink, give up space in proportion to the rows: with a full party it opened about one notice tall, the same squeeze #20's lesson describes. The first fix, `flex-shrink: 0` on the strip, then let it run past the bottom of a short window's panel, clipping its "Show less" so it could not be collapsed (Codex). The demo check could show neither, since the demo has four players and the default window.
- Why: each change was judged by the case in front of it (four rows with the notices held; then a full party), not by the page's extremes in both directions (many rows, and a short window), and a flex rule on one child changes how its siblings shrink.
- Guard: the rows give up their space first (`flex: 0 1000 auto`, a heavy shrink weight), and the strip can still shrink to what is left; `RazorMarkupTests.TheOverlayPanelFitsItsContent` asserts both (a weight of 100 or more, no `flex-shrink: 0` or `flex: none` on the strip) and pins what the panel's height depends on (`#app` and `html, body` at 100%, no other rule sizing the panel). A headless Edge measurement with the real styles and synthetic markup (no window) compared main, the first fix and the final rule across row counts, window heights and strip states (WORKLOG). When a layout rule changes, measure both extremes, not only the demo. (#28 replaced these rules: nothing scrolls, the rows and the strip keep their full height and the window grows, and only at the screen's height do the rows give way; `TheOverlayPanelFitsItsContent` now asserts that.)

**Boss-fight hiding trusted the current target** (audit on #24, fixed before merge).
- What: the first "Hide during boss fights" hid the notices while a boss was the current target and its fight live or settled. Settled has no end and upstream changes its target only on a hit, so after a wipe the notices would have stayed hidden until the next hit; and the target flips to adds and back, so the strip would have come and gone on every swap. Unmounting the strip also broke its own live-region rule, and "Bring the meter back before an event" could restore a minimized meter mid-fight with its notice hidden.
- Why: the same assumption #21's lesson names, one steady target, came back in a new feature: the rule was read off `FightState`'s phases without asking how long a phase can last or what the target does between hits; the strip's own comment, which said why it is never unmounted, was not reread.
- Guard: `BossFightHold` follows the boss across adds and lets go after 60 seconds of still HP, with `BossFightHoldTests` (each rule shown to fail when removed); the strip is held, not unmounted; `AlertRestoreGate` with its test. For any rule fed by upstream's target, write the test for adds between hits and for a target that never goes.

**Switches that were never saved went unnoticed** (audits on #22 and #24, fixed before merge).
- What: twice, a `[JsonIgnore]` on a new setting (`PictureOpacity`, then `HideDuringBossFights`) passed every test, so the player's choice would have been lost at the next start without a failing test.
- Why: each new setting got a default test and an equality entry, but the round trip through the file was tested per setting, by hand, and forgotten for the new one.
- Guard: `SettingsTests.EveryOnOffSettingSurvivesASave` flips every bool in every section, saves, loads and checks each (shown to fail with a `[JsonIgnore]` on two switches), so a new switch is covered without a new test; numeric settings keep their own round-trip tests.

**"Kill in", the glide and the effects assumed one steady target** (audit on #21, fixed before merge).
- What: "Kill in" forgot the boss whenever the current target (upstream's: the one last hit) flipped to an add, so its estimate kept restarting; the glide measured row positions from the panel and kept them across a row-size change, so a later reorder glided and flashed rows that had not overtaken anyone; the effects ran on every row and would have run between pulls; two test guards (scripts, styles) matched too loosely to fail when broken.
- Why: the features were built and checked against the demo, whose single boss is the only target and whose rows go when it dies; the tests were written alongside the code and checked for passing, not each broken on purpose until the audit did it.
- Guard: per-target samples, `FightState` pausing the effects, an eight-bar cap, rank-based flashes and re-measured positions, each with a test or a visual check at other row sizes; whole-selector and every-tag guards, each shown to fail. Ask how a value behaves in a real fight (targets flipping, a full party, the time after a kill), not only in the demo, and break every new guard once before trusting it.

**The expanded alert strip would have squashed its notices instead of scrolling** (audit on #20, fixed before merge).
- What: the expanded strip was capped at 45% of the meter with `overflow-y: auto`, but each notice, a flex item with `overflow: hidden`, could shrink to nothing, so five due notices would have been squeezed into the cap with their text clipped and no scrollbar. Once drawn, the strip showed two more problems no reading had found: "Show less" scrolled out of sight, and the strip had no edge against the rows above.
- Why: the layout was written and reasoned about but never drawn, since drawing it needs a window and the player may be in game; the tests cover the helper's lists, not the CSS.
- Guard: `.alert { flex: none }`, a sticky "Show less" and a top line on the expanded strip, each with a comment saying why. A visual change is not done until it has been seen once, at its fullest (here, more notices than fit): ask the player for a moment when a window can open (LESSONS, "UI checks opened windows"), rather than skipping the look. (#28 removed the cap, the scrolling and the sticky "Show less": the strip shows every notice and the window grows; `.alert { flex: none }` and the top line remained; #30 folded the strip into one bar, whose header now separates it from the rows, and dropped the top line.)

**The in-place update guarded "data", not the player's files** (deep audit and Codex on #19, fixed before merge).
- What: the first version of the one-copy update moved only a top-level `data` folder and deleted everything else in the old copy, so a `data - Copy` backup or notes beside the exe would have gone on the next update. When an undo failed it still said "your copy and its data are as they were" while the data sat in `.staging`. Its deletes removed the portable markers in directory order, so a delete stopped by an open file left an unmarked folder every later run refused. A drive root as output root became `C:`, which Windows reads as that drive's current directory. Two runs at once could delete each other's staging folder. After those fixes, Codex and a second audit found the recovery itself was not resumable: it dropped the staging marker before the rest of the player's files had followed from `.old`, so a recovery stopped there left a runnable copy without its data that every later run refused; and the earlier-build clean-up deleted through an inner folder that was a junction.
- Why: "the player's data" was equated with one folder name instead of "everything the build did not put there"; the failure paths were tested for the first failure only, not for an undo or a recovery failing too; the marker-last delete existed for the installer but was reused without its markers; root paths, links one level down and concurrency were not considered.
- Guard: a manifest of the build's files, so everything else is carried over and only listed files are deleted; an undo that fails leaves the copy's name free for the next run to finish and says where everything is; the copy keeps its staging marker until a recovery has finished, so the next run resumes it; `Remove-OwnedFolder` takes the markers to keep until last; `Remove-PortableFolder` refuses a folder that is a link; `Resolve-PortableRoot` keeps `C:\`; a named mutex per output root. `Test-PortableFolders.ps1` covers each, with a stand-in for the move that makes chosen moves fail and real held-open files, and each case was shown to fail with its guard removed. When code deletes near the player's files, define what is ours by listing it, and test each recovery step failing as well as the first.

**Share bars were hard to see** (Harry, from the screenshots, 2026-10-24).
- What: the details and saved-fight bars used a flat 32% red, which on the near-black panel came out as dark maroon.
- Why: the colour was picked as a token, not looked at on the real background; smoke runs checked the bars existed, not that they could be seen.
- Guard: bars use the overlay's gradients on a visible track. Look at a screenshot of every new visual element before calling it done.

**Settings backup overwritten when the main file was missing** (Codex on #2, fixed 2026-10-24).
- What: with `settings.json` missing and `settings.json.bak` damaged, from a newer build or locked, saving stayed on, and the second save's `File.Replace` replaced the only recoverable settings. Now a damaged backup is kept aside, a newer one is used read-only, and a locked or unmovable one switches saving off.
- Why: the keep-aside and read-only protections were written for a damaged main file only; the missing-file branch was not walked through the same cases.
- Guard: four `SettingsTests.WithoutTheMainFile...` tests (damaged, newer, locked and unmovable backups), shown to fail with the old branch. When adding a protection, list every branch that can reach the same write.

**Saved window position restored off every monitor** (Codex on #2, fixed 2026-10-24).
- What: placement was checked against the virtual screen's bounding box, which includes the empty corners of an L-shaped or offset layout.
- Why: a single-monitor machine never shows the difference.
- Guard: `ScreenPlacementTests` on a synthetic L-shaped layout. Test geometry against layouts this machine does not have.

**Update commands shown relative to the shell's directory** (Codex on #10, fixed in #12).
- What: Settings, About showed `scripts\Update-Upstream.ps1`, which only works from inside the checkout.
- Why: the paths rule was applied to code, scripts and docs, but not to text the UI shows.
- Guard: `RazorMarkupTests.ScriptCommandsShownToThePlayerAreRootedAtTheCheckout`. Commands shown to the player follow the same paths rule as docs.

**Next-event line skipped an event still running** (Codex on #6, fixed in #12).
- What: the line kept only occurrences in phase Upcoming, so Shugo vanished for four minutes after its entry closed (Invasion for ten).
- Why: the tests used an event that was not running at the instant tested.
- Guard: `AlertTests.TheNextEventLineKeepsAnEventWhoseCurrentOccurrenceIsStillRunning`. Test schedule code at the awkward instants (just after an entry closes, during a running occurrence), not only before a start.

**Buff counts rendered as Razor source text** (found in the #8 smoke).
- What: `x@buff.Applications` rendered literally: Razor reads a letter followed by `@` as an e-mail address.
- Why: the earlier smoke counted buff rows instead of reading them.
- Guard: `RazorMarkupTests.NoExpressionFollowsALetterOrDigitWithoutParentheses`; the scarlet-smoke skill says to read the text, not count elements.

**"Skill icons" off still loaded graph icons from the CDN** (audit and Codex on #8).
- What: `DamageChart` called `SkillIcons.Url(icon)`, whose `enabled` parameter defaulted to true.
- Why: an optional parameter let one caller forget the setting.
- Guard: `SkillIcons.Url` has no default, so a caller that leaves the setting out does not compile. A privacy or security switch is never an optional parameter.

**Late results overwrote newer state** (audits and Codex on #8, #10).
- What: a slower saved-fight load relabelled the details window; an older history search overwrote a newer page; a queued timer callback could call GitHub after the check was switched off.
- Why: results were assigned before checking they were still wanted; `ITimer.Dispose` does not stop a callback already on its way.
- Guard: load into a local, then apply only if the request is still current (a counter or the selection); check the switch and disposal at the top of every timer callback; tests with a check held in flight (`UpdateNoticeTests`).

**A hidden meter popped back four times an hour** (audit on #6).
- What: "bring the meter back for an alert" was on by default and fired for both the Soon and the Open notice of hourly events.
- Why: the default was chosen without counting how often it would fire.
- Guard: once per occurrence, Soon only, off by default; `AlertTests`. Count how often a default fires in a real hour before turning it on.

**A hand-edited settings file with a byte-order mark lost every setting** (found in the #6 smoke).
- What: the UTF-8 JSON reader rejects a BOM, so the file counted as unreadable and defaults were used.
- Why: tests wrote files without a BOM; only a smoke run that seeded a file from Windows PowerShell showed it.
- Guard: `SettingsTests.AFileSavedWithAByteOrderMarkLoads`. Seed test files the way people actually write them.

## In the process

**Save tests passed with a change's save removed** (2026-10-27, the roster's notes; caught by a fail-first run before commit).
- What: with `MarkChanged` taken out of `RosterService.SetNote`, the note test still passed: earlier changes in the same test (a report, a corridor run) had marked the roster changed, so the note was saved with them. The same run found that a restart's loading of the artifact holders, a control character at a note's end and two listings of one artifact were each in no test.
- Why: the test checked the saved file at the end of a sequence, not that this change alone leads to a save.
- Guard: each kind of change has a test where it is the only change before the save (`RosterTests.ANoteIsKeptThroughReportsAndSaved`, its second part), and each mutation the run missed now fails a test. When testing that something is saved, make it the only change since the last save.

**A mutation runner's restore kept the file's old time, so the next build kept the mutation** (2026-10-27, the character roster; caught by a control).
- What: the scratch runner restored each mutated file with `Copy-Item`, which keeps the original's write time. That time was older than the mutated build's output, so MSBuild took the project as up to date and kept the mutation compiled in: the run's last engine mutation (`ToIdentity` without its user check) stayed in the engine for every run after it. Those runs mutated only the app and ran only its tests, so their catches stood, but the next run's control failed on `EntryListTests.UpstreamsUserIsTheCharacter`.
- Why: the runner checked that each mutated build compiled, not that the restored source was compiled again, and its only control ran first.
- Guard: the runner marks a restored file as changed (write time now) and ends every run with a control over every test class it ran; a run counts only when both controls pass. A restore by copy is a restore of content, not of build state.

**A mutation run that ran no test passed its controls** (2026-10-26, the dummy-fight change; caught before any result was used).
- What: the fail-first script ran the test executable with `--filter-class`, the testing platform's option, but the executable is xunit's own runner (`-class`): it refused the option and ran nothing. Every mutation came out "missed" and both controls "passed", since nothing failed.
- Why: the controls checked only that no test failed, which a run of no tests also satisfies; the test count was printed but not checked.
- Guard: the script's verdict needs a test count above zero ("no tests ran" otherwise) and cross-checks the failures it names with the summary's count. A control proves the guard only when it also shows the tests ran.

**A line-ending check read LF files as CRLF** (2026-10-26, the dummy-fight change; caught before commit).
- What: the editor writes new files with LF while the repository's working files are CRLF. `grep -c $'\r$'` in Git Bash reported every line of the five new files as CRLF; only `od -c` showed plain LF.
- Why: the count looked right (one per line) and matched what was expected, so it was not questioned.
- Guard: count carriage returns as bytes (`tr -cd '\r' < file | wc -c` beside `tr -cd '\n'`, or PowerShell's `ReadAllBytes`), and convert new files with PowerShell (`ReadAllText`, `\r\n`, `WriteAllText` without a BOM), not Git Bash's `sed -i`, which writes LF.

**A helper that rewrote test lines dropped five new assertions** (2026-10-26, caught by a fail-first run before commit).
- What: replacing a test's lines by number with a PowerShell function, the multi-line replacements were written as `` Line 85 'a' + "`r`n" + 'b' ``, which PowerShell parses as separate arguments: only the first piece was written, and the rest (the no-rows guard among them) silently went missing. The build and every test passed.
- Why: nothing checked the edit's result; the passing build read as success.
- Guard: the fail-first run, which showed a mutation of the no-rows rule passing; after a scripted edit of a test, grep for each new assertion (done since), and keep running a mutation per new rule before trusting the tests.

**A check launched the debug build and Windows popped a ".NET is missing" dialog on the player's screen** (2026-10-26, while Harry was in game; closed at once).
- What: to check the resizable meter in a real window, the debug `ScarletMeter.exe` was started with `--demo`. It is framework-dependent, and the .NET 10 runtime here is the private SDK's, not installed for Windows, so the app host showed its download dialog over the game instead of the meter.
- Why: the builds and tests run with `SCARLET_DOTNET`/`DOTNET_ROOT` set in the shell, so nothing had launched the app host without them; the launch did not set `DOTNET_ROOT`, and the first look (a screenshot) did not show the dialog, only the window handle's class did.
- Guard: a launch of a debug build sets `DOTNET_ROOT` to the SDK's folder first (the scratch driver does), or uses the self-contained portable copy; before any launch while the player is in game, ask (memory), and check the started process's window is the meter (its class and title) before looking at it.

**A negative lookahead after `\s*` made a guard fail on clean code, and mutations looked caught** (#33, caught by a control before commit; the same slip in #30's shadow guard).
- What: a source guard for "no padding other than 0" read `padding:\s*(?!0[\s;])`. The regex engine lets `\s*` give back the space, so the lookahead sees " 0" and the pattern matches `padding: 0 18px`: the test failed on the unchanged stylesheet, and five mutations run against it reported "caught" for a test that failed regardless.
- Why: mutations were run before the changed test had passed on the clean tree, and a mutation run counts any failure as a catch.
- Guard: put the whitespace inside the lookahead (`:(?!\s*0[\s;])`); run a changed guard on the clean tree before mutating, and include a control (a change the guard must let through) in every mutation run, so a guard that always fails shows up as a failed control.

**A late history cleanup deleted a test's row** (CI's push run on #30's commit, found by #30's deep audit, 2026-10-25).
- What: `HistoryTests.AFightSavedWithAnUnusableStartOrEndShowsItsPlayersOnly` failed once in CI (5 fights expected, 4 listed) and passed on the pull-request run of the same commit. Upstream's history store starts its expired-session cleanup on the thread pool from its constructor; on a busy runner it started after the test had damaged fight 904 to end two hours after `DateTime.MinValue`, and deleted it as expired. The same race would have reached every history test from 2026-11-19, when the fights each of them dates 2026-10-20 pass the default 30-day retention (their later fights a day or more after).
- Why: the test host already knew about the cleanup, but only as something to wait for before deleting the test's directory; nobody asked what else it could do mid-test. Locally the cleanup always finished before a test wrote anything, so no run had shown it, and a fixed fixture date was not seen as something that ages into the retention period.
- Guard: `TestEngineHost` hands the history store out (to the test, the engine and everything else) only after its cleanup has finished; `TestEngineHostTests` holds the cleanup and fails if the store is handed out meanwhile (shown to fail with the wait removed, and with the whole hand-out removed). A probe delaying the cleanup failed the test on the old host and passes on the new one (WORKLOG). When a dependency starts background work from a constructor, ask what data it touches and let tests start only after it.

**A site that bars AI agents was fetched with a browser's user agent** (2026-10-25, found by the agent itself).
- What: asked whether the meter could read aion2timers.com's boss timers, the agent fetched the page; the first fetch got a holding page, and a second, sent with a browser's user agent, got the page (not kept or read). Only then did it read the site's robots.txt, which bars AI agents by name, enforced server-side, and disallows its `/api/` for every crawler.
- Why: the question was treated as a technical one (how is the data loaded?) before the site's own rules were read, and a block was treated as an obstacle rather than an answer.
- Guard: read a site's robots.txt and terms before fetching anything from it for the meter; never change a user agent or otherwise work around a block; a feature that shows another site does it as a window the player reads (`BossTimersWindow`), and automated checks of it never load the real site.

**A library "nobody used" was needed at run time, and only a launch showed it** (re-audit on #23, caught before merge).
- What: the re-audit found the exe bundles Microsoft's Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, from the versioned `net10.0-windows10.0.20348.0` target) under a licence known only by a URL, said nothing in the meter uses it, and suggested plain `net10.0-windows`. That build passed all 405 tests and its publish passed the layout check, but the published exe crashed on start: BlazorWebView draws the see-through overlay with WebView2's composition control, which loads the projection. The retarget was reverted.
- Why: "unused" was judged by searching our own code, not what our dependencies load; and no check before the visual one runs the published program, so a build that cannot start looked green. The first licence review had also listed only what the NuGet restore names, which is how the projection went unnoticed until the re-audit.
- Guard: `ThirdPartyNoticesTests.TheProgramKeepsTheWindowsSdkProjectionAndNamesIt` fails if the app loses its Windows 10 SDK target or the notices stop naming the projection and its licence; the target framework carries a comment saying why. Smoke-run the published exe (not only the Debug build) after any change to the target framework, packages or publish settings, and for a licence review list what the build output holds, not only the package graph.

**The first release named its licences but did not carry them** (deep audit on #23, fixed before any release).
- What: the planned v0.1.0 zip listed each component's licence by name, but binary copies must carry the texts and notices themselves (WebView2's BSD-style licence says so in as many words; MIT and Apache-2.0 too); .NET's notices were linked on `main`, not the shipped version; and "the source is the tag" left RATmeter's half of the source in someone else's repository, since GitHub's source archives drop submodules. The release job also ran restores, builds and tests with a write token kept in git's config, took the tag name into a script's text, and accepted a tag on any commit.
- Why: the review asked whether each licence is compatible with the GPL, not what each one requires of a binary copy; and the workflow was written as a build script, without asking what its token could do in a compromised build.
- Guard: a `licenses` folder in the zip (texts, every package's own files byte for byte, the runtime packs' notices from the packs used), checked by `ThirdPartyNoticesTests`, `Test-PublishLayout.ps1` and the release's zip check; `ScarletMeter-<version>-source.zip` from `Write-SourceArchive.ps1`, run in CI and attached to every release; a read-only build job and a write-only draft job, pinned actions, the tag through the environment and on `main`. For anything distributed, read each licence's conditions for binary copies, and give a workflow's write token to the smallest job that needs it.

**Guards were weaker than the words describing them** (deep audit on #23).
- What: the WORKLOG said the licence test fails "when `LICENSE` stops being the plain text", but it checked only the first and last lines, so a changed sentence in the middle passed; the package test matched id and version only, so a wrong licence passed; and the docs said the repository "has been public since 2026-10-25" while it was still private.
- Why: each guard was broken once, in the way it was written for (appended text, a dropped row), and the docs were written for the state the work was heading to.
- Guard: the GPL's SHA-256, licence cells compared with each package's .nuspec, an exact package set, and each guard broken in several places before it is described (WORKLOG lists them); planned states are written as planned ("to be made public once #23 merges") with a HANDOFF checklist, and updated when they happen.

**Every build was a new copy to unzip** (Harry asked, 2026-10-24).
- What: `Publish-Portable.ps1` wrote each build to a new stamped folder and zip and never replaced one, so the player had to unzip every update by hand and ended up with several folders, each with its own `ScarletMeter.exe`, not knowing which to click.
- Why: "never overwrite anything" was chosen as the safe default from the developer's side; the player's question, which exe to run, was not asked. Safety belonged around the player's data, not around program files.
- Guard: one live copy, `portable\ScarletMeter`, updated in place by renames that keep its `data` folder; earlier builds are removed; CI checks that one `ScarletMeter.exe` is left and a planted settings file survives. When a safety rule makes the player do more work, ask what it protects and protect only that.

**Remove-Item on a junction waits for a confirmation** (Test-PortableFolders.ps1, 2026-10-24).
- What: in Windows PowerShell 5.1, `Remove-Item -Force` on a junction asks to confirm, and a non-interactive run fails there.
- Guard: delete a junction itself with `[IO.Directory]::Delete(<path>, $false)`, which never follows it.

**UI checks opened windows over the player's game** (2026-10-24).
- What: back-to-back smoke runs (a screenshot tour, the share-bar checks) started the meter on Harry's desktop; its always-on-top overlay and the details, history and settings windows popped up and took focus while Harry was in AION 2, which got Harry killed in game.
- Why: smoke runs were treated as local and harmless, without asking whether the PC was in use.
- Guard: AGENTS.md "Review and merge" and the scarlet-smoke skill: ask before any check that opens windows or uses the mouse. Builds and tests without windows need no ask.

**A reused branch would have built old code** (Codex and the audit on #16).
- What: Update-Meter.ps1 reused an existing `update/upstream-<tag>` branch as it was, so a run after new commits on main would have zipped the branch's older source; and it read the current release from `upstream/`'s files, which a `git pull` leaves behind, then claimed a branch it had just deleted.
- Why: the first run was tested from a fresh state only; reuse and an out-of-date submodule were not.
- Guard: a reused branch must contain HEAD (`merge-base --is-ancestor`), the current release comes from `HEAD:upstream`, and the branch line is printed only when the branch exists. Test a script's second run and a `git pull`-ed checkout, not just the first run.

**A test harness cut a script short** (testing Update-Meter.ps1, 2026-10-24).
- What: piping the script's output through `Select-Object -First 20` stopped the pipeline after 20 matching lines, which stopped the script itself partway through the build, so the run looked like a script failure.
- Guard: never truncate a long-running command's pipeline; write its output to a log file (`*>&1 | Out-File`) and filter the log afterwards.

**Distribution was designed from the developer's side** (Harry asked for a portable build, 2026-10-24).
- What: the only way to get the meter was an installer into `%LocalAppData%\Programs`, though RATmeter, which players know, is a zip to unzip and run with its data beside it.
- Why: the update flow (build from source, keep a rollback) was designed first, and how a player expects to run a meter was not asked.
- Guard: `Publish-Portable.ps1` and the portable layout, checked by CI. Ask how the player wants to run a feature, starting from the tools they already use.

**A shareable zip carried the builder's account path** (audit on #15).
- What: the first portable zip had debug symbols (whose paths name the Windows account and checkout) and API documentation files.
- Why: the publish settings were copied from the local install, which is never handed on.
- Guard: the portable publish uses `DebugType=none` and no documentation files; before sharing any build, search its files for the account name.

**The README did not say what running needs or where the exe is** (Harry asked, 2026-10-24).
- What: one sentence mixed build and run requirements (it listed the .NET 10 SDK, which only building needs), and nothing said where the installed exe lives or how to start it.
- Why: the docs were written from the developer's side (build, test, publish) and never read as a player installing it.
- Guard: README "Requirements" (run versus build) and "Run it". Read user-facing docs as the player before calling a feature done.

**Codex's review comments were not read before merging** (PRs #4 to #10).
- What: Codex left eight findings; nobody read them, and two were still bugs on `main` after the merges.
- Why: the merge checklist had CI and the auditor, not the PR's comments.
- Guard: AGENTS.md "Review and merge" and the scarlet-ship skill: wait for Codex's review, read every comment, fix or answer each, record them in the WORKLOG.

**A review stuck at "Running" was taken as silence** (PRs #1 to #3).
- What: Codex never finished reviewing three PRs; their three findings surfaced only when the review was requested again.
- Guard: the same rule: a summary that never says Completed means no review happened; comment `@codex review`.

**A check that proved nothing** (this session, twice; again on 2026-10-26 for the event row).
- What: a "fails with the bug put back" run passed because the test edit had not been applied (the editor refused an unread file), and a mutation did not compile. On the event row, a `sed` meant for one `return null;` also hit another method's, the mutated build failed, and the runner went on to test the previous build, reporting the mutation as surviving.
- Guard: a fail-first check counts only when the output names the new test as failed. Read the output, not the exit code. A mutation run (the scratch scripts are not in the repository) must stop with "did not build, not run" when the mutated build reports an error, rather than test the last good build, and must show a test count above zero; scope a `sed` mutation to its method (an address range) when the line it matches is not unique.

**A wait loop matched the wrong commit** (#12).
- What: it waited for `b62ae8f` while the commit was `b62ae8b`, so it ran to its time limit.
- Guard: match on the full SHA from `git rev-parse`, never a retyped prefix.

**A merge re-staged an old submodule commit** (#4).
- What: `git add -A` after resolving a merge staged the worktree's stale `upstream` checkout over main's newer pin.
- Guard: after every merge, `git submodule update` before staging, and check `git ls-tree HEAD upstream` against `main`.

**An upstream release changed more than its notes said** (#7).
- What: v1.11.5.341's boss HP fix also rewrote how a summon's owner is read.
- Guard: the scarlet-upstream-update skill: read the diff, record every parsing change, add an in-game item for each.

**Shell quoting on this machine** (throughout).
- What: Windows PowerShell 5.1 splits native arguments at embedded double quotes (a commit message was lost, `gh --jq` filters broke); its double-quoted here-strings eat backticks; Bash heredocs above about 10 KB fail.
- Also (2026-10-26): Git Bash's `sed -i` writes a CRLF file back with LF line ends, so a later exact-text replacement written with CRLF found nothing (caught by the replacement's own not-found check); PowerShell names are case-insensitive, so a local `$tip` took over a `[switch]$Tip` parameter; and `R` is PowerShell's alias for `Invoke-History`, so a helper function named `R` never ran (aliases win over functions). In a double-quoted string `"$Version: ..."` is a drive-qualified variable and a parse error (`Get-ChangelogSection.ps1`, caught by its first run); write `${Version}`.
- Guard: commit with `git commit -F -` and a quoted Bash heredoc; run `gh --jq` from Bash; build Markdown-carrying scripts from single-quoted here-strings saved as UTF-8 with a BOM; write large files with the editor. All in the scarlet-ship and scarlet-smoke skills. Normalise line ends to CRLF after any `sed -i`, make every scripted replacement throw when its text is not found, and give script helpers and variables names no alias or parameter shares (`Swap-Text`, `$tipHtml`).

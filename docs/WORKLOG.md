# Work log

Newest first. Each entry: what changed, why, actual checks, unresolved issues, next action.

## 2026-10-27 — The roster shows each character's Kina

**What:** the character roster shows each character's Kina and Kina (Bound), stacked in one column, as its inventory last said, with the totals across characters in the header (Kina, Bound, the two together) and a "Most Kina" sort (ARCHITECTURE.md "Entry list and Odyle energy", "Kina"; "Character roster"). `Entries/InventoryRecords` reads the inventory's listing at world entry (`11 56`) and its changes (`1b 56`) in the layout SkeeveAN/Aion-DPS-Meter decodes (`Aion2Stock`, MIT): an entry found by its anchor, Kina item 930100031 and Kina (Bound) 930100001 (SkeeveAN's copy of the client's item table) in container 1. `EntryListTap` keeps the Kina slots and reports their sums with the counts (`CharacterProgress.Kina`, `KinaBalance`) by the same rule of whose they are; since the listing's place in a login is not documented, a whole list keeps a listing read within ten seconds before it. `RosterService` keeps the latest report, an item it does not know keeping its value; `RosterNormalizer` checks saved Kina. The roster window opens 920 px wide (the eighth column needed it). The demo has made-up Kina at login and gains some each fight.

**Why:** Harry, stopping the Kinah-gained-today work this change was going to be: "idc about kinah farmed for the day - i just care about the character's kinah current balance". A2Tools reads no balance; SkeeveAN's meter reads the whole inventory, Kina among it (its decoder: "also AP and Kinah"). The half-done gained-today work is kept in a git stash, not in any branch.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` Debug and Release 774/774. Fail-first: 16 mutations, each caught by its named test (the container, the kind, the quantity's top bytes, a gone slot, the listing's size, a missing item as none after a listing, the ten-second burst both ways, another character forgetting it, Kina waiting while the character is not sure and while unconfirmed, an unknown item keeping its value, an older report, saved Kina in range, the sort counting Kina (Bound)), with controls before and after passing; the first run missed "waiting while not sure", which got its own test (`KinaWaitsWhileTheCharacterIsNotSure`). A headless Edge render of a synthetic roster page with the real styles (no window): two Kina columns did not fit 860 px, so Kina (Bound) went under Kina and the window to 920 px, where it fits. Nothing has run with the game.

**Unresolved:** HANDOFF's Kina check: the ids and the layout are SkeeveAN's, not seen in Harry's game; where the listing falls in a login; whether Kina ever spans more than one slot.

**Review (PR #50):** Codex (P2) and the deep audit found the same bug: the ten-second rule kept the Kina when any inventory record, a change included, had been read just before a login's list, so a change of character A just before switching to B went to B (LESSONS). Fixed with the audit's other findings:
- **Major**: a whole list keeps the Kina only when a listing came within the burst.
- **Minor**: a listing while a character is named, with no self record just before it, waits for the next self record (a switch on the same connection).
- **Minor**: Kina read before anyone was named is forgotten at a new connection.
- **Minor**: Kina is one slot per item, so a move (a sort) is not counted twice; the audit suggested treating a sort as a listing, which needs the whole inventory's anchors, and one slot per item is simpler; HANDOFF asks for a sort check.
- **Nits**: the header says how many characters are not known ("?" when none is), from a pure `KinaTotals`; the column's tooltip says Engraved Kina is the game's Korean and Japanese name for Kina (Bound); the reader keeps every entry in order (its unused dedupe went); the writer's filler bytes are made up; ARCHITECTURE said four sorts.

Codex's P1 (no changelog line) was already met by 6d104e7; both threads answered.

**Checks after the review:** `scripts/Build.ps1` Debug and Release 779/779. Eight more mutations, each caught by its named test, with controls before and after passing: a whole list keeping only a listing, a listing waiting with no recent self record, a held listing kept for the next self record and not reported meanwhile, the self record's time noted, a new connection before anyone forgetting the Kina, a gone entry only in its own slot, the totals counting the unknown.

**Next:** Codex on the new head, CI, Harry's go-ahead to merge; then Harry's in-game checks.
## 2026-10-27 — The roster counts Artifact Corridor runs

**What:** the character roster has a Corridors column: the Artifact Corridors each character entered since the last Abyss Siege, as the meter saw them, and the header says when the next siege resets them (ARCHITECTURE.md "Character roster", "Corridors"). `RosterService.TakeMap` follows `ICombatEngine.MapChanged`: a change to the Artifact Corridor (`MapNames.ArtifactCorridor`, 503001, now a public constant) from another map is a run for the character being played, kept in `RosterCharacter.CorridorRuns` (the 12 most recent, saved in `roster.json`); none while the engine is not sure who is playing, and a load of the corridor straight after an unknown map within five minutes of the last run is the same run (a reconnection). `RechargeCalendar` has the Abyss Siege (`LastSiege`, `NextSiege`) from the world-event schedule; `RosterRules.CorridorRunsSinceSiege` counts the runs after the last one. `ICombatEngine.Character` is now the character only while that is sure (null after a login's list until its self record, and after a new connection until a self record names the character again), so a run is never put on a character the meter is unsure of. `RosterCharacter` compares its runs by value (a record compares lists by reference), guarded by a reflection test over every property; `RosterNormalizer` checks saved runs.

**Why:** Harry: "work on next feature please", the next in the order agreed for the dashboard (Odyle roster, then corridors, then Kinah): the simple version, counting the corridor's map loads the meter already reads, rather than A2Tools' passage timers and artifact holdings, which are only evidenced by A2Tools.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` Debug and Release 756/756. Fail-first: 14 mutations, each caught by its named test (a repeat load, a run while not sure, a reconnection, the runs kept, the map changes followed, runs after the siege only, a siege at the instant, equality over the runs and over the membership, saved runs' dates and order, the rows' count, the engine's character only while sure), with controls before and after passing. A headless Edge render of a synthetic roster page with the real styles (no window): the ninth column fits the default 860 px window. Nothing has run with the game.

**Unresolved:** HANDOFF's corridor check: whether the count matches the game, when corridors really reset, a second layer's corridor id, and how many corridors a side may run (not read; the page shows the runs alone).

**Review (PR #49):** the deep audit asked for changes:
- **Major (design)**: a corridor entry seen while the engine was not sure who was playing was dropped. A2Tools says the record naming the character comes at a zone load and every few minutes after, so it can follow the corridor's load, and a whole Abyss visit could go uncounted. Fixed: such an entry is held for the character the roster knew and counted, at its own time, if the game names that same character within ten minutes; another character (a switch) or none in time drops it.
- **Minor (also Codex P2)**: a saved run dated after now (a clock set back) made every entry straight after an unknown map read as the same run, and broke the order. Runs after now are dropped when a run is added.
- **Minor**: no test pinned two quick runs with a known map between them; one does now. The reconnection rule also needs the last known map to be the corridor (the audit's nit).
- **Minor**: the footer said only that a corridor entered with the meter closed is missed. It now names all three cases, with the confirmation window taken from the service, pinned by `RazorMarkupTests`.
- **Nits**: the normalizer called trimmed runs "unusable" and kept duplicates; they are now counted apart and removed. A character first seen through a corridor run now has its class and combat power. HANDOFF had "corridors and Kinah later" and a log line that changed; this entry lacked a blank line and claimed Debug only.

Codex reviewed 1e1e252 with a P1, no changelog line (added in 3bd78b9, editing #48's unreleased line), and the P2 above; both answered on their threads.

**Checks after the review:** `scripts/Build.ps1` Debug and Release 760/760. Nine more mutations, each caught by its named test, with controls before and after (59 tests): a held run only for the same character, only in time, held at all; runs after now dropped; a reconnection needing the corridor last known; a new character's class; saved runs once each; trimmed runs not called unusable; the footer naming the misses. Not mutated: the sort after adding a run (a held run is settled before any later run can be counted, so it only guards order) and a held reconnection dated before the last run.

**Next:** Codex on the new head, CI, Harry's go-ahead to merge; then Harry's in-game checks; Kinah next.

## 2026-10-27 — The character roster: Odyle energy, its refill, Nightmare and Ascension

**What:** a character roster (ARCHITECTURE.md "Character roster"): every character the player logs in on with the meter running, with Odyle energy (basic, +charged), when basic energy is full again, Nightmare and Ascension Trial entries left (+extra), last played, the one being played marked; estimates (≈) of the recharges since the game last said; sorts, a cap per character (560, 840 with a membership, automatic by default), removal. It opens from the overlay's Odyle energy and from Settings, Appearance, "Characters". Underneath:
- **Entry list** (ARCHITECTURE.md "Entry list and Odyle energy"): `Entries/EntryListRecords` reads the game's whole list of entry counts by entry id, as A2Tools now does (b79969a, 2149936, caa7ed4, unchanged up to b933531), replacing `Odyle/OdyleEnergyRecords`, which read the Odyle entry's id as "slot 1, key 87 93 03" (LESSONS). `Upstream/EntryListTap` replaces `OdyleEnergyTap`: it also takes the player's own character record (`33 36`), asks upstream's tracker who upstream read it as (`UpstreamMapping.ToIdentity`), and gives a login's counts to the character the next self record names (A2Tools' owner rule); `ICombatEngine.Character` and `ProgressObserved` are new, `OdyleEnergy` comes from the counts. `RecordTap` now takes upstream's pipeline in its constructor, so upstream has read each record before the tap hands it on, whatever is resolved first.
- **Storage**: `Storage/ProtectedJsonFile` is settings.json's protections (atomic writes, backup, damaged file kept aside, newer read-only, saving off when a write could destroy something), moved out of `SettingsStore`, which keeps every branch and message; `Roster/RosterStore` uses it for `roster.json` (`demo-roster.json` in demo mode), checked on load by `RosterNormalizer`.
- **Rules** (`Roster/RosterRules`, `RechargeCalendar`, pure): from A2Tools' Roster plugin (taengu/A2Tools-Plugins `plugins/alt-roster` at 7a4720b): +15 basic every three hours on the hours that meet the daily reset, caps 560/840; Nightmare (entry 4) +2 a day to 14; Ascension Trial (80000001) +3 a week to 3; the resets from the world-event schedule.
- **Live** (`Roster/RosterService`): follows `ProgressObserved` from before capture starts, keeps a count only when read later than the kept one, saves two seconds after the last change and at exit. **Page**: `Pages/RosterPage` (a tool window, `WindowKeys.Roster`), names through the "Hide names" mask.
- The demo sends a login list with Nightmare and Ascension and its user's own record, so its character shows (in `demo-roster.json`). THIRD-PARTY-NOTICES, SECURITY (the roster is the player's progress, never logged), README, HANDOFF (in-game checks).

**Why:** Harry, after a scoping of the whole dashboard (Odyle, its recharge, Kinah, Abyss corridors; a screenshot of another tool's Odyle dashboard): "Start with the Odyle roster here: the entry-list rework, the roster store, Odyle energy with its refill estimate, and the Nightmare/Ascension counts, as a first PR with the usual audit, Codex and CI. Corridors and Kinah later."

**Checks (SDK 10.0.402):** `scripts/Build.ps1` Debug and Release 740/740. New: `EntryListTests` (replacing `OdyleEnergyTests`; synthetic bytes in A2Tools' documented shapes, none of its samples' bytes; through upstream's real reassembly, upstream's own `PlayerInfoProcessor` naming the character from the writer's self record, plain and bundled), `RosterTests`, `RazorMarkupTests.TheRosterOpensFromTheOdyleEnergyAndMasksNames`; `MapProbeTests` moved to the new reader. Fail-first: 28 mutations on a scratch copy, each caught by its named test (the list's end, the update's tail, the id and value bounds, the Odyle by id and as the list's condition, the tap's bound, a list waiting for its character, another character dropping the counts, updates raised, the record tap after upstream's pipeline, only the user; the refill floor, the cap, weekly against daily, the full time, an older report, saving off, the save delay, the membership, the normalizer's dates, control characters and duplicates, the full ones first; a newer file read-only, a file not set aside not saved, settings recovered from the backup; the overlay's link), with controls before and after passing (203 tests). A second run's control first failed: the runner's restores kept the files' old times, so a build kept the last engine mutation compiled in (LESSONS); with the runner fixed, all 28 again, both controls passing. A headless Edge render of a synthetic roster page with the real styles (no window on the desktop) showed basic energy in cyan and a light sort box; both fixed and rendered again. Nothing has run with the game.

**Unresolved:** everything in HANDOFF "Next: the character roster": the ids, the order of a login's records (list, then the self record), the self record at server changes, and every recharge time come from A2Tools and are not seen in Harry's game. Abyss corridors and Kinah are later PRs, as asked.

**Review (PR #48):** the deep audit asked for changes. Findings and what was done:
- **Major**: a switch whose list and self record upstream lost would have put the new character's counts on the last one. A new connection now leaves the character unconfirmed and holds reports until a self record names it again (LESSONS).
- **Minor**: a kept count dated after now (a clock set back) would never be replaced; now any read replaces it, and `LastSeen` is the report's time.
- **Minor**: a tutorial character's placeholder name (`$` and a code, upstream dropping the `$`) became a character; now it names no one (`Entries/SelfRecords`, A2Tools' rule).
- **Minor**: no test covered a backup that cannot be opened with no main file; one now does for the roster (settings maps that branch to its own read-only notice whatever the helper says).
- **Nits**: an unknown cap name made the whole roster unreadable, so caps are now read by `Storage/LenientEnumConverter` and reset by the normalizer; the save wrote under the lock the capture thread's reports take, so it now writes a copy outside it, and `Dispose` stops taking changes before its last save; this entry lacked a blank line and the review record.
- **Accepted**: the settings refactor logs "could not keep settings.json.bak aside" before "settings.json could not be read" in eight unreadable-file cases (main had it after). The audit ran main's `SettingsStore` and this one over 121 file states: status, saving, notice, settings and the files on disk were the same in every one.
- **Residual**: the owner rule assumes a login's list comes before its self record, as A2Tools saw (HANDOFF).

Codex reviewed 039ab5f with one P1: the changelog had no roster line. It was added in c04dc45, right after the PR was opened, as AGENTS.md asks; answered on the thread.

**Checks after the review:** `scripts/Build.ps1` Debug 747/747. Ten more mutations, each caught by its named test, with controls before (84 tests) and after (130) passing:
- the new connection leaving the character unconfirmed;
- the updates held meanwhile;
- a self record confirming the character;
- a placeholder naming no one, its length rule, and no name flag;
- a count dated after now;
- a number, and an unknown name, as no cap;
- a backup that cannot be opened switching saving off.

**Next:** Codex on the new head, CI, Harry's go-ahead to merge; then Harry's in-game checks.

## 2026-10-27 — The Artifact Corridor named, and not paused

**What:** `Maps/MapNames` names map 503001 "Artifact Corridor", so Settings, About shows "Artifact Corridor (map 503001)" there instead of "Map 503001 (no name known)". `Maps/AbyssMaps` is unchanged: the corridor is not one of the Abyss's maps for the pause, now said in its comment and pinned by a test.

**Why:** the meter's log in Harry's game on 2026-10-26 had "Map loaded: 503001" at 23:47:26 (+11:00), between "Map loaded: 20" at 23:47:02 and again at 23:49:49, on the same connection. Asked, Harry said it is "an 'artifact corridor'. you spend 2 mins killing mobs inside there for abyss points", and then: "add \"Artifact Corridor\" as map 503001's name ... Don't pause the meter there." The pause is for the Abyss's crowds; the corridor is small and short, its load ends the pause, and the load of 20 on the way out starts it again. No source lists the id or the name, so the table's comment, ARCHITECTURE ("Map probe", "Abyss pause"), THIRD-PARTY-NOTICES ("Data") and SECURITY ("Provenance") say this one comes from the player's own game.

**Checks (private SDK 10.0.402, worktree `smwt\corridor` on branch `artifact-corridor` from main 460805e):** fail-first: with the tests changed and the table not yet, `MapProbeTests.KnownMapsHaveTheirNames(503001, "Artifact Corridor")` and `TheNameTableHoldsTheSourcesWhole` (now seven maps below 600000 and 67 in all) failed by name, 74 tests run; with the name, 74/74. `AbyssPauseTests.TheAbyssIsMaps20And22` gains 503001 as not the Abyss: it passes as it must, and with 503001 added to `AbyssMaps` (a mutation, restored) it failed by name. `scripts/Build.ps1` 718/718 in Debug and in Release (main's 716 plus the two new cases); Release's five compiler warnings are all in upstream's code.

**Not checked:** a build with the name in the game (HANDOFF's Abyss item says what to look at).

**Audit (independent reviewer, standard pass, on 5d3ff9f): approve with nits**, all three applied. It traced the pause in the code (`MapLoadTap` raises a change for any new id on the connection, `AbyssPause.Update` re-decides under its gate: 20 → 503001 tracks again, 503001 → 20 saves the open fight and pauses), checked the counts (seven below 600000, 60 dungeons, 67), the changelog line and that nothing personal is in the diff, commits or PR text, and ran `MapProbeTests`, `AbyssPauseTests` and `ChangelogTests` (79/79). (1) ARCHITECTURE's test list still described the whole table without the corridor: updated. (2) In ARCHITECTURE's "Abyss pause", the corridor's sentence left "It ends when…" reading as the corridor: the pause's end now comes first and names the pause, and the corridor's sentence says the same connection is what Harry's game showed, a new connection with a lost map record being the documented safe side. (3) The behaviour was pinned only at `AbyssMaps.Contains`: `AbyssPauseTests.TheArtifactCorridorTracksAndTheAbyssPausesAgainAfterIt` now loads 20, 503001 and 20 on one connection through upstream's reassembly (paused, tracking with the map 503001, paused again); it passes, and with 503001 added to `AbyssMaps` (a mutation, restored) it and `TheAbyssIsMaps20And22` fail by name. `AbyssPauseTests` 16/16.

**Codex:** on aa63241 one P1, "Add the player-visible map name to Unreleased": valid for that commit, already done in 5d3ff9f (the line was added once the PR had its number, as AGENTS.md's Changelog rule says); answered on the thread. On 4b79d5d, after `@codex review`: completed, no findings (a thumbs-up). CI passed on 5d3ff9f (both runs).

**Next:** CI and Codex on this commit, then the squash merge pinned to it (Harry, 2026-10-27: "merge #47 ... once CI passes on the head and Codex's fresh review has no new findings").

## 2026-10-26 — v0.3.0 released

**What:** PR #45 merged as f22f4db (standard audit: approve with nits, fixed in 454336d; Codex found nothing on 1a48498 or 454336d; CI passed on both). `v0.3.0` tagged on it; the push went through as the repository's admin, which the "Release tags" ruleset allows and GitHub records as a bypass. The release workflow (run 40306761355) built the draft: `ScarletMeter-0.3.0-win-x64.zip`, `ScarletMeter-0.3.0-source.zip` and their `.sha256`, its notes ending with "What's changed in 0.3.0" from the changelog. Checked as DEVELOPMENT.md "Releases" asks: both checksums match; the program zip has the exe, the `licenses` folder (17 files) and the notices, the source zip RATmeter's `upstream/` (259 files); the 552 files of both zips, searched as bytes in UTF-8 and UTF-16, hold no home path, machine name or e-mail address of the player (the addresses in them are third-party licence authors, upstream's README contact and the tests' made-up ones). The account name appears only as Harry's first name in docs and code comments that quote requests, never in a path, as in v0.2.0's files and the public repository. Published as a pre-release on 2026-10-26 at 21:38 local time, the changelog's date: https://github.com/harrynguon/Scarlet-Meter/releases/tag/v0.3.0.

**Why:** Harry: "let's publish this as a release?"

**Next:** the test host's cleanup timeout in a PR of its own (HANDOFF.md, "After acceptance"); the in-game checks in HANDOFF; the next release when there is something to ship.

## 2026-10-26 — v0.3.0 prepared

**What:** `<Version>` is 0.3.0 (`src/Directory.Build.props`). `CHANGELOG.md`'s Unreleased lines are now `## [0.3.0] - 2026-10-26` (nine changes: #34 to #44), under a new, empty Unreleased, with the compare links moved on. `docs/release-notes/v0.3.0.md` has the download steps (updating from 0.2.0: move the `data` folder over), a few highlights and the risk and credit notes; the release workflow adds the 0.3.0 section after them, the first release to get it. No package or project file changed since v0.2.0, so the third-party notices need no rewrite (`ThirdPartyNoticesTests` pass); RATmeter stays at v1.11.5.341.

**Why:** Harry: "let's publish this as a release?"

**Checks (SDK 10.0.402):** `scripts/Build.ps1` Debug and Release 716/716. The first Debug run failed one test, `DemoModeTests.AFailingEventIsLoggedOnceAndTheFeedCarriesOn`, on `TestEngineHost`'s 10 s `CleanupTimeout`: resolving the engine resolves the history store, which the host hands out only once upstream's startup cleanup reports finishing, and on a busy machine that cleanup can start seconds late (LESSONS, "A late history cleanup"; #31). It passed on the rerun, with the whole suite, in both configurations. The cause is the known one; the symptom is new (no earlier run recorded the timeout firing), and any test that resolves the store can hit it, so it is a follow-up in HANDOFF.md. `Get-ChangelogSection.ps1 -Version 0.3.0` prints the nine changes; `Test-ChangelogSection.ps1` passes. Standard audit (PR #45): approve with nits; the release notes now credit the Odyle energy record layout as well as the map's, and the flake is tracked.

**Unresolved:** the test host's cleanup timeout (above).

**Next:** PR, standard audit (the version), Codex, CI, merge; tag `v0.3.0` on the merged commit; check the draft's zips (checksums, no account name, home path or e-mail address in their files); publish it as a pre-release. Then a PR of its own for the cleanup timeout (HANDOFF.md, "After acceptance").

## 2026-10-26 — No sliver of the next bar below the fifth

**What:** when the bar area scrolls (more than five players), its bottom edge is cut off rather than faded: `.rows.scrolls`' mask is a hard stop 8 px above the list's bottom (its 6 px padding and the last whole row's 3 px gap, less the 1 px of the player's own ring, a `box-shadow` drawn outside the row), so nothing of the next bar peeks out below the fifth, under the player's own pinned bar included, while that bar's ring stays whole. The thin scrollbar is the sign that more follow. `RazorMarkupTests.TheBarAreaScrollsWhenItCannotShowEveryRow` pins the hard stop and its width from the padding, the gap and the ring (with effects on too); ARCHITECTURE and HANDOFF say so, and CHANGELOG's bar-area line under Unreleased says it ends at the fifth bar, with this PR's link.

**Why:** Harry, with two in-game screenshots of a raid: "when dps bars surpasses five players, you can get a glimpse of the actual 5th/6th(?) player behind my player ... is this intentional? ... or would it be cleaner to hide this mini section of their dps bar". It was #35's scroll hint (the bottom 9 px faded, the next row's top showing), but with the player's own bar pinned in the last row's place it showed another player's numbers under it; asked, Harry chose to hide it and keep the scrollbar as the hint.

**Checks (SDK 10.0.402):** `dotnet build` and `dotnet test` 716/716. Headless Edge, the stylesheet of main against this branch, twenty synthetic players, the player 4th and 9th (pinned): main shows the next bar's top below the fifth in both; the branch shows nothing below it. The first cut, 9 px, clipped the bottom line of the pinned bar's gold ring (seen zoomed in the render); 8 px keeps it, the next bar still 2 px below the cut (LESSONS). Fail-first against a scratch copy, control clean: the fade put back, a 9 px cut (clipping the ring) and a 5 px one (showing the next bar) each fail the test.

**Not checked:** in game (the row size other than 1 scales the cut with the rows, by the list's zoom; not seen).

**Audit (scarlet-auditor, standard, on 6691224): approve with nits**, all applied. It checked the geometry against the code and rendered the branch headless at row sizes 0.75, 1 and 1.5, display scales 1, 1.25 and 1.5, effects on and off, the own row pinned and ranked 3rd, and scrolled to the end: the ring whole and nothing of the next row below the fifth; main's stylesheet reproduced Harry's sliver. (1) The docs claimed nothing peeks out below the last whole row at any scroll: only at a whole-row position (scrolled mid-row, or on a screen too short for five rows, a part row shows, cut off sharply), and nothing ever under the pinned own row; the stylesheet's comment and ARCHITECTURE say so now. (2) The test's comment put the limit at the gap; it is the bottom padding. (3) LESSONS' newest entries ran into each other in Markdown: blank lines restored. (4) The ring check read two of the own row's three ring rules: it reads every rule that rings `.row.me` now, and a theme's ring made 2 px fails it. Not taken: the top edge's counterpart (1 px of the row beneath can show above the own row pinned at the top after a mid-row scroll), which predates this PR. CI passed on 965878c and 6691224; Codex's first review was still running.

**Next:** Codex on the fixes, CI, merge (Harry's go-ahead), then the portable copy.

## 2026-10-26 — Map probe and the Abyss pause

**What:** the engine reads the game's map-loaded record (`21 36`) and keeps the id of the map last loaded: `ICombatEngine.MapId` (null until a load) and `MapChanged`, kept across `Stop` and `Reset`. Each change is logged once at Information as "Map loaded: <id>", the number alone, and Settings, About shows it in a quiet "Map id" line (the id, or "None yet"; later "Map", with the name, below). Nothing acts on it. The record's layout is A2Tools' (`parse_map_load_packet`, taengu/A2Tools-DPS-Meter b1fcd53, 2026-10-05, read at today's head 6abf5e7, v2.0.79): `<length varint> 21 36 <u32> <map id u32> …`, both little-endian; `Maps/MapLoadRecords` reads the id only, and refuses 0 and ids past `int.MaxValue` (Scarlet's check). To read it beside upstream's pipeline without opening every LZ4 bundle a third time, the Odyle tap's listening is split out: `Upstream/RecordTap` subscribes to upstream's reassembly once, opens each bundle once and hands each record to every `IRecordReader` that takes its opcode; `OdyleEnergyTap` and the new `MapLoadTap` are its readers, and `Records/GameRecords` holds the framing and varint reader `OdyleEnergyRecords` had to itself. A reader that throws no longer stops the record reaching the others, nor the rest of a bundle. The tests' record helpers moved to `SyntheticRecords` and a shared `RecordingLogger`.

**Why:** Harry asked for a way to switch the meter's tracking off in the Abyss, where hundreds of players fight and upstream keeps every hit of everyone it sees, so the meter costs more as the fight goes on. Nobody knows the Abyss's map ids, and a wrong id would quietly switch the meter off in a dungeon, so this first build only tells Harry the id at each load; the switch waits for ids read off in the game (HANDOFF, "Next: the map probe").

**Source checked:** A2Tools read through GitHub's API (GitHub's robots.txt disallows its raw, tree, archive and `.git` paths for AI agents and points to the API). `parse_map_load_packet` was added in b1fcd53 (merged with ca078ee) and is unchanged since: of the commits touching `stream_processor.rs` from 2026-10-04 to the head, only b1fcd53 changed it; the others the search matched (c9a8c84, 4220243, 9300bc5) add code beside its call or elsewhere in the file (LESSONS, "A parser was ported from a merged PR without the source's later fixes"). A2Tools uses the id to file fights under a dungeon (dungeons 600000 to 699999, an open-world list from the game's Map table). Its world-map boss list (`01 91`) names the Abyss map 20 and its artifact record a second layer 22; whether `21 36` sends those ids is not known, and nothing here assumes it. THIRD-PARTY-NOTICES pins both commits; SECURITY's provenance now names both A2Tools ports (Odyle's was missing).

**Checks (private SDK 10.0.402, worktree `smwt\mapprobe` on branch `map-probe` from main 9524681):** `scripts/Build.ps1` 639/639 in Debug and in Release (main's 611 plus 28); Release's five compiler warnings are all in upstream's code, none in Scarlet's. New: `MapProbeTests` (27 with its cases) and `RazorMarkupTests.TheAboutTabShowsTheMapIdAndFollowsIt`; `OdyleEnergyTests` now builds the tap from `RecordTap` and `OdyleEnergyTap`, its cases unchanged. Fixtures are synthetic: the documented shape (length 52, `21 36`, the count, the id, 38 more bytes) with made-up ids and made-up filler, none of A2Tools' sample bytes. Fail-first (scratch runner, xunit's `-class` over `MapProbeTests`, `OdyleEnergyTests`, `RazorMarkupTests`, `EngineRegistrationTests` and `DemoModeTests`, 123 tests per run; a mutation that does not build is reported as not run): two controls (the id sliced as a range, the same-map check written as a pattern) passed; 25 mutations each failed the tests named for them: the id read from the count, any opcode read, the opcode's bytes swapped, no id check, 0 accepted, the whole documented record required (a plausible tightening), the id read big-endian, every load logged, the record's length in the log line, the map reader taking nothing, a map subscriber's failure rethrown, a failing reader stopping the record, a failing reader stopping the bundle, a bundle opened twice, the failure flag never cleared, either reader left unregistered, `MapId` always null, `MapChanged` dropping handlers, a varint past 32 bits wrapping, an opcode read past a short record, a reader subscribing to the stream itself, Settings keeping its handler after `Dispose`, Settings formatting the id by culture, the Odyle reader taking only the list. Two tests were tightened before the run, when reading them showed a mutation would pass: the tap's "works again" line now has to come once over two clean records and a failure has to warn again after it, and a failing map subscriber has to be logged as such (the tap's own containment would otherwise hide a missing catch). The subscriber mutation first failed to compile (an unused variable, warnings being errors) and counted for nothing; rerun in a form that builds, it failed its test.

**Then, from Harry:** asked whether someone had done this already, whether there was an easier way to know he is in the Abyss, and what happens if the ids change. Found: yykk62/AetherDPS (GPL-3.0, another meter on RATmeter) reads the same record and counts 20, 21 and 22 as the Abyss (its `dungeon_categories.json`, no source given), and its field-boss builder gives the game's internal map names their ids (World_L_A 1010, World_L_B 1011, World_D_A 1110, World_D_B 1111, Abyss_Reshanta_A 20, Abyss_Reshanta_C 22); A2Tools' world-map record uses 1010 for Verteron, the id its capture shows the map-loaded record sending there, and 20 and 22 for the Abyss. Neither switches tracking off (AetherDPS pauses recording by editing its own copy of RATmeter). Then Harry: "i quite like seeing the map id (and add name in too?) if you click settings". The About line is now "Map", with the name and the id ("Fire Temple (map 600021)"), the id alone where no name is known (`Formatting/MapText`), from `Maps/MapNames`: SkeeveAN/Aion-DPS-Meter's names (MIT) for the six open-world and Abyss maps by AetherDPS's ids, and A2Tools' 60 dungeons with their difficulty. Cities and most of the open world have no source and show the id alone. The log keeps the number alone. THIRD-PARTY-NOTICES ("Data"), `licenses/MIT.txt` (SkeeveTV) and SECURITY's provenance name the sources at pinned commits. Checks: `scripts/Build.ps1` 665/665 in Debug and in Release (26 new cases in `MapProbeTests`: each source's names, none for 21 or unnamed maps, the whole table, the About line; `RazorMarkupTests.TheAboutTabShowsTheMapAndFollowsIt` now pins `MapText`). The 60 dungeon lines were generated from A2Tools' file by a script, not typed. Fail-first (the same scratch runner, 149 tests per run): a control (the line built by concatenation) passed; the Abyss's name moved to 21, a dungeon line dropped, no lookup, the bare number for an unknown map, a named map without its id, the id with separators, the page showing the id alone and a name with a stray space each failed the tests named for them.

**Test copy:** at Harry's request, a separate portable copy built from this working tree with `Publish-Portable.ps1` into the worktree's own `portable\ScarletMeter` (git-ignored; its own `data` folder), Release tests 665/665 and the layout check passed; Harry's usual copy in the main checkout was not touched.

**In the game (Harry, 2026-10-26, the test copy; read from its log, which holds no names):** started in a dungeon, Harry left for Verteron and About still said "None yet". The log showed the capture locking at 16:53:08, a second "Packet Stream SYNCHRONIZED" at 16:54:52 (a new connection, no DESYNC warning) and no map record. Upstream's reassembly keeps each connection apart by the client's port and reads a new one only from its first keep-alive on, dropping what comes before, so the map record that opens a connection is often lost. In Verteron, loads on the same connection were read (1010, the world layers 101014 and 101020). Entering the Abyss: a new connection at 17:05:56 and "Map loaded: 20" at 17:05:57, so the Abyss's lower layer is 20, as the other meters' data said. Leaving: a new connection at 17:07:21 and no map record, and About still showed the Abyss (LESSONS, "The map probe kept a map after the game changed server"). Fix: the record tap tells its readers when it first reads a record of a connection it has not seen lately (`IRecordReader.ConnectionStarted`; it remembers 16), and the map reader forgets its map there ("Not known" in About, `MapChanged` with null, logged once); Odyle energy is kept. `MapProbeTests.UpstreamDropsTheMapRecordThatOpensANewConnection` pins upstream's loss.

**The Abyss pause:** Harry then asked for a status "like where it says 'Combat'" saying the meter is disabled in the Abyss. Asked whether boss fights should still count there, Harry: bosses only if that would not still lag, else fully off; with hundreds of players on one world boss every hit is a boss hit, so it would, and the pause tracks nothing. `Upstream/AbyssPause`: while `EngineSettings.PauseInAbyss` (Settings, Tracking, "Pause in the Abyss", on by default) is on and the last map read is 20 or 22 (`Maps/AbyssMaps`; 21 left out until seen), upstream's boss-only filter is forced on through the settings bridge (`PauseState`, so the bridge does not depend on the pause), the fight is cleared on entering, on leaving and every 2 seconds (upstream's reset completes and saves nothing), the engine's snapshot is empty and details null (`ICombatEngine.PausedInAbyss`), and the header reads "In the Abyss · meter paused". Leaving the Abyss is a server change, so the map is forgotten and the pause ends; if the map record entering is missed, the meter tracks there as before. Capture keeps running (only the record tap can tell the meter it has left), so upstream still reads every packet. Harry also asked whether the bars could stay with only his own damage: not cheaply, since upstream files every hit before Scarlet could pick his out, so picking them out means letting upstream keep everyone's (the lag) or decoding damage beside it; and which player is you after a server change comes from the same first records of a connection; noted as a possible later project. While building the pause, a test was found to check only a flag rather than the timer, so the pause takes its interval from an internal constructor and `TheTimerSweepsWhilePausedAndStopsAfter` watches the real timer; a sweep already under way as the pause ends can clear once more (in the game a moment of hits right after leaving), which the test waits out. Checks: `scripts/Build.ps1` 685/685 in Debug and in Release (new: `AbyssPauseTests`, the new-connection cases in `MapProbeTests`, `SettingsTests.AFileFromBeforeTheAbyssPauseLoadsWithItOn` and the engine receiving the switch, `RazorMarkupTests.TheHeaderSaysWhenTheMeterIsPausedInTheAbyss`). Fail-first (the scratch runner, now also over `AbyssPauseTests` and `SettingsTests`, 154 then 213 tests per run): controls passed (a no-map check by `HasValue`, the Abyss ids written as comparisons); each of these failed the tests named for it: new connections never noted, the map kept at a new connection, every record a new connection, no bound on the connections remembered, Odyle energy forgotten at a new connection, the connection noted after its first record, a failure at a new connection not logged, a reader failing there stopping the others; 21 counted as the Abyss, boss-only not forced, no clear on entering or leaving, the sweep timer never started, the sweep clearing when not paused, the snapshot or the details shown while paused, the setting ignored or its changes not followed, the settings service not applying it, off by default in the app or the engine, the header never saying it, the pause logged without its map, and a disposed pause still following. Three first runs proved nothing and were redone: one refused its mutation because a file had been rewritten with LF line ends (the runner matches exact text), and two mutations did not compile (unreachable code) and were rewritten so they build. The disposed-pause mutation first survived, since disposal also unsubscribes: a direct update after disposal now has its test.

**Not checked:** a meter with the fix and the pause in the game: leaving the Abyss or a dungeon showing "Not known", the pause starting and ending, and how the game feels in an Abyss fight with it. A running meter's header and About line have not been seen in a window (none was opened: Harry may be in game). The cost of asking each reader about every record on a crowded stream is not measured (two interface calls per record, beside a bundle decompression that was already there).

**Merged `main` (#38 and #41, 17f444a) into this branch:** conflicts in `UpstreamCombatEngine` (both features add to the constructor and `GetSnapshot`: the pause is checked first, inside the dummy filter's snapshot lock, and also clears what was shown, so nothing from before the Abyss is held over it), the ARCHITECTURE file table's Upstream row (both descriptions merged), README's comparison table, LESSONS and this log (both entries kept, newest first). `scripts/Build.ps1` 714/714 in Debug and in Release; the `upstream` pin unchanged (aa3ffc6 on both sides).

**Review (main session, 2026-10-26):** CI passed on 5e265c0. Codex's first review stuck at "Running" on 0d8a135; a fresh `@codex review` answered on 5e265c0 with one P2 on `AbyssPause.cs`: "Discard paused boss sessions before shutdown persistence", valid, fixed below. **Deep audit (scarlet-auditor, on 5e265c0): request changes**, all applied. (1) Major: entering the Abyss reset the fight without saving it, and upstream saves a fight only at a later hit, a new try or exit, so a boss killed just before going to the Abyss never reached History (the audit's scratch test confirmed it): entering now completes and saves the open fights first, as at exit. (2) Closing the meter while paused saved the boss hits since the last sweep (Codex's P2): the engine drops the open fight first when paused. (3) History listed, and could open (copying every hit of a crowd's fight under the session lock), the running Abyss boss fight until the next sweep: while paused `GetHistory` leaves out every running fight and `GetHistoryFight` opens none. (4) `MapNames`' comment said 20 was not yet seen; it was. (5) A sweep checked the pause under its lock but cleared outside it, so one under way could clear the next place's first hits: the state changes, saves and clears are all under the lock now, upstream's session lock taken inside it and never the other way round. Tests: `AbyssPauseTests` checks the fight from before saved on entering, nothing of the Abyss saved by a sweep, on leaving or at exit (`ClosingTheMeterWhilePausedSavesNothingFromTheAbyss`, with the same hits outside the Abyss saved), and History listing and opening no running fight while paused; `dotnet test` 715/715. Fail-first against a scratch copy, control clean: entering without saving, closing without dropping, History listing or opening the running fight each fail their test; the sweep's lock is a race no test pins deterministically, so the code comment states the lock order. The audit found the parsing bounded, the Odyle readings unchanged, each bundle opened once, the settings default and old files right, the upstream boundary and the provenance claims sound.
**After the fixes:** Codex's review of c3ee75f found nothing (its P2 thread answered and resolved); CI passed on c3ee75f. Main merged in after #43 (Boss encounters only on by default), keeping both sides: README's fight options and the Abyss bullet, ARCHITECTURE's `Fight` list with the pause, these entries newest first, and in `SettingsTests` #42's file-before-the-pause test beside #43's default test, the engine test turning both boss-only and the pause off.

**Next:** CI on the merge, then merge (Harry's go-ahead, 2026-10-26: "sort out all PRs and all that into main"); then the in-game checks in HANDOFF.

## 2026-10-26 — Boss encounters only, on by default

**What:** "Boss encounters only" (Settings, Tracking; `FightSettings.BossOnlyCapture`) is on by default, so a new install tracks boss fights (and training dummies, which upstream counts as bosses) and not every mob. A settings file that saved the switch keeps its value, on or off, since an older file's "off" may be the player's own choice; a file whose fight section never named it gets the new default. The setting's description says so, and CHANGELOG.md has its line under Unreleased. The engine's own `EngineSettings` default is unchanged: the app's settings are applied to it on load.

**Why:** Harry: "ensure 'boss fights only' is set to on by default".

**Checks (SDK 10.0.402):** `dotnet build` and `dotnet test` 641/641, with `SettingsTests.BossEncountersOnlyIsOnByDefaultAndASavedChoiceIsKept` new and three settings tests reworded from "turned on" to "turned off". Fail-first against a scratch copy, control clean: the old default fails the new test and the engine test; a normalizer that forces it on over a saved "off" fails it and three round-trip tests.

**Not checked:** in game. A player whose settings file saved "off" (every file saved by an earlier build) keeps it off until they turn it on in Settings, Tracking.

**Codex on 2ecbad4:** one P1, the missing CHANGELOG line: added in 75564f7 (answered on the thread).

**Audit (scarlet-auditor, standard, on 75564f7): approve with nits**, all applied. (1) The Codex thread: answered, and a fresh review asked for. (2) Upstream's boss test is the mob code alone (`Mob.IsBoss`; its HP-threshold fallback is commented out), so with the switch on a boss the meter never saw appear (started mid-fight: no spawn record, code 0) or one its list lacks (new after a game patch) records nothing: confirmed in upstream's code, not in game. The setting's description and the README say so and to turn it off for such a boss, ARCHITECTURE names it as a fix to offer upstream, and HANDOFF has an item to check it in game. (3) Two HANDOFF checks that need an add's hits now say to turn the switch off for them. (4) The CHANGELOG line says an existing player keeps it as they had it, off unless they turned it on. (5) `BossEncountersOnlyIsOnByDefaultAndASavedChoiceIsKept` also checks that a saved "off" reaches the engine on load. The audit found no conflict in behaviour with #42's Abyss pause (its bridge combines the player's value with the pause and never writes the player's), only text conflicts to resolve when #42 merges first.

**After the fixes (e90bc00):** Codex's fresh review: no findings; its earlier P1 answered on the thread. CI passed on both runs.

**Next:** merge (Harry's go-ahead, 2026-10-26: "sort out all PRs and all that into main"), ahead of #42, which was still in review.

## 2026-10-26 — A changelog, and each release lists what changed

**What:** `CHANGELOG.md` at the root records what changed for players, release by release (Keep a Changelog: Added, Changed, Deprecated, Removed, Fixed, Security, each change with its PR's link), newest first: `## [Unreleased]` (Odyle energy #34/#36, the event icons #39, the self-sizing bar area #35/#37/#40, other players' dummy fights ignored #38), then 0.2.0 and 0.1.0 rebuilt from their release notes and merged PRs, then compare links. `scripts/Get-ChangelogSection.ps1` prints one version's section (absolute paths from its own location; throws for a missing section, a heading without a date, or one listing nothing). The release workflow runs it in its first check, so a tag without its section fails before building, and writes the draft's notes as the notes file followed by "What's changed in <version>" and the section, with a link to the changelog at that tag. CI runs it for the app's version (`src/Directory.Build.props`), so a version bump without its section fails on the PR. AGENTS.md gains "Changelog": every PR a player would notice adds its Unreleased line in the same PR, developer-only changes stay here; the scarlet-ship skill, DEVELOPMENT ("CI", "Releases"), README, docs/README and ARCHITECTURE ("Tests") say so. `ChangelogTests` guards the shape: Unreleased first and undated, releases newest first with dates, releases matching `docs/release-notes`, the app's version present once its notes exist, groups and PR links, a link per heading, and the workflows' use of the script.

**Why:** Harry: "from now on, let's keep a track of a changelog. so each release lists what has changed. and maybe one overall changelog file for documentation purposes too". The release notes had a hand-written "What's new" per release but nothing recorded changes between releases or across them.

**Checks (SDK 10.0.402):** `dotnet build` and `dotnet test` 616/616 (five new). The script, run by hand: 0.1.0 and 0.2.0 print their sections (28 lines for 0.2.0); 9.9.9 throws "no section"; "Unreleased" is refused by the parameter's pattern; on a synthetic changelog in a scratch folder, a dateless heading and an empty section each throw (its first run failed to parse, `$Version:` in a double-quoted string, fixed; LESSONS, "Shell quoting"). Fail-first against a scratch copy, control clean: 10 mutations each fail `ChangelogTests` (Unreleased renamed, a release undated, dates out of order, a release without notes, a change without its PR link, an unknown group, a wrong compare link, the release body back to the bare notes, the workflow's early refusal gone, CI's check gone).

**Not checked:** the release workflow and the CI step run only on GitHub (the CI step on this PR; the release steps at the next tag). PR #38 (training dummies) merged first, so its Unreleased line is added here, with main merged in (the WORKLOG's two new entries kept, newest first).

**After the merge of main (#38):** `scripts/Build.ps1` Debug and Release 640/640; CI on 3e0571c passed (its new step: "CHANGELOG.md has 0.2.0, 9 changes"); Codex reviewed 3e0571c with no findings.

**Audit (scarlet-auditor, standard, on 3e0571c): approve with nits**, all applied. (1) 0.1.0 was dated by GitHub's UTC publish time (2026-10-24) where the WORKLOG and HANDOFF use the local day (2026-10-25): now 2026-10-25, and DEVELOPMENT says the dates are the local day. (2) `ChangelogTests` passed a bullet placed before a section's first group, and did not tie a link's `#N` to its `/pull/N`: a section must now start with a group, and a mismatched number fails. (3) The script let a reference link inside a section end it early, without a refusal, and its refusals had been run only by hand: only the closing block of links ends the last section now, the heading's date must be a real date and a trailing space is ignored, and `scripts/Test-ChangelogSection.ps1` (nine cases, CI runs it under pwsh) covers it; against the script as it was, it fails four cases (the inner reference, the trailing space, the impossible date, and the old wording for a dateless heading). `ChangelogTests` also checks that the release workflow's refusal comes before its build. Fail-first, control clean: a bullet before a section's first group, a link to another PR's number, the refusal moved after the build, and CI without the script cases each fail `ChangelogTests`; `scripts/Build.ps1` 640/640. (4) This entry now records the post-merge checks. (5) The release workflow's rewrapped comment is reflowed. The audit found the release workflow's security unchanged (permissions per job, no text evaluated by a shell, the draft job only receiving files) and the changelog's statements accurate against the tags and PRs.

**After the fixes (12f77db):** Codex's fresh review (requested with `@codex review`): no findings, no inline comments or reviews. CI passed on both runs (push and pull request), its new steps printing "CHANGELOG.md has 0.2.0, 9 changes" and, under pwsh, "All changelog cases passed"; tests 640/640.

**Next:** merge with Harry's go-ahead.

## 2026-10-26 — Other players' training-dummy fights ignored

**What:** a training-dummy fight the player has not hit is no longer shown, listed or saved, once the meter knows the player's character (ARCHITECTURE.md "Training dummies"). `DummyFightRule` (pure) decides; `DummyFightFilter` reads upstream's facts (the target's `IsDummy`, the players' `IsUser`, whether any tracked player is the user, latched once true). `DummyFightHistoryStore` wraps upstream's history store, registered in its place by `AddScarletEngine`, and does not save such a fight. `UpstreamCombatEngine`: while such a fight is upstream's active one, `GetSnapshot` returns the snapshot it returned last (a stranger's fight shown before the user was known is dropped instead), `Reset` clears it, and `GetPlayerDetails` returns null; `GetHistory` leaves such a running fight out (upstream's page when there is none, otherwise upstream's merge redone without it, `PageWithout`) and `GetHistoryFight` does not open it. The contract's doc comments, README (How it works, the comparison, a Q&A) and HANDOFF (an in-game item; the upstream candidate) say so.

**Why:** Harry: "if i walk to a training scarecrow and someone is attacking it, it keeps tracking them. if i afk there then it'll pollute the dps logs with randoms", asking to ignore dummy tracking "unless the character is me attacking it". Upstream counts dummies as bosses (saved) and, five seconds after the user's last hit, makes the target with the most recent hits the active fight, whoever hits it. Asked whether other players on a dummy the player is hitting should still show and save, Harry chose "Keep their bars". While the meter does not know the user (started after login) every dummy fight counts, as before: hiding them all would hide the player's own. The brief's first plan for the overlay, the empty snapshot, would have wiped the player's own result five seconds after their last hit whenever someone hit a dummy nearby, so the overlay keeps the last fight instead. The running fight in History, which upstream lists ahead of the saved ones, was a third way in, so the list leaves it out too.

**Checks:** baseline on `main` (3500895) in this worktree: `scripts/Build.ps1` 641/641. With the change: `scripts/Build.ps1` 662/662 (21 new: `DummyFightRuleTests`, `DummyFightTests`). `TestEngineHostTests` pinned the handed-out store to upstream's type and failed once; it now checks the decorator around upstream's store. Fail-first (scratch script, each mutation built and run against `DummyFightTests`, `DummyFightRuleTests` and `TestEngineHostTests`, 22 tests, with the file restored byte for byte): 19 mutations, each failing its expected tests by name: the decorator saving everything or not registered; the rule ignoring an unknown user, ignoring the user in the fight, or asking eagerly; any named player taken for the user; bosses taken for dummies; a saved fight's target not looked up; the overlay unfiltered, emptied instead of held, kept across `Reset`, or holding a stranger's fight from before the user was known; details unfiltered; the history list unfiltered, counting the left-out fights, offsetting the saved ones by every running fight, reading too short a running head, or counting the running fights without the name filter; a left-out running fight opened. Two controls ran all 22 tests and passed (the clean tree, a reworded log line); one mutation known to change only cost (snapshotting every running fight, not just the dummies') passed, as expected, and has no guard. A first run of that script ran no test (wrong runner option) and "passed" both controls (LESSONS). The five new files were written with LF and converted to CRLF (LESSONS).

**In game (Harry, 2026-10-26, the portable copy built from 391097f, not launched by me):** "ok it works". Which of HANDOFF's training-dummy checks that covered was not said, so the item stays open.

**Review (#38; deep audit at high effort on 391097f, approve with nits; CI passed, runs 40267793519 and 40267798374; Codex: review completed, no findings):** fixed: (1) a snapshot read across a change of fight (the target read before upstream switched, the players after) could mix the user's boss with a stranger's dummy rows, and the held snapshot would keep that mix up (the audit's stress probe reproduced it; rare at the real poll): `GetSnapshot` now compares the active fight before and after its reads and reads again, as `GetPlayerDetails` does, giving up with nothing new after three (`DummyFightTests.ASnapshotReadAcrossAChangeOfFightIsTakenAgain`, through a `BetweenSnapshotReads` seam). (2) Two behaviours no test held: a fight without the user that is no dummy fight stays up while a stranger's dummy fight is ignored, and a fight saved before the user was known still lists and opens (`AFightWithoutTheUserStaysUpWhileAStrangersDummyFightIsIgnored`, `AFightSavedBeforeTheUserWasKnownStillOpensOnceTheUserIs`). (3) Opening a running fight read the tracker's target from the history window's thread unguarded, so a racing write could put History in damaged mode for a call: the filter treats a failed read as an unknown target (kept), and the history list uses the live fight's own target. (4) The decorator takes the lifetime of upstream's registration, and the registration test pins it as a singleton. Answered, not changed: the user known as one entity id while the user's character fights as another, unmarked, would make the user's own dummy fight look like a stranger's (the audit's probe shows it; whether the game ever does that is not known): ARCHITECTURE's "Not covered" and HANDOFF's item now say so and ask for a check after a teleport. A racing first read of the user could, at the moment the user becomes known, show one stranger's fight for a poll and then drop it with the last one shown; before that moment every dummy fight is shown anyway, so it is left as is. Upstream's `GetHistoryPage` is called twice while a boss fight runs, and a running dummy fight is snapshotted even while the user is unknown: on demand only, left as is. Checks after the review: `scripts/Build.ps1` 665/665, Debug and Release. Fail-first (the scratch runner, now refusing a run that built nothing or ran no test; 25 tests per run): no before-and-after check of the fight, a fight without the user dropped instead of held, saved fights filtered too, and the decorator registered as transient each fail their test by name; two controls (the clean tree, a reworded comment) ran 25 and passed.

**Not checked:** no window opened by me. When upstream learns the user is known only from `PlayerInfoProcessor` (opcode 0x3633); whether the game sends it only at login is not known. A racing read of the tracker's players from the overlay's poll is caught and counts as "not known" for that call; no test races it. The check of the target's id against the active session's in `GetSnapshot` guards a switch away and back between two reads, which no test makes.

**Merged `main` (#39 and #40, 9524681) into this branch:** only this log conflicted (each branch's newest entry at the top); all three entries kept, newest first. The other docs and every code file merged cleanly; the engine files this change touches are untouched by #39 and #40. `scripts/Build.ps1` 635/635 (main's 611 and this change's 24), Debug and Release; the `upstream` pin unchanged.

**Next:** CI and Codex on the merge, then merge (Harry's go-ahead, 2026-10-26: "have everything merged into main"); then HANDOFF's training-dummy item in game.

## 2026-10-26 — The bar area sizes itself, up to five players

**What:** the overlay's bar area is no longer sized by hand. It has a row for each player shown, up to five (`Pages/BarArea`: `Rows`, `Scrolls`), so it is empty with nobody to show (start-up, a reset), grows as a fight's players come and keeps the last fight's bars until the next; with more players (a raid) the top five show and the rest scroll, the player's own row pinned in view as #35 made it. The window stays exactly as tall as the content (#37's model), but its top and bottom edges are no resize handles any more (`WindowChrome.ResizeBorderThickness` 6 on the sides only), so only the width is the player's, and at the end of any move or sizing loop it takes the content's height back at once. Gone: `AppearanceSettings.BarRows` and its repair (a file with `barRows` loads, the value passed over, and a save drops it), `OverlayFit`'s drag rules (`MostRows`, `RowStep`, `DragLimits`, `HoldDraggedEdge`, `ResizedRows`, `TopAfterResize`, `CarriedRows`, `RowsFor`, `IsHeightEdge`), the window's `WM_SIZING` handling, `IMeterWindows.OverlayResizing`, the page's `sizing` class and "empty" measurement (`overlay-size.js` reports one height), and `NativeMethods.WmSizing`. `--bar-rows` falls back to 0.

**Why:** Harry: "i want it so automatically if no one is attacking, then the dps bar area is shrunk (as nothing is happening), and then in a dungeon, it automatically expands up to five dps bar players worth of area. so this means the player won't need to fiddle with resizing the dps meter. and make it so they can't anymore, like before. if in a 20 man raid, still only the top five players will be visible, and the user can scroll to see more players. what we have now with resizing, maybe store it somewhere as reference if we want to go back to it?" Asked what happens after a fight, Harry chose to keep the last fight's bars until the next over shrinking once nobody attacks. The resizable bar area (#37) stays in the history for reference: squash commit 3500895 on main has its window, rules, page and tests; ARCHITECTURE.md ("Overlay") and `OverlayFit`'s summary name it.

**Checks (SDK 10.0.402), in a worktree of main on branch `auto-bars`:** `dotnet build` and `dotnet test` 599/599 (main's 641 less #37's drag tests, plus `OverlayFitTests.TheBarAreaFollowsThePlayersUpToFive` and `SettingsTests.AFileWithTheDraggedBarAreasRowsLoads`; `RazorMarkupTests` now pins the content height alone, the side-only resize, the take-back at a loop's end and the absence of every drag piece). Fail-first against a scratch copy of the branch, control clean before and after: 12 mutations each fail their guard (six rows, rows unclamped, a cut window ignored, scrolling at five, the page's rows or scroll not from `BarArea`, the list cut to five, the top and bottom edges resizing again, a keyboard sizing kept, the fallback back at 8, the `.sizing` rule back, `BarRows` back). Headless Edge with the branch's stylesheets and synthetic rows (no window): no players, the panel 77 px with an 8 px bar area (its padding; 75 px and 6 px at row size 0.75); four players, a 124 px bar area (four rows); twenty, 153 px (five rows) and scrollable, the player's own row (9th) pinned at the bottom.

**Not checked:** a running meter (the window's fit as players come and go, the side-only resize handles, wheel scrolling), the game. Harry ran a portable copy with this change in game before a build without it brought the height resize back ("why can the UI be resizeable again height-wise??"), but no check list was gone through.

**Review (#40; deep audit at high effort on 180c056, approve with nits; CI passed, runs 40267951412 and 40267928810; Codex: review completed, no findings):** (1) The rows followed the current target's players, and upstream's target is the one the player hit last, so every switch between a boss and an add fewer had hit resized the meter (4 rows to 2 and back). Asked, Harry chose "shrink after a pause" over keeping a fight's most rows or following the target: `Pages/BarRowsHold` grows the rows at once, drops to fewer only once fewer have been wanted for 15 seconds, and empties at once with nobody to show; the page feeds it with every snapshot and a reset clears it (`BarRowsHoldTests`; `RazorMarkupTests` pins the page's use). (2) HANDOFF's overlay item and ARCHITECTURE's `RazorMarkupTests` line still described a height resized from the edges; reworded. (3) Comments in `MeterPage` and `index.html` named the dragged edge and two heights; reworded. (4) Two statements on one line in `RazorMarkupTests`; split. (5) This entry gave the empty panel as 75 px; it is 77 px (75 at row size 0.75). Not changed: the end of any move or sizing loop applies a pending shrink at once (`Refit(shrinkNow: true)`), a header drag included; harmless and documented. Checks after the review: `scripts/Build.ps1` 605/605, Debug and Release (a guard banning the word "sizing" in the page caught a reworded comment first). Fail-first (scratch runner, xunit's `-class` over `BarRowsHoldTests`, `RazorMarkupTests` and `OverlayFitTests`, 37 tests per run): no pause, none waiting too, a clock going back ignored, a 10-second pause, the page skipping the hold and a reset keeping the rows each fail their tests by name; two controls (the clean tree, a reworded comment) ran 37 and passed.

**Merged `main` (#39, the event row, 661917d) into this branch:** conflicts in README, ARCHITECTURE, this log and two comments in `RazorMarkupTests`, as the audit's trial merge found; resolved keeping this branch's bar area with #39's event-row wording (both log entries kept), and `OverlayFit`'s summary says an event's line opening rather than the notices. `scripts/Build.ps1` 611/611, Debug and Release; the `upstream` pin unchanged.

**Next:** CI and Codex on the merge; merge (Harry's go-ahead, 2026-10-26: "have everything merged into main"); then the portable copy from main and HANDOFF's bar-area item in game.

## 2026-10-26 — World events as a row of icons

**What:** the overlay's world events are one row of icons at the bottom of the meter in place of the folding notice strip (`Components/EventRow.razor`, rules in `Alerts/EventRowLayout`, drawings in `Alerts/EventGlyphs`): one 24 px button per alerted event in catalog order. An icon is dim until its event is due, then washed-out gold with the whole minutes to its start ("55m", rounded up; later minutes and seconds, `EventText.IconTime`, below) once within the lead time, and teal with the minutes to the close while its entry or portal is open. With "Show the next event" on and nothing due, the next event's icon shows its time dimly. Hovering or focusing an icon shows a tip after a short pause: the name with what it is doing in clock times ("starts 21:00", "portal open until 21:10", or the settings list's "next 21:00"), and its location; the row anchors the tip, which never leaves the window. Clicking an icon opens one detail line under the row (the old notice line: its countdown and progress, or for an event not due its next start and the time to it); a second click closes it, and a click on another icon moves it (later several open at once, below). The line is the row's own state, not saved, so `AlertSettings.NoticesExpanded` is gone (a file that has it loads, the value passed over). `AlertView` now carries the alerted events and the server offset. The catalog's order is Harry's (Shugo, Invasion, Rift, Siege, Kaira, Nahma, Executors, Daily, Weekly; the fork's started with Kaira) and the siege is renamed Abyss Siege; both are in `EventCatalog`, so the settings list follows, and `AlertSettings.DefaultEvents` is in the new order. The icons are Scarlet's own drawings: a cat with whiskers (Shugo), a three-arm vortex (Invasion), a portal with a tear (Rift) (both changed later, below), a catapult (Siege), an eye, a shield and crossed blades for the bosses, an arrow and a calendar page for the resets.

**Why:** Harry: the vertical list took a lot of space; "on the last row where our first event timer entry goes, i want that to instead be a row containing icons that arranges horizontally ... and if a user hovers over it they see a tooltip of what it is", with a screenshot of another meter's icon bar highlighting "55m" within the alert window. Asked about details: a click "expands a row under it" for that event; the next-event setting becomes a dim time on the next icon; "air raid" is the Dimensional Invasion; the icons stay Scarlet's own, in the order above, with Shugo "a cat-like icon" (the first looked like a monster boss), the invasion "more like a vortex", and the siege renamed Abyss Siege with a catapult (the first, a castle, looked like the weekly calendar). The rift was not in that list; Harry's first message named it third, so it sits after the invasion, and its spiral became a torn portal so it does not look like the new vortex.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 646/646 (a schedule-validation test that took the catalog's first entry for Kaira now looks it up by id). New: `AlertTests` (the row's icons and order, Soon, Open, the next event, whole minutes rounded up, the tip's words, the detail line's countdown and progress clamped, the click rules, a drawing per event as path data only), `SettingsTests.AFileWithTheOldNoticeStripsChoiceLoads`, `RazorMarkupTests.TheEventRowIsOneIconPerEventThatOpensItsLine`; `EventCatalogTests` pins Scarlet's order and the fork's values with the one renamed entry. Fail-first, against a scratch copy with a clean control before and after: 24 mutations, each failing the guard meant for it (no next icon, Open shown as Soon, "closes" in the tip, unclamped progress, a click that never closes, a lost line falling back to the first icon, notices not matched to icons, the idle line without its countdown, minutes floored, a shared or markup-carrying drawing, the defaults in the old order, the fork's siege name, no accessible name, the drawing as raw HTML, the tip read aloud, an unkeyed line, the icon anchoring the tip, a tip wider than the row, wrapping tip lines, a tip that never shows, the "you" gold, the page using the old strip, the old setting kept). One first run did not compile (its `sed` also hit another method's line) and tested the previous build; it was rerun scoped to its method, and the runner now refuses a mutation that does not build (LESSONS, "A check that proved nothing"). Headless Edge with the real stylesheets and the markup the component writes (no window): at the narrowest meter (300 px) with no bars the tip's top stays 18 px inside the panel and its longest name is not cut; the default seven icons fit one line at 300 px with three lit, and the row wraps only with all nine on and two or more lit; the detail line is 26 px.

**Then, from Harry (same day, with the portable copy):** "make the spacetime rift icon what the dimensional invasion icon is. thats what it looks like in game", and the invasion like a screenshot of the game's icon, a gold diamond with a crown over a bar; "make it so when users click on icon entries, they can click on multiple, to have multiple rows open". The rift has the vortex, the invasion a crown over a bar in a diamond (chosen from five drawn in headless Edge), and a click opens its event's line beside any already open, in the row's order, a second click closing only its own (`EventRowLayout.Toggle` now returns a new set; `Opened` lists the open icons in row order). `scripts/Build.ps1` 646/646. Fail-first against the scratch copy, control clean: one line at a time, lines in click order and a toggle that edits the caller's set each fail `AlertTests.AClickOpensAnEventsLineBesideTheOthersAndASecondClosesIt`.

**Then, from Harry trying the portable copy in game:** an icon read "3m" while the game's own timer showed 2:01 to 2:19, since whole minutes were rounded up: "can you show the minutes:seconds? otehrwise it can be a bit misleading". An icon now shows minutes and seconds under an hour (`EventText.IconTime`, the detail line's `Countdown` form, "2:19"), then "2h 05m" and "6d 05h" for a next event further off. `AlertTests.AnIconsTimeIsMinutesAndSeconds` replaces the rounding test. A fail-first run putting the detail line's countdown on the icon survived until `RazorMarkupTests.TheEventRowIsOneIconPerEventThatOpensItsLine` pinned the icon's call to `IconTime`; rounding up again, or an hour shown as "60:00", fail `AnIconsTimeIsMinutesAndSeconds`. `scripts/Build.ps1` 646/646. Re-measured in headless Edge: a lit icon is 58 px wide with "54:53" (53 px with "55m"), so at the narrowest meter (300 px) three lit icons now wrap to a second line, where they fitted one; at 360 px, about Harry's width in the screenshot, they fit one.

**Not checked:** the row in a running meter (the component's render, the hover tip in WebView2 and a click, which need a window), over the game, the times in game. `docs/images/alerts.gif` and `settings-alerts.png` still show the list.

**Review (#39; deep audit at high effort on e65de67, approve with nits; CI passed, runs 40267898691 and 40267880066; Codex: review completed, no findings):** fixed: (1) nothing checked that the row's view carries the player's server offset (a mutation passing the preset's survived; tips and lines would be an hour off at UTC+8): `AlertTests.TheRowsTimesFollowThePlayersServerOffset`. (2) The tip's place above the row and the row's local clock were not pinned (a tip below the row, cut by the panel, and UTC times both survived): `RazorMarkupTests.TheEventRowIsOneIconPerEventThatOpensItsLine` pins both. (3) A click re-rendered only the row, so the page measured its new height on its next refresh, up to 100 ms later, the bar area squeezed meanwhile: the row raises `LinesChanged`, which renders the page at once (not seen in a window either way). (4) This entry's first paragraph is marked where later changes superseded it, the component's header says minutes and seconds, ARCHITECTURE's `SettingsTests` line names the old-file test, and the PR text no longer says whole minutes. (5) LESSONS no longer names a mutation runner that is not in the repository. Not changed: no `aria-controls` on the icons, and the faint focus style (both noted by the audit). Checks after the review: `scripts/Build.ps1` 647/647, Debug and Release (a markup guard that pinned `Held` as the tag's last attribute now allows the new one). Fail-first (scratch runner, xunit's `-class` over `AlertTests` and `RazorMarkupTests`, 44 tests per run): the view carrying the preset's offset, the tip below the row, the row in UTC, a click not telling the page, and the page not binding the callback each fail their test by name; two controls (the clean tree, a reworded comment) ran 44 and passed.

**Next:** merge (Harry's go-ahead, 2026-10-26: "have everything merged into main"); with Harry's go-ahead a look at a demo meter, and re-recording the alerts images.

## 2026-10-26 — A bar area the player sizes, from no rows to ten

**What:** dragging the meter's bottom edge sizes the bar area only: as many rows tall as dragged (`AppearanceSettings.BarRows`, 0 to 10, `OverlayFit.MostRows`), saved, with empty rows when fewer players are on it and a scroll (#35's pinned own row, fade and no scroll anchoring) when more are. The rest keeps its own height (header, target card, totals, notices), and the window is exactly as tall as the content, growing when the notices open or the card comes. While an edge is dragged (`WM_SIZING`) the page marks the panel `sizing`, which fills the window so the bars follow the edge; `MinHeight` and `MaxHeight` hold the window between no rows and ten for the drag; where it ends becomes whole rows (`OverlayFit.RowsFor`), which the window takes at once and saves. An earlier build's saved window height becomes rows on the first start, once. `js/overlay-size.js` measures the panel with the bar area at its rows and at none. #35's "Rows before scrolling" slider and `AppearanceSettings.VisibleRows` are gone (a file with the value loads with it passed over).

**Why:** Harry: "please make the dps meter resizeable height-wise, min height can just be empty like how it looks atm, and max height of 10 players worth i think", with a screenshot of the empty meter ("min size like this?"); asked what a small party should look like, Harry chose "Keep my size" over shrinking to the bars. A first form sized the whole meter (the panel filling the window, empty panel below the notices); Harry then asked: "make sure the resizing only changes the size of the dps bar area. it doesn't affect the event timer area", so the drag now sizes the bar area alone and the empty rows sit inside it, above the timers.

**Checks:** the first form: `scripts/Build.ps1` 598/598, and a demo meter (`--demo`, a scratch `--data-dir`, with Harry's go-ahead while Harry was in game and not using it), driven with window messages only (`WM_ENTERSIZEMOVE`, `SetWindowPos`, `WM_EXITSIZEMOVE`; no mouse or keyboard input) and seen through `PrintWindow`: a resize past ten rows was held at 411 px (121 px empty and 290 px of rows), one below the empty meter at 121 px, one between rows snapped to three rows (208 px), the snapped height was saved, a move kept it, a restart restored it; no warning in its log. A first launch popped Windows' ".NET is missing" dialog on Harry's screen (the debug build is framework-dependent and the runtime is the private SDK's); it was closed at once (LESSONS). The bar-area form (once Harry was out of game, SDK 10.0.402): `scripts/Build.ps1` 604/604. The same demo, driven the same way plus `WM_SIZING` (bottom edge; it carries the dragged rectangle, which Windows copies across processes, so a null one is dropped: the first drag, sent with null, was rightly taken for a move), started from an earlier build's settings (a 300 px window, no rows, the notices open with an hour's lead): the 300 px became 2 rows (the notices and the boss card took the rest), the window 298 px, its content. A drag to 900 px: held at 530 px (240 px empty and 290 px of rows) while it lasted, the bars following the edge and the notices staying below; let go, 10 rows saved and the window at 530 px, four bars and six empty rows above the notices. Between fights the boss card went and the window shrank by its 44 px, the bar area kept. A drag to 60 px: held at the empty meter (196 px), 0 rows, header, totals and notices. A drag to 339 px: 5 rows (4.93 above 196), the window growing by the next fight's card. Restarted with the notices closed: the same 5 rows (four bars, an empty row), the window 87 px shorter, three notice rows of 29 px. No warning in its log.

**Found by Harry with the portable copy:** "when i resize it by dragging the top side of the window, when it maxes out in height, the window will move up with my cursor". WPF holds `MinHeight` and `MaxHeight` by keeping the top and moving the bottom, so with the top edge dragged past ten rows the top followed the cursor and the bottom with it. The window now holds the rectangle in `WM_SIZING`, before Windows moves the window, by the edge being dragged (`OverlayFit.HoldDraggedEdge`, tested on all eight edges and corners): the top stops and the bottom stays, or the reverse. `scripts/Build.ps1` 616/616. On the demo (window messages; `WM_SIZING` with the top edge and a rectangle 1000 px too tall, the held rectangle read back): held at 297..740 with the bottom kept at 740, mid-drag and after, 10 rows saved; the top dragged 900 px down: held at 587..740 (the empty meter, 153 px), 0 rows. LESSONS entry. Fail-first, eleven mutations, each failing its test by name: the top edge held like the bottom, no hold, rows floored or unbounded, `WM_SIZING` left to WPF, no carry-over from an earlier build's height, no scroll for more players than rows, the bar area's clamp gone, the measured bar area at its full height, the bars not following a drag; a comment passes (the top-edge one first failed to compile, warnings being errors, and was rerun in a form that compiles).

**Not checked:** a real drag with the mouse (the window messages stand in for it), wheel scrolling, the meter over the game, row sizes other than 1 in a window.

**Review (deep audit at high effort, on a920f4a; verdict: request changes; CI passed; Codex: one P2):** fixed: (1) **major:** the window saved its shown height, which at no rows with no notice due is 77 px (75 at row size 0.75), below the 96 px floor a saved size is kept to, so the settings dropped the whole placement (LESSONS' earlier "small overlay lost its saved position"), and with no notice due a drag could not reach 0 rows: the floor (`WindowPlacement.MinSize`) is 48 px now, a saved height is never below it, and a test pins it below the smallest empty meter. (2) Carried over at the default's 8 rows, the window never refitted: it fits at once now, and only a restored placement is carried over (a first start takes the default). (3) A drag of a side only (Codex P2: a meter cut by a short screen saved the rows it fit), or a resize cancelled with Escape, rewrote the rows: only a change of height by the top or bottom edge sets them (`OverlayFit.ResizedRows`), and only those mark the page `sizing`. (4) At no rows with players a sliver of the pinned own row showed over a scrollbar: no rows are drawn then unless the edge is being dragged. (5) A top-edge resize settled by moving the bottom: the bottom stays (`OverlayFit.TopAfterResize`). (6) WPF's `MinHeight`/`MaxHeight`, set for every move-or-size loop, shrank a window waiting to shrink when a header drag began: they are not used; `WM_SIZING` holds the edge with `OverlayFit.DragLimits`. (7) The row step is re-sent when only it changes. (8) Stale docs and comments (the saved placement, `OverlayPlacement`, `OverlayFit`'s work-area qualifier, the rows giving way, HANDOFF). (9) Housekeeping. The pure rules (`DragLimits`, `ResizedRows`, `IsHeightEdge`, `TopAfterResize`, `CarriedRows`) are tested.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 641/641. Fail-first, each failing its test by name: a side setting the rows, an unchanged height setting them, the top kept after a top drag, a first start carrying the window's height, the floor back at 96, a saved height below the floor, rows drawn at no rows, the step not re-sent, the edge held by WPF's floor, a cancelled resize refitting late; a comment passes. One of them (rows drawn at no rows) first passed: a helper that rewrote the test's lines had dropped five new assertions (LESSONS); with them back it fails. The demo (window messages): a drag of the right side 60 px wider kept 3 rows; the top edge 70 px up (2.4 rows) settled on 5 rows with the bottom held at 708; a drag to nothing gave 0 rows, no bar drawn in a fight, and between fights the meter was 77 px with its placement saved at 77; no warning in its log but the hotkey Harry's own meter holds.

**Next:** Codex on the fixes and CI; merge with Harry's go-ahead (given: "push and get everything merged into main and rebuilt"); rebuild the portable copy from main.

## 2026-10-26 — Odyle energy: the entry's three kinds, updates that start 00, no zeros

**What:** `Odyle/OdyleEnergyRecords` reads the energy entry in all three of its kinds, which say which values are sent, a zero being left out: `0c` both, `04` basic only, `08` charged only (it read only `0c`). An update's first byte is no longer trusted as a count: at least one entry is read, since updates that start `00` carry one too. Both are A2Tools' own later fixes (2723bff and 29e7193, after the PR #63 that was ported). Scarlet's addition: an entry with a 0 where its kind says a value is, is turned away, since the game never sends one and the data before the list opens with exactly such a look-alike (`0c 01 00 00 00` in A2Tools' sample). `OdyleEnergyRecordWriter` writes the same three kinds and refuses no energy at all, whose form is not documented; the demo stops sending at 0 and 0. `OdyleEnergyTap` logs each change with the record it came from (the list or an update), never the value or the bytes. THIRD-PARTY-NOTICES pins both fix commits; ARCHITECTURE and HANDOFF follow.

**Why:** Harry, with a screenshot of the overlay after a boss: "odyle counter doesnt work", the totals line reading `0 (+0)`. The port followed A2Tools' PR #63 as merged; A2Tools found afterwards that players with no charged or no basic energy are sent one-value entries, and that many updates start `00`, so this reader missed their energy entirely. Under the old rules, a list whose header look-alike is followed by a slot-2 look-alike, with the real entry charged-only after it, reads as exactly `0 (+0)` (tested). Which of these Harry's game sent is not known: no bytes were captured, nor should be.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 583/583 (`OdyleEnergyTests`: the three kinds in the list and in updates, updates that start `00`, the zero look-alike, the writer's kinds and refusal, the log without values). With main's rules rebuilt in place, 11 of the new cases fail, the look-alike list reading `0 (+0)`. Fail-first, ten mutations, each failing its test by name: zeros taken, the update's count trusted, the charged-only kind not looked for in the list, charged-only read as basic and basic-only as charged, the slot not checked, the writer always writing both values, the log showing the value, logging nothing, and not telling the list from an update; a comment passes. Every byte in the tests is synthetic (the made-up key `92 21`).

**In game (2026-10-26, portable copy built from this working tree over 966e30b, not launched by me):** started mid-session it showed nothing, as expected; after a relog it showed the energy, matching the tooltip (Harry: "yep it works"), and the log named the list of entry counts (01:08:33). What the old reader's `0 (+0)` came from is still not known.

**Not checked:** an update moving the value (a recharge item, a raid); the cases with basic at 0 or charged at 0 against the tooltip; how the game writes no energy at all. The demo stopping at 0 and 0 is not tested (no test runs the demo that long). Nothing was run with a window open.

**Review (deep audit at high effort, on 73821ac; verdict: request changes; CI passed; Codex's review still running then):** parity with A2Tools' current parser confirmed (their Odyle code is unchanged since 29e7193; the zero rule is the only difference on plausible input, and none of their eight documented samples reads differently). Fixed: (1) **major:** an update took any slot-1 entry, and with the new kinds another counter's one-value entry (A2Tools' list holds one) would read as the energy, "2 (+0)": the reader now returns the entry's key (`ReadEntry`), the tap keeps the key from the last list and takes an update's entry only under it; before any list the first slot-1 entry is still taken, as A2Tools does. (2) The demo reached 0 and 0 after about 160 fights: the display froze and a later Stop then Start threw in `Begin` (the writer refuses 0 and 0): `AfterFight` starts again from the opening values, never returning 0 and 0. (3) ARCHITECTURE and LESSONS stated the cause of Harry's `0 (+0)` as fact: they now give it as unknown, with the audit's likelier candidate too (an update of the header's table, whose id-1 entry reads as `0c 01 00 00 00`: kind 0c, slot 1, key 0, values 0 and 0), now a test; the zero rule's comment covers updates. (4) Ignoring a non-zero count passed every test: now tested. (5) Some test bytes were A2Tools' published sample bytes (an update without the energy, the header's start, an update's trailing bytes, and the writer's data before the list, the last two from #34): replaced with made-up bytes of the same shape. (6) HANDOFF notes the likely stale value once both are spent. LESSONS entry extended.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 588/588. Fail-first, each failing its test by name: an update ignoring the key, the tap never keeping it, updates defining the key too, the count ignored, the demo running out, zeros taken; a comment passes. (The never-keeps-the-key mutation first failed to compile, warnings being errors, so its first run tested a stale build; rerun in a form that compiles, it fails.)

**Merged:** Codex found nothing on 597b188 and CI passed; squash-merged as eac1bf5 (#36) with Harry's go-ahead.

**Next:** the rest of the in-game check (HANDOFF).

## 2026-10-25 — The bars scroll past 10 players; your own row stays in view

**What:** with more players than "Rows before scrolling" (`AppearanceSettings.VisibleRows`, 10 by default, 3 to 40, a slider under Row size in Settings, Appearance; clamped and reported by `SettingsNormalizer`), the overlay's player list stops at that many rows and scrolls (`MeterPage.RowsClass` marks it `capped`; `app.css` `.rows.capped`: a `max-height` of that many 29 px rows from the panel's `--visible-rows`, a thin scrollbar, the bottom 9 px faded where the next row's top shows). The page measures the list at that height, so the window stops growing there. The player's own row is sticky (top 0, bottom 3 px): it stops in the first or the last whole row's place when it would scroll out of sight, with a solid backdrop only while stuck (a `scroll-state` container query on the row), and `overlay-fx.js` measures again on scroll, since a stuck row's `offsetTop` follows it. A small party's list is unchanged (clipped, no scrollbar). The notices still never scroll. Comments that said nothing in the meter scrolls now say only the capped list does (CSS, both scripts, `OverlayFit`, `OverlayWindow`, `IMeterWindows`); README, ARCHITECTURE and HANDOFF follow.

**Why:** Harry, with a screenshot of a 20-player raid: "the window expands a lot vertically. can we cap the dps bars to like 8-10 players? and if you want to see more players then you have to scroll. the dps meter is just too long vertically", then "maybe set a maximum height for the dps meter window (excluding the event timers)". This reverses, for the bars only, "i dont like scrolling" (2026-10-25, event timers): the timers keep their no-scroll rule. Pinning the player's own row is my addition, so a player ranked below the cap never has to scroll to find their own numbers; it is shown in the preview for Harry to keep or drop.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 575/575 (`RazorMarkupTests.TheRowsScrollPastTheirCap` new; `TheOverlayPanelFitsItsContent` allows exactly one scrolling overlay rule, `.rows.capped`; `SettingsTests.RowsBeforeScrollingKeepToTheirRange`, 6 cases, and the default for a file from an earlier build). Fail-first, 19 mutations on a scratch copy, each failing its test by name: the cap counting rows without their gap, a fallback cap unlike the default, the capped list not scrolling, the base list scrolling, the own row not sticky or pinned over the fade, the backdrop always on or queried on one edge only, no fade, the measuring rule uncapping the list, no re-measure on scroll, capping at the cap itself, no `--visible-rows`, the list never capped, no clamp, the clamp unreported or not kept, a default of 12, a half-row slider step; a comment change passes. (A first run of the unreported-clamp mutation did not compile, warnings being errors, so it tested a stale build; rerun in a form that compiles, it fails.) Headless Edge 155 (no window; the real `app.css` and `alerts.css`; synthetic players and markup): 20 players at the default cap give a 298 px list (10 × 29 + 8) and a 528 px panel, where the list alone was 588 px before; at row size 1.25 the list is 372.5 px; 8 players are not capped (240 px, no scrollbar). The player 14th sits stuck in the 10th row's place (263 px from the list's top) with the backdrop; scrolled six rows, in its own place without it; ranked 5th and scrolled to the end, stuck at the top (2 px) with it. The stuck state reads correctly one frame after a scroll, not in the same task.

**Not checked:** the meter itself (HANDOFF): wheel-scrolling over the overlay while the game has focus, the pinned row in a live raid, a reorder while scrolled, the look over the game and a picture. Nothing was run with a window open.

**Review (deep audit at high effort, on 54cdbaf; verdict: request changes; CI passed):** the cap, its arithmetic, the setting and the window fit held. Fixed: (1) with bar effects on, a reorder while scrolled made every visible row jump a row and slide back: `overlay-fx.js` compared `offsetTop` (content coordinates) while Chromium moved the list's scroll position during the reorder (anchoring at the view's edge; clamping at the end when a row above the view goes). Rows are now measured in the list's view (`drawnTop`) and the capped list has `overflow-anchor: none`. (2) While cut, the 26 px give-way fade beat the capped list's own on specificity and half faded the pinned row and the last row: `.overlay.cut .rows.capped` keeps the 9 px one. (3) A cut, capped list changing height moved the pinned row with nothing re-measuring: a `ResizeObserver` calls `remember`. (4) The backdrop was a hard-coded colour called the panel's: it is `--bg` now, noted at `:root`, and documented as opaque. (5) Without `scroll-state` queries (a WebView2 runtime before Chromium 133) the pinned row would show the row beneath: `@supports not` keeps the backdrop on there. (6) The fade's 9 px is now tied in the test to the row gap and the list's bottom padding. (7) The script's header comment and ARCHITECTURE said positions were measured only on style or class changes. LESSONS entry.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 575/575. Fail-first, 12 more mutations, each failing its test by name: remembering or comparing rows in content coordinates, `drawnTop` ignoring the scroll, no resize observer or one kept across lists, scroll anchoring back, the cut capped list with the give-way fade or without its rule, a fade wider than the gap and padding, the list's bottom padding changed, the backdrop a literal colour, no fallback without `scroll-state`; a comment in the script passes. Headless Edge 155 (the real `app.css` and `overlay-fx.js`, synthetic rows, scroll events dispatched by hand since headless renders no frames): a swap at the view's top edge scrolled 140 px, the end of the list losing a row above the view (scroll position 290 to 261), a row moving from below the view to above it, the own row pinned at the top and at the bottom with a swap in view, and an uncapped party's swap: no row in view moved except the moved ones, each starting from where it was drawn. The same page on 54cdbaf's files jumped every visible row by 29 px in the first three cases. Computed styles: a cut, capped list's fade is the 9 px one, a cut uncapped list keeps 26 px, the unstuck own row has no backdrop.

**Merged:** Codex found nothing on efacdc5 and CI passed; squash-merged (#35) with Harry's go-ahead, after main (with #36) was merged into the branch: WORKLOG and LESSONS kept both changes' entries.

**Next:** Harry's in-game check (HANDOFF).

## 2026-10-25 — Odyle energy beside the ping

**What:** the overlay's totals line shows the player's Odyle energy left of the ping, basic then charged in brackets ("420 (+1,200)", `Formatting/OdyleText`), with a crystal of Scarlet's own drawing, once the game has sent it and while "Show Odyle energy" (`AppearanceSettings.ShowOdyleEnergy`, on by default; Settings, Appearance) is on. The engine reads it beside upstream's pipeline: `Upstream/OdyleEnergyTap` subscribes to upstream's `TcpStreamBuffer.PacketExtracted`, opens LZ4 bundles with upstream's internal `PacketProcessor.ProcessPacket` (bound once by reflection), and `Odyle/OdyleEnergyRecords` reads records `0b 61` (the whole list of entry counts) and `0c 61` (changes), as A2Tools documented them; `ICombatEngine` gains `OdyleEnergy` and `OdyleEnergyUpdated`. The demo hands the tap synthetic records (a login list, an update per fight). ARCHITECTURE "Odyle energy" has the detail; THIRD-PARTY-NOTICES credits A2Tools (GPL v3).

**Why:** Harry: "work on a new feature - odyle energy displayer. place it to the left of the ping display", with an earlier analysis of how Abyss Logs and A2Tools (taengu/A2Tools-DPS-Meter#63) read it from the game's traffic. Upstream's opcode table takes only processors marked with its internal attribute, so a Scarlet processor cannot join it without editing `upstream/`; the stream buffer's public event is the seam.

**Found on the way:** upstream's `PacketAccumulator` searches for its keep-alive from the end of the bytes it holds (`IndexOfArray(pattern, length)` passes the length as the start), so a new stream synchronises only on a keep-alive left from an earlier chunk. Live traffic presumably gets there, since the meter reads fights (how soon is not measured); a one-off injection does not, so the demo hands its records to the tap directly and the tests synchronise with a padded keep-alive and then another. A candidate fix to offer upstream; whether it delays live capture's start is not verified.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 551/551 (`OdyleEnergyTests` new, 20 cases; `FormattingTests.OdyleEnergyReadsAsTheGameWritesIt`, `RazorMarkupTests.OdyleEnergySitsLeftOfThePing`, the setting's default in `SettingsTests`). Fail-first, twelve mutations, each failing its test by name: the list without the slot-2 check, any value plausible, a varint wrapping past 32 bits, an update reading only its first entry, bundles not opened, an unchanged value raised again, exceptions escaping into the stream, the demo without its login list or its spending, the page never reading the value, a ping before the energy, the energy not pushed right; a comment in the reader passes. Every byte in the tests is synthetic.

**Not checked:** real traffic (HANDOFF): that the records and the shape heuristic hold on the live game, the value against the game's tooltip, a recharge item and a raid; the display over the game. Nothing was run with a window open.

**Review (deep audit at high effort, on 0b41a21; verdict: approve with nits; Codex found nothing; CI passed):** no correctness bug; it checked the parser's bounds (and that `InList` is linear: 40 KB in about 0.3 ms), parity with A2Tools' Rust, the reflection bind's failure cases, threading and lifetimes, the contract and the setting. Fixed: (1) every list here had a two-value slot-2 follower, while the game's list (A2Tools' sample) has a one-value one, so a "tightening" to the two-value kind would have stopped the meter reading the login list with CI green: the writer now writes the game's shape, the tests take both and an update with the game's trailing bytes. (2) The varint guard was tested on one bit: now on every bit past 32 and a sixth byte. (3) The docs said an escaped exception would only stop later records; it is worse (upstream's accumulator would keep the record and deliver it again, its hits counted twice, or stall the stream): the remarks, ARCHITECTURE and the tests say so, and each catch now has a test of its own (a test-only constructor takes a throwing bundle opener; a throwing subscriber inside a bundle). (4) Opening each bundle a second time (a full decompression and allocations, on the capture thread) is now in the tap's remarks, ARCHITECTURE and HANDOFF's CPU check. (5) The demo's login list went through the schedule, which changed a seeded demo's event order: it goes in `Begin` now. (6) The demo's recharge is a pure step (`DemoCombatFeed.AfterFight`) with tests; (7) Dispose's unsubscribing has a test; (8) test names and claims say what they check, and unverified effects of upstream's quirk are marked so; (9) purpose comments; (10) the reflection bind returns null rather than throw if a delegate cannot be made; (11) THIRD-PARTY-NOTICES pins A2Tools' merge commit.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 568/568. Fail-first, the audit's six surviving mutations now each fail their test by name (the follower required to be two-value, no outer catch, no subscriber catch, no unsubscribe, a recharge adding to charged, a narrower overflow guard), and so does logging a reading failure every time; the first twelve fail again; a comment in the reader or the tap passes.

**Next:** Codex and CI on the fixes; Harry's in-game check; merge with Harry's go-ahead.

## 2026-10-25 — v0.2.0 released

**What:** PR 32 merged as e5cbdcd (Codex found nothing on its head 99f2ff4; CI passed). `v0.2.0` tagged on it; the release workflow (run 40189037349) built the draft: `ScarletMeter-0.2.0-win-x64.zip`, `ScarletMeter-0.2.0-source.zip` and their `.sha256`. Checked as DEVELOPMENT.md "Releases" asks: both checksums match; the 518 files of both zips hold no home path, machine or account name and no e-mail address of the player (the addresses in them are third-party licence authors, upstream's README contact and the tests' made-up ones). Published as a pre-release: https://github.com/harrynguon/Scarlet-Meter/releases/tag/v0.2.0.

**Why:** Harry: "run the audit. fix what it finds. then merge and release."

**Next:** the in-game checks in HANDOFF; the next release when there is something to ship.

## 2026-10-25 — v0.2.0 prepared

**What:** `<Version>` is 0.2.0 (`src/Directory.Build.props`) and `docs/release-notes/v0.2.0.md` says what is new since 0.1.0 (#26 to #33: the icon and the header's face with its HP ring, readable rows, the one-bar world-event strip, the boss-timers window, the meter growing instead of scrolling, and the fixes), how to update from 0.1.0 (move the `data` folder), the Npcap step and the risk note, as DEVELOPMENT.md "Releases" asks. No package changed, so `Write-ThirdPartyNotices.ps1` has nothing to rewrite. Added to PR 32 rather than a PR of its own, so the release takes one round of review.

**Why:** Harry: "run the audit. fix what it finds. then merge and release."

**Next:** merge PR 32 once Codex and CI are clean on its head; tag `v0.2.0` on the merged commit; check the draft's zips (checksums, no account name, home path or e-mail address in their files); publish the draft as a pre-release.

## 2026-10-25 — Scarlet's face in the header, ringed by the boss's HP; larger class icons on the bars

**What:** (1) The overlay's header starts with Scarlet's face (`wwwroot/images/avatar.png`, 64 px, shown 20 px and round), always, between fights too, in place of the state dot. Its ring takes the current target's HP (`MeterPage.AvatarClass`, `Formatting/HpBand`): green (`#5ed36a`) from 66%, yellow (`#f2c94c`) from 33%, red (`#f04c56`) above 0; glowing while the HP moves, without the glow and a touch dimmer once it has been still for 10 seconds; grey at 0 HP; a faint edge between fights. The overlay's `.state-dot` rules are gone. (2) The class icon on the overlay's bars is 19.5 px instead of 18 (`.rows .class-icon`, about 8% larger), still with 3 px or more above and below it in the 26 px row; the history and details windows keep 18 px. (3) `Test-PublishLayout.ps1` requires the image in a publish; THIRD-PARTY-NOTICES "Artwork" names it.

**Why:** Harry: "is there anywhere we can put this image? like in permanent place of the grey dot in the dps meter at the top left (and why do we need the grey dot)? show me first though", with the portrait without its meter badge. The dot showed the fight's state, three ways (live, still for 10 seconds, down); the text already says combat or defeated, so its own news was the still-for-10-seconds amber. Three ways were rendered in headless Edge with the real styles (the face alone, the face with a small corner dot, the face with a ring in the state colour); Harry chose the ring, "a tiny bit smaller" (20 px rather than 22). Then: "can you make the class icons like 5-10% bigger in the dps bars? it's a bit hard to see them." Then, of the ring's colour: "green looks as bit weird. OH WAIT, how about colour coding based on the hp percentage??? like green = 66->100% hp, yellow = 33-66% hp, red = 0.1-33% hp" (the green was the theme's teal, `--good`). A mock-up of every state was shown first; the still state keeps the band's colour and loses the glow, so the dot's one signal of its own survives. Noted to Harry: the ring follows the target the player hit last, as the card does, so it flips to an add's HP while the player hits one; and green against red is the hardest pair for red-green colour blindness (the yellow step and the HP bar below help).

**Side effect:** the history list's in-progress dot (`FightHistoryPage`, `.state-dot` in views.css) also took the overlay's base `.state-dot` rule, which gave a finished fight's grey dot a green glow; with that rule gone it has only its own styles.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 521/521 (`RazorMarkupTests.TheHeaderShowsScarletsFaceRingedByTheFightsState` and `TheBarsClassIconsAreALittleLarger` new, `FormattingTests.HpBands` with nine boundary cases; `AppIconTests` checks the notices name the image). Fail-first, for the face and icons seven mutations, each failing its test by name: the face only inside the fight branch, the still state shown as live, the live ring without its glow, the image no longer required in a publish, the bars' icon back at 18 px, at 22 px (over 10%), and not square; for the HP ring five more: 66% counted as yellow, 0% given a band, the still state not marked, the still ring keeping its glow, the green back to the theme's teal; a comment added in the header's styles passes each time. Headless Edge (no window), real styles: the header and bars today and with the change, in each band, still, defeated and between fights.

**Not checked:** the meter itself (HANDOFF): the face's sharpness at the display's scale, the ring in a real fight, dragging by the header over the face, the larger icons at every row size.

**Review:** pushed to PR 32 as 0e478cd with Harry's go-ahead; Codex found nothing and CI passed on it; Harry's copy was rebuilt from it. A standard audit on 0e478cd (approve with nits) found: (1) a live fight whose target's total HP is not known yet (`HpPercent` reads 0 then) showed no ring, like the gap between fights, while the docs and a NaN test case described an "unknown" that `HpPercent` can never return; the mapping moved into `HpBand.RingClass(phase, target)`, a white-ish ring now marks that case (`.hp-unknown`), and `FormattingTests.HeaderRings` tests every phase and the unknown total instead of the source text. (2) The guard did not check the still ring's dimming or that the history list keeps its own dot in views.css; it does now. (3) This entry's next step was stale. (4) Noted, not changed: 19.5 px is a fractional size, so at 100% scale the icon's edges may be slightly soft; Harry asked for 5 to 10%, and the HANDOFF check covers how it looks.

**Next:** Codex and CI on the fixes; merge and a v0.2.0 release with Harry's go-ahead (given: "run the audit. fix what it finds. then merge and release").

## 2026-10-25 — No gap above a table's header; the picture fits the panel again

**What:** (1) The tab body (details, history, settings) has no top padding; a 6 px spacer (`.tab-body::before`) that scrolls away keeps the space above the content. A table's sticky header (`top: 0`) stopped at the padding edge, 6 px below the top of the scroll, and the rows showed through that strip. (2) The overlay's picture covers the panel again (`center / cover`, laid out against the panel), instead of the window (`background-attachment: fixed`, since #26).

**Why:** Harry, with screenshots: "in-between the skills row buffs.. row and the skills spec .. row, there's a bit of a gap, so when yoiuy're scrolling up and down you see some of the skills through taht gap. please close that gap", and "when choosing a background image, it no longer fits in the background" (a zoomed-in slice of the picture, against an older screenshot where all of it showed). Both reproduced in headless Edge (no window) with the real styles before fixing: the header 6 px below the top of the scroll when scrolled; and in a 480 × 640 window with about 250 px of panel, the picture scaled to the window's height and centred on it, below the panel.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 505/505 (`RazorMarkupTests.StickyTableHeadersSitFlushUnderTheTabs` new, `TheOverlayPanelFitsItsContent` now requires the picture on the panel). Fail-first, six mutations, each failing its test by name: `fixed` back on the picture, `background-attachment: fixed` added, the tab body's top padding back, no spacer, top padding on the log body, the header stuck at 6 px. Headless Edge after the fix: the scrolled header's offset is 0; the whole synthetic picture (both corner labels and its centre) shows in the panel in the tall window.

**Not checked:** the meter itself (no window opened); the picture moving as the panel's height changes, the trade-off of laying it out against the panel (HANDOFF).

**Shown to Harry:** the before and after renders, also with Harry's own picture (not committed); Harry asked to push it as a PR without merging.

**Review (standard audit on 65f14a0; verdict: approve with nits; Codex found nothing):** the audit rendered every page using the tab body (details tabs including the Log tab's flex layout, history, settings) and found its spacing unchanged, and nothing else depending on the picture's attachment. Fixed: (1) HANDOFF still asked that the picture "does not jump when the notices come and go", which only laying it out against the window gave; it now asks that it moves only with the panel's height and never shows a zoomed slice. (2) "Rescales a little" understated it: the picture re-centres by half of any height change, and rescales only in a panel proportionally taller than it; the stylesheet comment and ARCHITECTURE say so. (3) The new guard read only the spacer's height and rules starting with `.tab-body`: a spacer without `content` or `display: block`, `padding-block-start`, a non-digit padding (`.4em`) or `.details .tab-body { padding-top }` all passed. It now pins the spacer's `content` and `display`, and reads every rule naming the tab body for `padding`, `padding-top` or `padding-block[-start]` other than 0, comments dropped. (4) This entry's next step was stale.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 505/505. Fail-first, the audit's five cases, each failing the guard, and a padding rule only inside a comment passing; the first six mutations fail it again with the new guard. The control caught a mistake in the first version of the new regex: `:\s*(?!0[\s;])` let `\s*` give back the space before a 0, so the guard failed on the clean stylesheet and every "caught" mutation meant nothing; it is `:(?!\s*0[\s;])` (LESSONS).

**Next:** Codex and CI on the fixes; merge with Harry's go-ahead.

## 2026-10-25 — The history tests no longer race upstream's startup cleanup

**What:** `TestEngineHost` replaces the history store's registration (the engine's, or the test's own, as `HistoryTests` makes) with one that returns the store only once its startup cleanup has logged that it finished, using the host's existing `HistoryCleanupWatcher`. So the test, the engine and the demo feed all get the store after the cleanup, and nothing a test writes can meet it. New `TestEngineHostTests.TheHistoryStoreIsHandedOutOnlyOnceItsStartupCleanupHasFinished` holds the cleanup (a proxy over upstream's settings that stops at the retention period, which only the cleanup reads, before it deletes anything) and checks the store is not handed out until it is released. `HistoryTests`' summary says why its damaged rows are safe. Test code only; nothing under `upstream/` or `src/` changed. ARCHITECTURE (the test-host paragraph), LESSONS.

**Why:** CI's push run 40165530306 on #30's commit 65400f4 failed `HistoryTests.AFightSavedWithAnUnusableStartOrEndShowsItsPlayersOnly` at its last-but-one line (expected 5 fights, 4 listed); the pull-request run on the same commit passed. Upstream's `CombatHistoryStore` constructor starts `CleanupExpiredSessionsSafe` with `Task.Run`, which deletes every session ending before now minus the retention period. The test damages fight 904 to end at `DateTime.MinValue` plus two hours; when the thread pool starts the cleanup late, it deletes that row. The other damaged rows are safe: 901 keeps its end, 902, 903 and 905 end after the cutoff, and text in a date column (the unreadable-row tests) compares after any date. But `HistoryTests` dates its fights from 2026-10-20 (`Day1`) to four days later, and every one of its tests saves a `Day1` fight, so from the evening of 2026-11-19 the default 30-day retention would cover those (and the later ones a day or more after), and the same late cleanup would delete ordinary saved fights in any of its tests. No effect on the meter: a fight saved while the cleanup runs ended moments ago, inside any retention period.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` on `origin/main` (873a153) 500/500 before the change. Reproduction on the old host, with a probe (not committed) that delays the cleanup by 3 s, as a busy runner does, and waits 4 s after the damage: the test fails, and listing the table there shows `[901, 902, 903, 905]`, only 904 deleted. The same probe with a one-day retention (standing in for the default 30 days a month from now) also fails `SavedBossFightsAreListedNewestFirstWithTheirTotals` (3 fights expected, 0 listed). With the new host and the same probes, all 14 `HistoryTests` pass (the store waits the 3 s). Fail-first on the guard, each failing it by name (`Assert.NotSame`): the wait removed from the hand-out, and the hand-out removed. After the probes were removed: `scripts/Build.ps1` 501/501, `scripts/Build.ps1 -Configuration Release` 501/501 (run alongside the next check, which loaded the machine), and the whole suite 20 times in a row through the test executable: 20 passing runs, none failed.

**Not checked:** a CI run under load reproducing the original failure (it was seen once); the probe stands in for it.

**Review (standard audit on 88c97eb, with two deep checks since the host now blocks on a thread-pool task: upstream's behaviour read, and the wait removed to break the guard; Codex):** the audit approved with two nits, both fixed in docs: (1) the date was loose, since only the fights dated 2026-10-20 pass the retention on 2026-11-19 and the later ones a day or more after (ARCHITECTURE, LESSONS and this entry now say so); (2) this entry did not yet record the review. It confirmed the cause from upstream's code and CI's log, that every path to the store goes through the new registration (upstream registers only the interface, and the engine, the session manager, the demo feed and `SyntheticCombat` take it), that the blocking factory cannot deadlock (the cleanup uses only services already built), and that the guard fails with the wait removed. Codex reviewed 88c97eb and found nothing (no inline comments or reviews, a thumbs-up). CI on 88c97eb: push run 40168477504 and pull-request run 40168484669 passed, 501/501 each.

**Next:** Codex's review of the docs fix and CI on it; merge with Harry's go-ahead. HANDOFF's list of upstream quirks to offer already names the cleanup that outlives disposal; that it can also start seconds late is noted here.

## 2026-10-25 — Readable rows, a white-ish death count, the event strip folds into one bar

**What:** (1) Every bit of a row's text (name, combat power, deaths, DPS, share, total) sits on a crisp dark edge, `--row-edge` on `.rows` (a tight 1 px outline shadow under a soft 3 px one), and names are bold (700). (2) The death count is the meter's white-ish text (`rgba(235, 233, 242, 0.85)`), like the combat power, instead of red. (3) The world-event strip is one bar, a button (`aria-expanded`): closed, the default, it names the most urgent notice with its countdown, its progress line, "+N" for the others due and a chevron, and its tooltip lists every due notice with its local time; open, it is a "World events" header with the count over every notice. The choice is saved (`AlertSettings.NoticesExpanded`, default false, in its equality and hash) through `MeterPage.SetNoticesExpanded`. `AlertStripLayout` loses the two-shown rule (`CollapsedCount`, `HiddenCount`, `StaysExpanded`, `HiddenSummary`) for `Shown` (nothing closed, all open), `MoreCount` and `List`; the screen-reader summary still names the first two (`AnnouncedCount`).

**Why:** Harry: "white character names is asbit hard too read i think? is it more contrastly? but i like white", "just make the whole event ttimers section at the bottom hideable/exapndable instead of always showing two", and "death counter colour can be white as well? or a whiteish colour i reckon". For the names, four options were rendered with the real styles in headless Edge (no window; Harry was in game) over the brightest class bars, and the crisp edge with bolder names was chosen.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 503/503: `AlertTests` (the closed bar shows no rows and the open one every notice, "+N" never "+0", the tooltip lists every notice, closed by default), `SettingsTests.EqualityCoversEveryProperty` (now `NoticesExpanded`), `RazorMarkupTests.TheNoticeStripIsOneBarThatOpensAndCloses` and `ARowsTextSitsOnACrispDarkEdge` (new), `UpcomingNoticesUseTheSoftGold` (the bar takes the notice's colours). Fail-first, 10 mutations, each failing its test by name: no `aria-expanded`, the toggle not inverting, the overlay not binding the saved choice, two rows shown while closed, "+0" for a single notice, `NoticesExpanded` out of equality, red deaths, names at 600, a soft shadow put back on the share, the chevron not turning. Headless Edge (no window) with this branch's styles: rows over gold, green, purple and blue bars, the closed bar (an open portal, "+3"), the open list of four, and a closed bar for one upcoming event.

**Not checked:** the meter itself (Harry is in game, so no window was opened): clicking the bar, the choice surviving a restart, the window growing and shrinking as the strip opens and closes (HANDOFF). The README's `alerts.gif` still shows the old two-notice strip with "+2 more" and "Show less"; it needs re-recording, which opens windows.

**Review (deep audit at high effort, on 65400f4; verdict: approve with nits):** saved data, the Settings window writing at the same time, the debounced save, a newer build's file and the window's fit were found correct. Fixed: (1) the tests did not pin what the closed bar names (the first notice, "+N", its progress line), and the shadow guard read only selectors starting with `.row`, though `.fx .row …` rules exist: five mutations survived; the guard now pins the closed branch, `--row-edge` declared once, and any selector naming a row, comments dropped. (2) ARCHITECTURE still said "Show less" stays whole, the README's GIF alt text did not say it shows the old strip, and LESSONS still said the top line remains. (3) The bar was 24 px, a notice 26 px (24 inside its border; the bar, a button, is border-box), so "as tall as a notice" was wrong: the bar is now 26 px, pinned against the notice's height and border. (4) The closed bar's progress line glided from one notice's fill to the next's when the most urgent changed: it is keyed to its notice. (5) Comments: `alertSettings` names the open/closed choice, `Toggle` has one, a stray line break. Noted, not changed: an older build loads a newer file and saves it without `noticesExpanded`, so after a downgrade the strip comes back closed (as with earlier switches); and an open strip stays open with one notice due, a header over that notice, by design. CI: the pull_request run passed; the push run failed `HistoryTests.AFightSavedWithAnUnusableStartOrEndShowsItsPlayersOnly` (5 expected, 4), which the audit traced to upstream's history store deleting expired fights on a background task at startup, racing the test's fight dated `DateTime.MinValue`; unrelated to this change and left for its own fix.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 503/503. Fail-first, 8 mutations, each failing its test by name: the audit's five survivors (the least urgent notice named, no "+N", no progress line, a soft shadow under `.fx .row .name`, `--row-edge: none` later in `.rows`), the progress line unkeyed, the bar back at 24 px, and the bar without `border-box`. Measured in headless Edge (no window), with this branch's styles: the bar and a notice are both 386 × 26 px.

**Next:** Codex and CI on the fixes; re-record `alerts.gif` once Harry is out of game; merge with Harry's go-ahead.

## 2026-10-25 — An app icon

**What:** the meter has an icon: `src/ScarletMeter.App/Assets/ScarletMeter.ico`, the exe's `ApplicationIcon`, which every window shows on its title bar and taskbar button through WPF's default (no window sets one; see Review). Nine PNG entries, 16 to 256 px, each from the whole picture (see Replaced), high-quality downscales, a light sharpen at 48 px and below, rounded corners. `AppIconTests`; THIRD-PARTY-NOTICES.md "Artwork" names the source; ARCHITECTURE and HANDOFF updated.

**Why:** Harry wanted an icon for the exe, at first a character from Re:Zero or NIKKE. Those are copyrighted characters and the meter is public and released, so original designs were drawn instead (four emblems, three chibi mascots, a hand-drawn-style headshot); Harry then generated two portraits with GPT 6.1 Astra and asked to use the better one, with a disclaimer, noting the project is non-commercial. Of the two, the second holds up better at 16 to 32 px (crisper line work and higher contrast, especially on a light taskbar); compared at real sizes on dark and light taskbars before choosing. The resemblance of the portrait to Re:Zero's Emilia (silver hair, violet eyes, elf ears, flower hairpin, braid) and to NIKKE's Scarlet's hat was pointed out to Harry; using it is Harry's call.

**Checks (SDK 10.0.402):** `scripts/Build.ps1` 503/503. Windows' own icon decoder reads all nine sizes.

**Not checked:** the icon on the taskbar and title bars in a running meter (HANDOFF).

**Shown to Harry:** the icon at real sizes and the rebuilt portable copy, which Harry has since run (built with #30 on a local-only branch). Harry then asked to bring the branch up to date with main and push it as a PR, without merging or releasing yet. Rebased onto main after #30 and #31 (only this log conflicted; the three entries are kept in the order they were written); `scripts/Build.ps1` 507/507 after it.

**Review (deep audit at high effort, on cb4848b; verdict: approve with nits; Codex found nothing, CI passed):** (1) `Windows/AppIcon`, its `<Resource>` copy of the .ico and the two window hooks changed nothing on screen: WPF gives a window without an `Icon` the exe's own (the audit read `Window.UpdateIcon` and `IconHelper` in PresentationFramework 10.0.13, and found the icon group with all nine sizes in the Debug apphost, the dll and the portable exe). They only added ways to fail: exceptions outside the catch list, which the `Lazy` would have replayed in every window's constructor, and a decoder tied to the UI thread. Removed; the docs no longer describe a "default" fallback that was the same icon. (2) `AppIconTests` were weaker than their claim: a commented-out hook, a new window without one, a missing resource behind a comment, swapped directory sizes and a broken entry all passed. Replaced: no source file sets an icon (comments dropped), the built exe and dll carry all nine sizes (read from their resources as data), and each .ico entry is checked against its PNG header and decoded by WPF. (3) Documentation: ARCHITECTURE's folder table gains `Assets/`; THIRD-PARTY-NOTICES says the portrait was also sharpened and given rounded corners. (4) `.gitattributes` marks `*.ico` binary. Left for Harry: whether "Artwork" should state a licence for the portrait, and whether this log should keep the note on its resemblance to existing characters.

**Checks after the review (SDK 10.0.402):** `scripts/Build.ps1` 509/509, no warnings outside upstream. Fail-first, six mutations, each failing its test by name: the boss-timers window setting `Icon`, a window setting it in an initializer, the `<Resource>` copy put back, no `ApplicationIcon` (all three icon tests that read the build or the project fail), the 32 and 64 px directory entries swapped, and a broken 32 px entry (WPF drops it); a setter only in a comment passes, as it should.

**Merged main after #33:** only LESSONS conflicted (both added entries at the top); all are kept, newest first. `scripts/Build.ps1` 510/510; Codex found nothing on b5fc73d and CI passed.

**Replaced:** Harry replaced the portrait with a pixel-art one, also generated with GPT 6.1 Astra, with the meter's bars in a corner. A first version showed a wider scene; three ways to draw its small sizes were compared at real size on dark and light taskbars (the whole picture, a face crop, the bars badge alone), and Harry chose the whole picture, then found it hard to read on the taskbar and generated a close-up: the face fills the frame and the bars are larger. Compared with the wider scene at real size, its face and eyes read at 24 and 32 px and the bars from 20 px. A last version enlarged the bars badge, which then stays a clear red meter block from 20 px up and reads as a meter at 24 and 32 px; that is the icon, whole at every size. The .ico has the same nine sizes (184,273 bytes, no text or EXIF chunks); THIRD-PARTY-NOTICES and ARCHITECTURE describe it. Like the first, it resembles an existing character (NIKKE's Scarlet: silver hair, red eyes, a wide white hat, red tassels), which was pointed out to Harry; using it is Harry's call.

**Next:** Codex and CI on the new icon; Harry's look at it in a rebuilt copy; merge and a release with Harry's go-ahead.

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

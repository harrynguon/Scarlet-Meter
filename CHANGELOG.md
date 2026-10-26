# Changelog

What changed for players in each release of Scarlet Meter, newest first. Every pull request that changes what a
player sees or does adds a line under **Unreleased** (AGENTS.md, "Changelog"); a release turns those lines into its
own section, and the release workflow adds that section to the release's notes on GitHub, so each release lists what
changed. Changes for developers only (tests, CI, these docs) are in [docs/WORKLOG.md](docs/WORKLOG.md). The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions below 1.0 are pre-releases.

## [Unreleased]

### Added

- Settings, About names the Artifact Corridor, the two-minute instance you enter from the Abyss. The meter keeps
  tracking there; it pauses again once you are back in the Abyss. ([#47](https://github.com/harrynguon/Scarlet-Meter/pull/47))
- A character roster: every character you log in on with the meter running, with its Odyle energy, when basic
  energy is full again, and Nightmare and Ascension entries left, estimated (≈) from what the game last said. Click
  the Odyle energy beside the ping, or Settings, Appearance, Characters. The recharge times are not yet checked in the
  game. ([#48](https://github.com/harrynguon/Scarlet-Meter/pull/48))

## [0.3.0] - 2026-10-26

### Added

- Odyle energy beside the ping, as the game shows it: basic energy, then charged in brackets. "Show Odyle energy"
  in Settings, Appearance turns it off.
  ([#34](https://github.com/harrynguon/Scarlet-Meter/pull/34), [#36](https://github.com/harrynguon/Scarlet-Meter/pull/36))
- The meter pauses in the Abyss, whose crowds slow it down: it tracks and keeps nothing there, and its header reads
  "In the Abyss · meter paused". It tracks again once you leave. "Pause in the Abyss" in Settings, Tracking turns it
  off. ([#42](https://github.com/harrynguon/Scarlet-Meter/pull/42))
- Settings, About shows the map you are on: its name, where known, and its id.
  ([#42](https://github.com/harrynguon/Scarlet-Meter/pull/42))

### Changed

- "Boss encounters only" (Settings, Tracking) is on by default, so a new install tracks boss fights and training
  dummies, not every mob. If you already use the meter, it stays as you had it (off unless you turned it on). ([#43](https://github.com/harrynguon/Scarlet-Meter/pull/43))
- World events are one row of icons at the bottom of the meter instead of a list: one for each event you are alerted
  to, lit gold with the minutes and seconds to its start ("2:19") once it is within your warning time, and teal while
  its entry or portal is open. Hover over an icon to see what it is, where and when; click icons to open a line for
  each with its countdown, and click again to close it. ([#39](https://github.com/harrynguon/Scarlet-Meter/pull/39))
- With "Show the next event" on and nothing due, the next event's icon shows the time to its start, dimly, instead of
  a line. ([#39](https://github.com/harrynguon/Scarlet-Meter/pull/39))
- The events come in a new order (Shugo Festival, Dimensional Invasion, Spacetime Rift, Abyss Siege, then the field
  bosses and the resets), in Settings too, and the Artifact Siege is now the Abyss Siege.
  ([#39](https://github.com/harrynguon/Scarlet-Meter/pull/39))
- The bar area sizes itself: no bars until there is someone to show, then a bar for each player, up to five, and the
  last fight's bars stay until the next. It shrinks only after 15 seconds of needing fewer bars, so switching between
  a boss and its adds does not resize the meter. With more players the top five show and the rest scroll, your own bar
  pinned in view; the area ends at the fifth bar, nothing of the next one showing below it. Only the meter's width can
  be resized now.
  ([#35](https://github.com/harrynguon/Scarlet-Meter/pull/35), [#37](https://github.com/harrynguon/Scarlet-Meter/pull/37), [#40](https://github.com/harrynguon/Scarlet-Meter/pull/40), [#44](https://github.com/harrynguon/Scarlet-Meter/pull/44))
- Other players' fights on a training dummy are ignored, neither shown nor saved to History, unless you hit that
  dummy too (then everyone on it counts, as in any fight). Until the meter has seen your character (your own bar's
  ring is the sign), every dummy fight counts as before. ([#38](https://github.com/harrynguon/Scarlet-Meter/pull/38))

## [0.2.0] - 2026-10-25

### Added

- An app icon on the exe and every window, and Scarlet's face at the start of the overlay's header. Its ring follows
  the boss's HP: green above 66%, yellow from 33%, red below, its glow going out when nothing has hit the boss for a
  while, grey once it is down. ([#32](https://github.com/harrynguon/Scarlet-Meter/pull/32))
- A boss-timers window: the hourglass in the header opens aion2timers.com's world-boss timers in a small window of
  their own, which remembers your server and faction. ([#29](https://github.com/harrynguon/Scarlet-Meter/pull/29))

### Changed

- Readable rows: names, numbers and death counts sit on a crisp dark edge, names are bold, the death count is
  white-ish, the class icons are a little larger, and each row reads DPS, then the share over the total damage. Your
  own row is ringed instead of striped.
  ([#27](https://github.com/harrynguon/Scarlet-Meter/pull/27), [#28](https://github.com/harrynguon/Scarlet-Meter/pull/28), [#30](https://github.com/harrynguon/Scarlet-Meter/pull/30), [#32](https://github.com/harrynguon/Scarlet-Meter/pull/32))
- The world-event notices fold into one slim bar under the rows (click it for the list; it stays as you left it),
  never cover a bar, use a calmer gold and step aside during boss fights.
  ([#28](https://github.com/harrynguon/Scarlet-Meter/pull/28), [#30](https://github.com/harrynguon/Scarlet-Meter/pull/30))
- Nothing in the overlay scrolls: the meter grows to fit and returns to your size.
  ([#26](https://github.com/harrynguon/Scarlet-Meter/pull/26), [#28](https://github.com/harrynguon/Scarlet-Meter/pull/28))
- A crown marks a boss. ([#28](https://github.com/harrynguon/Scarlet-Meter/pull/28))

### Fixed

- The background picture fits the panel again, whatever the window's height.
  ([#33](https://github.com/harrynguon/Scarlet-Meter/pull/33))
- Scrolling a table in the details or history window no longer shows rows above its header.
  ([#33](https://github.com/harrynguon/Scarlet-Meter/pull/33))
- Spiritmaster is named as in the game. ([#27](https://github.com/harrynguon/Scarlet-Meter/pull/27))

## [0.1.0] - 2026-10-25

### Added

- The first public build: an overlay with a boss card (HP and a "Kill in" estimate) and each player's DPS, damage,
  share and combat power in class colours.
  ([#2](https://github.com/harrynguon/Scarlet-Meter/pull/2), [#21](https://github.com/harrynguon/Scarlet-Meter/pull/21), [#24](https://github.com/harrynguon/Scarlet-Meter/pull/24))
- Animated bar effects for each class (slashes, arrows, fire and ice, poison...), a glide when someone overtakes, and
  switches to tone them down or off. ([#21](https://github.com/harrynguon/Scarlet-Meter/pull/21))
- Player details (skills, buffs, damage graph, combat log) and a history of saved boss fights.
  ([#5](https://github.com/harrynguon/Scarlet-Meter/pull/5), [#8](https://github.com/harrynguon/Scarlet-Meter/pull/8))
- A background picture with its own opacity. ([#22](https://github.com/harrynguon/Scarlet-Meter/pull/22))
- A global hotkey to hide and show the meter (Ctrl+Shift+D by default).
  ([#2](https://github.com/harrynguon/Scarlet-Meter/pull/2))
- World-event notices before world events and field bosses, which step aside during boss fights (Global preset
  times, not yet verified in game).
  ([#3](https://github.com/harrynguon/Scarlet-Meter/pull/3), [#6](https://github.com/harrynguon/Scarlet-Meter/pull/6), [#20](https://github.com/harrynguon/Scarlet-Meter/pull/20), [#24](https://github.com/harrynguon/Scarlet-Meter/pull/24))
- A quiet line in the overlay when a newer RATmeter release is out.
  ([#4](https://github.com/harrynguon/Scarlet-Meter/pull/4), [#10](https://github.com/harrynguon/Scarlet-Meter/pull/10))
- A portable copy: unzip it anywhere and run it, settings, history and logs beside the exe.
  ([#15](https://github.com/harrynguon/Scarlet-Meter/pull/15), [#19](https://github.com/harrynguon/Scarlet-Meter/pull/19))

[Unreleased]: https://github.com/harrynguon/Scarlet-Meter/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/harrynguon/Scarlet-Meter/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/harrynguon/Scarlet-Meter/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/harrynguon/Scarlet-Meter/releases/tag/v0.1.0

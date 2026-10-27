# Third-party notices

Scarlet Meter (GPLv3, see `LICENSE` and `NOTICE`; `LICENSE.txt` and `NOTICE.txt` in the release zip) is built from
the following. The release zip carries this file, the licence and notice texts below in its `licenses` folder, and
the GPL beside `ScarletMeter.exe`. The complete source of a release is attached to it as
`ScarletMeter-<version>-source.zip`, RATmeter's code included; it is also the tagged commit in
https://github.com/harrynguon/Scarlet-Meter with the `upstream/` submodule at the commit that commit records.

## Licence texts

`licenses/` (in this repository and in the release zip) holds the full text of every licence named here:

- `MIT.txt`: the MIT licence, with the copyright notice of each MIT-licensed component.
- `Apache-2.0.txt` and `MPL-2.0.txt`: the Apache License 2.0 and the Mozilla Public License 2.0.
- `packages/`: every licence, notice and third-party-notices file the NuGet packages ship, verbatim (the table below
  says which package ships which), among them Microsoft.Web.WebView2's BSD-style licence and its notices.
- `dotnet-wpf/THIRD-PARTY-NOTICES.TXT`: WPF's third-party notices (dotnet/wpf at v10.0.13), since the Windows
  Desktop runtime pack ships only its licence.
- `runtime/` (release zip only): the licence and third-party notices of the .NET runtime packs bundled in
  `ScarletMeter.exe`, copied from the very packs the release was built with.

## Code

- **RATmeter** (packet capture, decoding, game data, class icons), https://github.com/Kuroukihime/AIon2-Dps-Meter,
  Copyright (c) 2026 Kuroukihime, GNU GPL v3. Included unmodified as the `upstream/` submodule at a release tag.
- **harrynguon/AIon2-Dps-Meter** (the World Companion's schedule engine, `DeferredWindowClose`), a fork of RATmeter,
  https://github.com/harrynguon/AIon2-Dps-Meter, GNU GPL v3.
- **A2Tools DPS Meter** (the layout of the game's list of entry counts, Odyle energy among them, and how to find it,
  `ScarletMeter.Engine/Entries/EntryListRecords`; and whose the counts are, the character the next self record names,
  `ScarletMeter.Engine/Upstream/EntryListTap`), https://github.com/taengu/A2Tools-DPS-Meter: the Odyle entry from pull
  request #63, merged as 94dda50fe5c324bb02fae72d25d0533cb0ef12cd, and its fixes
  2723bff70cb7154442f32ca4e4c484d4cf4f62d6 (the one-value entry) and 29e7193a193c24980ce9ad1bd16aa56d2a3196e1
  (charged-only entries, updates that start 00); the whole list by entry id (`content_entry_at` and the list and update
  readers in `src-tauri/src/capture/stream_processor.rs`, the owner rule in `src-tauri/src/combat/data_storage.rs`) from
  b79969aa35cc8d4cd402b63de07ce1733dbe7807, 2149936b642a6c9328622166937d59030888a5e4 (a login's list that does not
  read, updates that must read whole) and caa7ed4a1947f6cfed8f609595b07b43c1787575 (time entries), unchanged in those
  readers up to b93353150ffef43bff1276f4fc609b2fd6e6e38f (read on 2026-10-27); and the
  layout of the game's map-loaded record (`ScarletMeter.Engine/Maps/MapLoadRecords`), from `parse_map_load_packet` in
  `src-tauri/src/capture/stream_processor.rs`, added in b1fcd53fd676ad78224f4c89c543d744b0ac3766 and unchanged up to
  6abf5e7e28ddae93e04e870087d2ae457f752681 (v2.0.79, read on 2026-10-26); and the layout of the Abyss map's record and
  the one on entering the Abyss, who holds each artifact (`ScarletMeter.Engine/Abyss/AbyssArtifactRecords`), from
  `src-tauri/src/capture/abyss_artifacts.rs`, added in 6837e554919141e130adf08e57ccfb534714e1c0 and unchanged up to
  c3fcce31da487fb5146c653a38640e477536bd68 (read on 2026-10-27), with its side of a server by the id's thousands;
  and two observations behind the capture lock's check (`ScarletMeter.Engine/Capture/CaptureLockWatch`), taken as
  facts, not code: that the client asks for a ping every ten seconds (`src-tauri/src/combat/ping_tracker.rs`) and that
  the game has never used the web's ports, 80 and 443 (`capture_filters` in `src-tauri/src/capture/pcap_capturer.rs`),
  at 5c6f02cddd81ea1493295d76ef6adccc851da5d5 (read on 2026-10-27); and the class code after the server id in the
  player's own character record, four codes to a class, and the level and gear score after it with their bounds
  (`ScarletMeter.Engine/Entries/SelfRecords`), from `self_profile` and the masked-identity scan in
  `src-tauri/src/capture/stream_processor.rs` and `JobClass::from_roster_class` in `src-tauri/src/entity/job_class.rs`
  at the same commit (read on 2026-10-27); and that entry 1 of the list of entry counts is the Shugo Festival keys
  (its list of known ids in `character()`, `src-tauri/src/plugins/mod.rs`, at the same commit), taken as a fact; and
  which server ids exist, Korea's and Taiwan's and the Global servers NC has opened, by faction, region and slot
  (`ScarletMeter.Engine/Entries/GameServers`), from `exists` in `src-tauri/src/capture/servers.rs`, added in that
  commit (read on 2026-10-27), its rule ported and its open slots taken as facts;
  GNU GPL v3: its Rust parsing ported to C#. No captured bytes were taken; Scarlet's tests use synthetic records.
- **SkeeveAN/Aion-DPS-Meter** (the layout of the game's inventory records and how to find an entry in them by its anchor,
  `ScarletMeter.Engine/Entries/InventoryRecords`), https://github.com/SkeeveAN/Aion-DPS-Meter, `Client/Aion2/Aion2Stock.cs`
  as of 010e994c5 (its only commit, unchanged up to 729cb910494a1a0a7b837b8914c98921ce512fd4, read on 2026-10-27),
  Copyright (c) 2026 SkeeveTV, MIT (`licenses/MIT.txt`): its C# reading rewritten for Kina alone. The item ids of Kina
  (930100031) and Kina (Bound) (930100001) are from its `Client/assets/aion2/items/item_names.json` (the client's item
  table) at the same commit. No captured bytes were taken; Scarlet's tests use synthetic records.
- **.NET** (the runtime bundled in the self-contained `ScarletMeter.exe`, with WPF), Copyright (c) .NET Foundation
  and Contributors, MIT. Its licence and third-party notices are in the release zip's `licenses/runtime/`, and
  WPF's in `licenses/dotnet-wpf/`; for .NET 10 they are also at
  https://github.com/dotnet/runtime/blob/release/10.0/THIRD-PARTY-NOTICES.TXT and
  https://github.com/dotnet/wpf/blob/release/10.0/THIRD-PARTY-NOTICES.TXT. The ASP.NET Core Components and
  BlazorWebView packages ship their own notices (`licenses/packages/`; also
  https://github.com/dotnet/aspnetcore/blob/release/10.0/THIRD-PARTY-NOTICES.txt and
  https://github.com/dotnet/maui/blob/main/THIRD-PARTY-NOTICES.TXT).
- **Windows SDK projection** (`Microsoft.Windows.SDK.NET` and `WinRT.Runtime`, bundled in `ScarletMeter.exe` from
  the Microsoft.Windows.SDK.NET.Ref pack that the Windows 10 SDK target framework brings in, as in RATmeter's own
  builds). BlazorWebView draws the see-through overlay with WebView2's composition control, which needs it at run
  time. `Microsoft.Windows.SDK.NET`: Copyright (c) Microsoft Corporation, under the Windows SDK licence terms,
  https://aka.ms/WinSDKLicenseURL. `WinRT.Runtime` is C#/WinRT, MIT (`licenses/MIT.txt`).

## Data

- **World event schedules (Global preset)**: times and window lengths follow taengu/A2Tools-Plugins event-timers
  data at b11f345c93494a2b6a77142002dc26a656b610d2 (https://github.com/taengu/A2Tools-Plugins), GNU GPL v3:
  schedule parameters only, no code.
- **Character roster rules** (`ScarletMeter.App/Roster/RosterRules`): basic Odyle energy's refill (+15 every three
  hours on the hours that meet the daily reset, the 560 and 840 caps), the entry ids of Nightmare and Ascension Trial
  and their caps and recharges, and which Artifact Corridors a side can run (one per lower-layer artifact it holds, one
  nobody holds closed to both, one not named open), from the Roster / Content Overview plugin by Nullception in
  taengu/A2Tools-Plugins, `plugins/alt-roster` (`logic.js` and `plugin.js`) at 7a4720bf6af8adfe1fe03359540da007e9566c71
  (https://github.com/taengu/A2Tools-Plugins), GNU GPL v3: the rules only, written anew in C#. The Shugo Festival keys'
  rules (`ScarletMeter.Engine/Contracts/CharacterProgress` and the overlay's keys: shared by a server's characters, the
  two values the keys left and the extra, +3 a day up to 21) are from the same plugin's content table, its `shugo` row
  in `plugin.js` at 48f5ecd0885134ab8df2651410378b9ad9a639cc (read on 2026-10-27), taken as facts.
- **Map names** (`ScarletMeter.Engine/Maps/MapNames`, shown in Settings, About): the dungeons' names and
  difficulties from taengu/A2Tools-DPS-Meter's `src/data/i18n/dungeons/en.json` at
  6abf5e7e28ddae93e04e870087d2ae457f752681 (last changed in f942a54b68c5a5481cf7d4d2ad59a4c00de191fb), GNU GPL v3;
  the open world's and the Abyss's names from SkeeveAN/Aion-DPS-Meter's `Client/assets/aion2/bosses/field_bosses.json`
  at 23ec79e997b9a0c9e5eb1f322a08337fa6e2803e (last changed in f8d80684419d0ad58ddd0954ec7c3e17b6e52860), MIT,
  Copyright (c) 2026 SkeeveTV (`licenses/MIT.txt`), with the map ids yykk62/AetherDPS's `tools/build-field-bosses.py`
  gives them at 65edf72f08e08942cf3ecb832c9a222f05cd8b62, GNU GPL v3. Names and ids only, no code. One name, the
  Artifact Corridor (503001), comes from the player's own game, not from a source.
- **Legacy schedule defaults**: adapted from Byanski/aion2timeroverlay at 2a5727ecdf5487e9becb6b3a756819f9d14ff564
  (https://github.com/Byanski/aion2timeroverlay), whose `package.json` declares MIT (author Byanski); the snapshot
  has no separate licence text.
- **Game data and class icons** come from RATmeter's repository, as RATmeter ships them. AION 2's names, artwork
  and data belong to NCSOFT; Scarlet Meter is not affiliated with or endorsed by NCSOFT. Skill and buff icons are
  not shipped: the meter loads them from NCSOFT's game-data site at run time, as RATmeter does, and the "Skill
  icons" setting turns that off.

## Artwork

- **App icon** (`src/ScarletMeter.App/Assets/ScarletMeter.ico`, the exe's and every window's icon): an AI-generated
  pixel-art portrait, made for this project by its owner (harrynguon) with GPT 6.1 Astra on 2026-10-25, then resized
  into the icon's sizes, lightly sharpened at the small sizes and given rounded corners. It is not affiliated with or
  endorsed by any game, anime or studio. Scarlet Meter is a free, non-commercial project.
- **Header face** (`src/ScarletMeter.App/wwwroot/images/avatar.png`, at the left of the meter's header): the same
  portrait without the meter badge, also made by the project's owner with GPT 6.1 Astra on 2026-10-25, cropped to the
  face and resized to 64 px.

## NuGet packages

Every package that ships in the published program (build-only analyzers and tasks are left out), with the licence
its package declares and the licence and notice files it ships. An SPDX licence's text is in `licenses/`; Apache-2.0
and MPL-2.0 are compatible with GPLv3. PacketDotNet (MPL-2.0) is unmodified; its source is at
https://github.com/dotpcap/packetnet/tree/v1.4.7. K4os.Compression.LZ4 links its licence file, which is MIT
(`licenses/MIT.txt`). Microsoft.Web.WebView2 is under Microsoft's BSD-style licence shipped in that package; SQLite is
in the public domain. Generated with the copies in `licenses/packages/` by `scripts/Write-ThirdPartyNotices.ps1` from
the app's restore; `ThirdPartyNoticesTests` fails when a shipped package is missing here, a row is stale, or a copy
differs from what its package ships.

<!-- packages:begin -->
Target `net10.0-windows10.0.20348.0`, 59 packages.

| Package | Version | Licence | Its own notices, in `licenses/packages/` | Project |
| --- | --- | --- | --- | --- |
| K4os.Compression.LZ4 | 1.3.8 | <https://github.com/MiloszKrajewski/K4os.Compression.LZ4/blob/master/LICENSE?raw=true> |  | <https://github.com/MiloszKrajewski/K4os.Compression.LZ4> |
| Microsoft.AspNetCore.Authorization | 10.0.0 | MIT | Microsoft.AspNetCore.Authorization.THIRD-PARTY-NOTICES.TXT | <https://asp.net/> |
| Microsoft.AspNetCore.Components.Forms | 10.0.0 | MIT | Microsoft.AspNetCore.Components.Forms.THIRD-PARTY-NOTICES.txt | <https://asp.net/> |
| Microsoft.AspNetCore.Components.Web | 10.0.0 | MIT | Microsoft.AspNetCore.Components.Forms.THIRD-PARTY-NOTICES.txt | <https://asp.net/> |
| Microsoft.AspNetCore.Components.WebView.Wpf | 10.0.0 | MIT | Microsoft.AspNetCore.Components.WebView.Wpf.LICENSE.TXT, Microsoft.AspNetCore.Components.WebView.Wpf.THIRD-PARTY-NOTICES.TXT | <https://github.com/dotnet/maui> |
| Microsoft.AspNetCore.Components.WebView | 10.0.0 | MIT | Microsoft.AspNetCore.Components.Forms.THIRD-PARTY-NOTICES.txt | <https://asp.net/> |
| Microsoft.AspNetCore.Components | 10.0.0 | MIT | Microsoft.AspNetCore.Components.Forms.THIRD-PARTY-NOTICES.txt | <https://asp.net/> |
| Microsoft.AspNetCore.Metadata | 10.0.0 | MIT | Microsoft.AspNetCore.Authorization.THIRD-PARTY-NOTICES.TXT | <https://asp.net/> |
| Microsoft.Data.Sqlite.Core | 10.0.2 | MIT |  | <https://docs.microsoft.com/dotnet/standard/data/sqlite/> |
| Microsoft.EntityFrameworkCore.Abstractions | 10.0.2 | MIT |  | <https://docs.microsoft.com/ef/core/> |
| Microsoft.EntityFrameworkCore.Relational | 10.0.2 | MIT |  | <https://docs.microsoft.com/ef/core/> |
| Microsoft.EntityFrameworkCore.Sqlite.Core | 10.0.2 | MIT |  | <https://docs.microsoft.com/ef/core/> |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.2 | MIT |  | <https://docs.microsoft.com/ef/core/> |
| Microsoft.EntityFrameworkCore | 10.0.2 | MIT |  | <https://docs.microsoft.com/ef/core/> |
| Microsoft.Extensions.Caching.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Caching.Memory | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.Binder | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.Json | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Configuration | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.DependencyInjection | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.DependencyModel | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Diagnostics | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.FileProviders.Composite | 10.0.0 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.FileProviders.Embedded | 10.0.0 | MIT |  | <https://asp.net/> |
| Microsoft.Extensions.FileProviders.Physical | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Hosting | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.Abstractions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.Configuration | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.Console | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.Debug | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.EventLog | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging.EventSource | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Logging | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Options | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Primitives | 10.0.2 | MIT | Microsoft.Extensions.Caching.Abstractions.THIRD-PARTY-NOTICES.TXT | <https://dot.net/> |
| Microsoft.Extensions.Validation | 10.0.0 | MIT | Microsoft.AspNetCore.Authorization.THIRD-PARTY-NOTICES.TXT | <https://asp.net/> |
| Microsoft.JSInterop | 10.0.0 | MIT | Microsoft.AspNetCore.Authorization.THIRD-PARTY-NOTICES.TXT | <https://asp.net/> |
| Microsoft.Web.WebView2 | 1.0.3179.45 | file: LICENSE.txt | Microsoft.Web.WebView2.LICENSE.txt, Microsoft.Web.WebView2.NOTICE.txt | <https://aka.ms/webview> |
| PacketDotNet | 1.4.7 | MPL-2.0 |  | <https://github.com/dotpcap/packetnet> |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 |  | <https://github.com/serilog/serilog-extensions-hosting> |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 |  | <https://github.com/serilog/serilog-extensions-logging> |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |  | <https://github.com/serilog/serilog-sinks-file> |
| Serilog | 4.3.0 | Apache-2.0 |  | <https://serilog.net/> |
| SharpPcap | 6.3.0 | MIT |  | <https://github.com/chmorgan/sharppcap> |
| SQLite | 3.53.4 | file: LICENSE.txt | SQLite.LICENSE.txt | <https://sqlite.org/> |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.5 | Apache-2.0 |  |  |
| SQLitePCLRaw.core | 3.0.5 | Apache-2.0 |  |  |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.5 | Apache-2.0 |  |  |
<!-- packages:end -->

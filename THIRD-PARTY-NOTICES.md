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
- **Legacy schedule defaults**: adapted from Byanski/aion2timeroverlay at 2a5727ecdf5487e9becb6b3a756819f9d14ff564
  (https://github.com/Byanski/aion2timeroverlay), whose `package.json` declares MIT (author Byanski); the snapshot
  has no separate licence text.
- **Game data and class icons** come from RATmeter's repository, as RATmeter ships them. AION 2's names, artwork
  and data belong to NCSOFT; Scarlet Meter is not affiliated with or endorsed by NCSOFT. Skill and buff icons are
  not shipped: the meter loads them from NCSOFT's game-data site at run time, as RATmeter does, and the "Skill
  icons" setting turns that off.

## Artwork

- **App icon** (`src/ScarletMeter.App/Assets/ScarletMeter.ico`, the exe's and every window's icon): an AI-generated
  portrait, made for this project by its owner (harrynguon) with GPT 6.1 Astra on 2026-10-25, then cropped, resized
  into the icon's sizes, lightly sharpened at the small sizes and given rounded corners. It is not affiliated with or endorsed by any game, anime or studio. Scarlet Meter is a free,
  non-commercial project.

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

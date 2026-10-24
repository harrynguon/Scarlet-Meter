Scarlet Meter
=============

A DPS meter for AION 2 (Windows), with its own overlay on top of RATmeter's packet decoding.
Source, issues and releases: https://github.com/harrynguon/Scarlet-Meter

Before you start
----------------
- Windows 11, 64-bit. Windows 10 has not been tried; it may need the WebView2 Runtime from
  https://developer.microsoft.com/microsoft-edge/webview2/ first.
- Npcap, from https://npcap.com. In its installer, tick "Install Npcap in WinPcap API-compatible Mode", and leave
  "Restrict Npcap driver's access to Administrators only" unticked.
- Nothing else: .NET is built into ScarletMeter.exe. Windows 11 already has the WebView2 runtime the windows use.

Run it
------
1. Unzip anywhere you can write to, such as Documents or Desktop (not Program Files). You get a ScarletMeter folder.
2. Start the game, then double-click ScarletMeter.exe in that folder. It finds the game's connection by itself.
3. Settings, history and logs stay in the "data" folder beside the exe, so the ScarletMeter folder can be moved or
   deleted as one. (A copy installed from the source with Install-Meter.ps1 keeps them in
   %LocalAppData%\ScarletMeter instead.)

Ctrl+Shift+D hides and shows the meter (changeable in Settings).

To look around without the game: in this folder, Shift+right-click an empty space, choose "Open in Terminal",
then run   .\ScarletMeter.exe --demo   for made-up boss fights.

Update
------
Close the meter. Extract the newer zip into the folder that HOLDS your ScarletMeter folder (for example Documents,
if the meter is at Documents\ScarletMeter), replacing files. In Explorer's "Extract all", change the suggested
destination (a new folder named after the zip) to that folder, or you get a second, empty copy. The data folder is
kept.

Please read
-----------
- This is an early release: its in-game checks are not finished, so numbers may be wrong.
- The meter only reads network traffic, as RATmeter does; it does not touch the game's process or files. Even so,
  NCSOFT's terms forbid unauthorised programs and can lead to an account ban. Using it is at your own risk.
- AION 2 and its names, artwork and data belong to NCSOFT. Scarlet Meter is not affiliated with or endorsed by
  NCSOFT.
- Scarlet Meter is free software under the GNU GPL v3 (LICENSE.txt, NOTICE.txt). It includes RATmeter by
  Kuroukihime (https://github.com/Kuroukihime/AIon2-Dps-Meter); the other parts are in THIRD-PARTY-NOTICES.md and
  their licence texts and notices in the licenses folder. The complete source is on the release page.

Scarlet Meter
=============

A DPS meter for AION 2 (Windows), with its own overlay on top of RATmeter's packet decoding.
Source, issues and releases: https://github.com/harrynguon/Scarlet-Meter

Before you start
----------------
- Windows 11, 64-bit (Windows 10 1607 or later should work but has not been tried).
- Npcap, from https://npcap.com. In its installer, tick "Install Npcap in WinPcap API-compatible Mode", and leave
  "Restrict Npcap driver's access to Administrators only" unticked.
- Nothing else: .NET is built into ScarletMeter.exe. Windows 11 already has the WebView2 runtime the windows use.

Run it
------
1. Unzip this folder anywhere you can write to, such as Documents or Desktop (not Program Files).
2. Start the game, then double-click ScarletMeter.exe. It finds the game's connection by itself.
3. Settings, history and logs stay in the "data" folder beside the exe, so the folder can be moved or deleted as
   one. To update, close the meter and unzip a newer release over this folder, replacing files; "data" is kept.

Ctrl+Shift+D hides and shows the meter (changeable in Settings). To look around without the game, run
ScarletMeter.exe --demo from a command prompt in this folder.

Please read
-----------
- This is an early release, not yet tried in the game by its author. Numbers may be wrong.
- The meter only reads network traffic, as RATmeter does; it does not touch the game's process or files. Even so,
  NCSOFT's terms forbid unauthorised programs and can lead to an account ban. Using it is at your own risk.
- AION 2 and its names, artwork and data belong to NCSOFT. Scarlet Meter is not affiliated with or endorsed by
  NCSOFT.
- Scarlet Meter is free software under the GNU GPL v3 (LICENSE.txt, NOTICE.txt). It includes RATmeter by
  Kuroukihime (https://github.com/Kuroukihime/AIon2-Dps-Meter); the other parts are in THIRD-PARTY-NOTICES.md.

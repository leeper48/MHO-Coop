MHO Couch Co-op
===============
Two Marvel Heroes Omega games side by side on one screen, with one gamepad locked to each.

What you need
-------------
- Windows 10 or 11, 64-bit. Nothing to install for this app itself.
- Marvel Heroes Omega, with an account for each player on your server.
- Two XInput gamepads (Xbox pads, or anything Windows sees as one).
- ProtoInput 1.1.2 or newer: https://github.com/ilyaki/ProtoInput/releases
  Unzip it anywhere. This app only needs to be told where that folder is.

First run
---------
1. Start MhoCoop.exe.
2. Open Settings (top right):
   - Game program: MarvelHeroesOmega.exe, in the game's UnrealEngine3\Binaries\Win64 folder.
     A Steam install in the usual place is found automatically.
   - ProtoInput folder: press Browse and select ProtoInputHost.exe where you unzipped ProtoInput.
   - Server address: your server's SiteConfig address.
3. Press Start (or Enter, or A on a gamepad).
4. Two games open side by side. Each player logs in on the game's own login screen:
   click a game to type into it.

Playing
-------
- RB on either pad brings both games to the front.
- D-pad up or down puts the mouse cursor over that pad's game.
- Mouse and keyboard go to whichever game you clicked last.
- The app sits as an icon near the clock and closes by itself when both games are closed.

No account information is stored
--------------------------------
This app presently doesn't store any account information. Logging in happens only inside
the game.

Updates
-------
On first start the app asks whether it may look for new versions. If you say yes, it asks GitHub at
start (at most once an hour); nothing about you or your games is sent. A new version shows as a green
"Update Available" button in the menu. Updates are only installed when they carry the developer's
signature, and the old version is put back if anything goes wrong. Settings has the on/off switch and a
"Check Now" button.

Good to know
------------
- Any screen size works, 1080p included. Each game gets half of the main display, above the taskbar:
  about 960 pixels wide per player on a 1080p screen, 1920 on a 4K one.
- Settings (game location, ProtoInput folder, server address, gamepad numbers) are kept in
  %AppData%\MhoCoop. To keep them next to the program instead, put an empty file named MhoCoop.ini
  beside MhoCoop.exe before the first run.
- If linking the gamepads fails, check that your antivirus is not blocking ProtoInput, and that the
  game and this app are both run the same way (both normally, or both as administrator).
- MhoCoop.log in the settings folder records what happened in the last session.

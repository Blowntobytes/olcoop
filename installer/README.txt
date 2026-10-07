olcoop - Overload campaign co-op (up to 3 players)
==================================================

What you need
  - Overload on Steam, and Steam running.
  - olmod 0.5.14 (https://olmod.overloadmaps.com), usually in your Overload folder - another folder works too.
  - Host and every player must run the SAME olcoop version (shown top-left on the main menu).

Install
  1. Unzip this folder anywhere.
  2. Run install.bat. It looks for olmod (the folder with olmod.exe and GameMod.dll): your Overload folder on every drive,
     shortcuts to olmod.exe on the Desktop / Start Menu / taskbar, and the usual folders. It asks for the folder if it can't
     find it, copies the mod there (olmod loads mods from its own folder) and checks the copy.
     It also puts "olcoop", "olcoop SteamVR" and "olcoop Oculus" shortcuts (orange and black olcoop icon) on your Desktop.

Play
  1. Start the game with the olcoop shortcut, or olcoop.bat in your olmod folder. Do not start it from Steam's Play button:
     that runs the game without olmod.
     VR: SteamVR headsets - start SteamVR, then "olcoop SteamVR" (olcoop-steamvr.bat, -vrmode openvr).
         Oculus/Meta headsets - start the Oculus app, then "olcoop Oculus" (olcoop-oculus.bat, -vrmode oculus).
  2. Main menu, bottom right: CO-OP: HOST / JOIN.
     Host:   HOST A CO-OP GAME, invite your friends from the list, then BACK and start or continue the campaign
             (PLAY MISSION / LOAD SAVED GAME) as usual.
     Friend: accept the host's Steam invite (game already running), or open CO-OP and pick the friend who is hosting.
             You are taken into the host's level automatically.
  No port forwarding is needed: the connection goes through Steam.

Between levels
  Everyone sees the results and upgrade screens. Joiners press READY UP; the host's next level starts when
  everybody is ready.

Logs (for bug reports): olcoop_logs in your olmod folder (usually Overload\olcoop_logs)
Uninstall: run uninstall.bat (olmod and the game are not touched).

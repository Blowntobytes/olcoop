olcoop - Overload campaign co-op (up to 3 players)
==================================================

What you need
  - Overload on Steam, and Steam running.
  - Nothing else: olcoop brings its own, unmodified olmod 0.5.14 (MIT licence, see olcoop\olmod-LICENSE.txt).
    Your own olmod, if you have one, is not touched and keeps working for normal multiplayer.
  - Host and every player must run the SAME olcoop version (shown on the main menu).

Install
  1. Unzip this whole folder anywhere.
  2. Run install.bat. It finds your Overload folder (Steam libraries on every drive, shortcuts) or asks for it, copies the
     olcoop folder (olcoop + its olmod) to Overload\olcoop and checks every file. An older olcoop (0.7.x) in the Overload
     folder is removed, so your regular olmod no longer loads it.
     It also puts "olcoop", "olcoop SteamVR" and "olcoop Oculus" shortcuts (orange and black olcoop icon) on your Desktop.

Play
  1. Start the game with the olcoop shortcut, or olcoop.bat in Overload\olcoop. Not with Steam's Play button (no olmod)
     and not with your regular olmod (no olcoop).
     VR: SteamVR headsets - start SteamVR, then "olcoop SteamVR" (olcoop-steamvr.bat, -vrmode openvr).
         Oculus/Meta headsets - start the Oculus app, then "olcoop Oculus" (olcoop-oculus.bat, -vrmode oculus).
  2. Main menu, bottom right: CO-OP: HOST / JOIN.
     Host:   HOST A CO-OP GAME, invite your friends from the list, then BACK and start or continue the campaign
             (PLAY MISSION / LOAD SAVED GAME) as usual.
     Friend: accept the host's Steam invite (game already running), or open CO-OP and pick the friend who is hosting.
             You are taken into the host's level automatically.
  No port forwarding is needed: the connection goes through Steam.
  Without Steam invites: the host opens CONNECT BY IP (under INVITE THROUGH STEAM) and forwards UDP port 7777 in the
  router to the PC it shows; friends choose JOIN BY IP and type the host's address. Share your IP only with people you trust.
  A red "WARNING: OLMOD IS NOT THE BUNDLED 0.5.14" on the main menu means the game was not started with the olcoop shortcut.

Between levels
  Everyone sees the results and upgrade screens. Joiners press READY UP; the host's next level starts when
  everybody is ready.

Logs (for bug reports): Overload\olcoop\olcoop_logs
Uninstall: run uninstall.bat (your own olmod and the game are not touched; logs and settings are kept).

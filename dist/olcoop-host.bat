@echo off
rem Co-op HOST: start this FIRST. Plays single-player as normal; a joiner can connect on UDP port 7777.
cd /d "%~dp0"
start "" olmod.exe -modded -coophost -coopport 7777 -runInBackground -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile olcoop_logs\unity-host.log %*

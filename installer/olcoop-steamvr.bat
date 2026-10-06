@echo off
rem Overload co-op (olcoop) in VR through SteamVR (Index, Vive, WMR, Quest via Steam Link / Virtual Desktop in SteamVR mode).
rem Start SteamVR first, then this. Same as olcoop.bat plus -vrmode openvr.
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
start "" olmod.exe -modded -vrmode openvr -runInBackground -logFile olcoop_logs\unity.log %*

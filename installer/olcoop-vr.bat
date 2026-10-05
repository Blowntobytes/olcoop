@echo off
rem Overload co-op (olcoop) in VR (SteamVR / OpenVR headsets). Start SteamVR first, then this.
rem Same as olcoop.bat plus -vrmode openvr. On the main menu choose "CO-OP: HOST / JOIN".
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
start "" olmod.exe -modded -vrmode openvr -runInBackground -logFile olcoop_logs\unity.log %*

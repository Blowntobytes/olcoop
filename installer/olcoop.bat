@echo off
rem Overload co-op (olcoop). Start the game with this, then on the main menu choose "CO-OP: HOST / JOIN".
rem Steam must be running (friends, invites and the connection all go through Steam - no port forwarding needed).
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
rem 0.8.0: runs olcoop's own olmod 0.5.14 from this folder (Overload\olcoop). olmod finds the game in the parent folder and
rem loads only the mods in this folder. The Unity log path is absolute: olmod switches to the game folder before starting it.
start "" "%~dp0olmod.exe" -modded -runInBackground -logFile "%~dp0olcoop_logs\unity.log" %*

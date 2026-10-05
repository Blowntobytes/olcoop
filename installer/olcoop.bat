@echo off
rem Overload co-op (olcoop). Start the game with this, then on the main menu choose "CO-OP: HOST / JOIN".
rem Steam must be running (friends, invites and the connection all go through Steam - no port forwarding needed).
cd /d "%~dp0"
if not exist olcoop_logs mkdir olcoop_logs
start "" olmod.exe -modded -runInBackground -logFile olcoop_logs\unity.log %*

# Challenge co-op test (0.7.0-challenge)
Both players on 0.7.0-challenge. Send both logs after the run ([CHAL] lines).
1. Host: CO-OP -> PLAY CHALLENGE -> pick a level, mode (countdown/infinite), difficulty. Joiner waits in the main menu.
   Joiner log: "opened the challenge briefing". Host log: "on the challenge briefing; told joiners level ...".
2. Joiner picks weapons/missiles, presses START -> back at main menu, WAITING FOR HOST. Host presses START -> level starts for both.
   Joiner log: "loading host's challenge level ... loadout=chosen". Joiner has the weapons it picked.
3. Robots keep spawning near both players; both see the same score, kills, combo and timer.
4. 25 kills (20 in countdown): both get a weapon/missile upgrade. Host log "kill upgrade"; joiner log "team kill upgrade".
5. Countdown: at 0:00 both see COUNTDOWN COMPLETE, then both get the results screen with the same score.
6. Infinite + Respawn: one dies -> respawns; both die -> "CO-OP: TEAM WIPED - RUN OVER" -> results for both.
7. Hardcore: first death -> run over for both.
8. From results: host RETRY -> briefing; joiner (main menu) is taken to the briefing again.

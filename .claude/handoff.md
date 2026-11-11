# (8a) Missing units & walls — handoff (module agent 8a-mod-1: no coordinator, no sub-agents)
Worktree `.claude/worktrees/wave8a`, branch `wave8a-units` (main 584dc9a merged 2026-11-11; merge again at the finish).
Brief: G8, G9 (+ Atlantis dive of G11, already done before 8a). Notes: `.claude/notes/naval.md`, `walls.md`.

## Done
- Data (88955ec): 9 blueprints, roles `nuke_sub_t3`, `sonar_t3`, `wall_t1` (optional), roster tests.
- Salem on land (676de72) + WalkingShipTests (10, incl. naval factory roll-off and wreck on land/at sea).
- MissileSubTests (8) and CarrierTests (6) green, no sim fix needed (da9c66f and after).
- Atlantis dive: done before 8a (AtlantisDiveTests): only stale docs lines to fix.

## TODO in order
1. Walls (sim): see notes/walls.md.
2. Client: models `mech_marine`, `stealth_field`, `missile_sub`, `cy_missile_sub`, `cy_carrier`, `cy_wall` (+ UEF wall /
   sonar if they fall back to default): new partial files + one `case` line each in UnitModels.cs; carrier deck spots
   (UnitRenderer.Cargo.cs ~144 keyed on shape "atlantis"); Salem legs/height on land (recipe at end of naval.md);
   `--scene units8a`.
3. Finish: merge main, build, 3 test projects, AI ref + bench 2000 300, screenshots, docs (ARCHITECTURE
   `## Missing units & walls (8a)`, CLAUDE.md --scene line, ROADMAP G8/G9 `[x] 2026-11-11 (8a)`, stale Atlantis-dive
   lines ~364 + G11, AI_TUNING hash note, "- [ ] later:"), `.claude/agent-report.md`.

## Reference numbers
- AI ref `Rts.Bench -- ai 30 256 42 hard,normal`: "WINNER: Player (Hard) after 15:35", hash BBD1FF3C4E116A4A
  (main BF85B7DBBF03D949: blueprint index shift only). Bench `2000 300`: ~1.75-1.95 ms/tick.

## Pitfalls
- zsh: `command grep`, `/usr/bin/find`; quote globs (`--include='*.cs'`). `sed -i` errors on this disk: edit with python.
- dotnet only via `bash tools/agents/dn.sh ...`; test output Italian (`Superato|Non superat`).
- NavalWorld.Basin has a shoal at x 40..44 (depth 0.4): subs/ships stop there.
- Older replays/saves refuse to open after the new blueprints: expected, say so in the report.
- Handoff review material: coord-2 note lacked "Atlantis dive already done" and "line placement already exists";
  the naval sub-agent left no note (WIP read from the diff); the MissileSubTests draft failed only on test geometry.

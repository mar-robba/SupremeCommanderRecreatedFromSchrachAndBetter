# (8a) piece 2 "sim naval" — note (agent 8a-naval-3)

## Done
- 676de72 WIP Salem on land (WalkingShips + hooks), by the previous agent.
- 60e5664 `tests/Rts.Sim.Tests/WalkingShipTests.cs` (7 tests, green): speed sea = MaxSpeed / land = MaxSpeed x 0.35,
  LayerOf Water->Land, guns kill tanks on land, tanks damage it, torpedo tube + zapper silent on land and working
  again in water, CargoSize 0 (transport Load ignored), UEF destroyer stays in water, no overlap with tanks on land.
  No sim fix was needed: the 676de72 design holds.

## Done by 8a-mod-1 (2026-11-11)
- MissileSubTests green (test bug only: the shoal x 40..44 of NavalWorld.Basin is too shallow for subs).
- CarrierTests (6, green, no sim fix). WalkingShipTests + factory roll-off of the Salem + wreck on land/at sea (green).

## Left, in order (old list; all done except the ARCHITECTURE section)
1. (task 1 leftover, optional) audit of the `MotionLayer.Water` sites for the Salem: AiNavy graph (Water graph: a Salem
   in a fleet stays at sea, fine), AiPerception:328 (Hover branch is after the IsNaval branch, fine). NOT checked:
   naval factory roll-off of a Hover-layer ship, wreck/sinking of a Salem that dies on land.
2. Missile subs submerged launch -> `tests/Rts.Sim.Tests/MissileSubTests.cs`.
3. Cybran T3 carrier -> `tests/Rts.Sim.Tests/CarrierTests.cs`.
4. Whole sim suite; ARCHITECTURE section `## Missing units & walls (8a)` / "Naval units".

## Decisions / pitfalls
- Test map `WalkingShipTests.Beach()`: sea x<=52, slope 0.5 to x=64, land at 8; shoreline x=60.
- Hull separation pushes a Salem and a ship apart when their capsules overlap: spawn hulls parallel in tests.
- The zapper kills torpedoes at its range (10), so "torpedo near the Salem" is only observable when it is held.

## For the client piece
- "Salem on land" from a snapshot: blueprint is Naval + Movement.Layer Hover (`WalkingShips.IsWalkingShip(bp)`) and
  the terrain under it is not underwater (`!terrain.IsUnderwater(pos)`); elevation = terrain height there
  (GetSurfaceHeight), on water = water level. Speed on land is 35 %: use it for the leg animation rate.

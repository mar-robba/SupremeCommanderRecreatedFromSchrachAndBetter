# (8a) client piece "models & scene" — note (agent 8a-client-3, CHECKPOINT at the context cap)

## Done
- a67b600: models (build green, NOT yet screenshotted, client tests NOT yet run):
  `src/Rts.Client/Rendering/UnitModels.Units8a.cs` (UEF mech_marine = MechLegs + torso turret; stealth_field = plinth +
  octagonal tower + spinning vane head; missile_sub = long hull, missile deck 2x4 hatches, sail, tail) and
  `UnitModels.CybranUnits8a.cs` (cy_missile_sub = CySub + missile spine; cy_carrier = CyShipHull + flight deck + island +
  4 SAM turret pivots; cy_wall = full-footprint base so sections tile; `CyCarrierSpot(k, size)` 2 cols x 8 rows).
- Shared files, one line each: `UnitModels.cs` dispatch (3 cases), `UnitModels.Cybran.cs` CybranShape (3 cases),
  `UnitRenderer.Cargo.cs` AddParkedAircraft (`BaseShape(shape) == "carrier"` -> CyCarrierSpot).

## Left (in order)
1. Run `bash tools/agents/dn.sh test tests/Rts.Client.Tests` (CybranModelTests checks the new cy_* shapes: no generic
   fallback, pivots for the carrier SAM, min y).
2. Walking Salem (task 3), plan: new `src/Rts.Client/Rendering/UnitRenderer.WalkingShip.cs`. Facts found: the sim
   already gives a Hover-layer unit Elevation = GetSurfaceHeight (terrain on land, water on sea), so on land the
   snapshot Y is the ground, but the hull (CyShipHull bottom -0.3 h, model.MinY) is sunk by its draft. Plan: render
   Y = max(snapY, groundH - model.MinY + 0.35 h) (pure static fn, client test), only when
   `Rts.Sim.Naval.WalkingShips.IsWalkingShip(bp)`; legs = small lazily built leg-segment InstancedMeshes (upper as
   Body, lower as Turret of a pseudo UnitModel, scaled only along Z so normals stay exact), hips at
   (±0.44 w, 0.21 h, z = 0.1 l / -0.24 l) like CyDestroyer's folded legs, diagonal gait, amplitude =
   speed/(MaxSpeed*LandSpeedFactor) from prev/cur snapshot positions (dt 0.1 s). Hook = one line in UnitRenderer.Draw
   before AddUnit (AddUnit's naval bob 0.05 is harmless on land).
3. `src/Rts.Client/Units8aScene.cs` (pattern CybranScene.cs: Name, ApplyDefaults, Setup, FindRect/Land/Deep helpers)
   + hooks GameApp.cs ~line 212 (+ `--scene` doc comment ~line 36) and GameSession.cs ~line 90.
4. Shift-drag wall lines check (Input/BuildPlacement.cs + PlacementLine.cs) - not read yet.
5. Screenshots (`--scene units8a ... --size 960x540`), client tests green.

# (8a) walls — note (8a-mod-1)
- Code: `src/Rts.Sim/Combat/Walls.cs` (IsWall, SegmentBox, SegmentHit, FirstOnSegment = DDA over occupancy cells).
- Hooks (minimal): CombatCatalog `UnitCombatInfo.IsWall`; ProjectileSystem HitVisitor uses the box for walls;
  TargetingSystem ScanVisitor + CanEngage skip walls (forced Attack path untouched); WeaponSystem.Emit beams:
  first enemy wall on the segment takes the hit (after the shield check).
- Movement: no change needed (1x1 structures stamp occupancy; A* has no corner cutting): diamond enclosure test holds.
- Only ENEMY walls absorb shots (HitVisitor visits enemy armies only); own/allied walls let shots through.
- Wall box height = Visual.Size.Y (1.0): shots from tall muzzles (Monkeylord 1.9) at far targets pass over walls
  that are not close to the target. Ballistic (arcing) shots test only their target: they fly over.
- Tests: `WallTests` (11, green). Line placement: client (7b's Input/PlacementLine.cs), untouched.

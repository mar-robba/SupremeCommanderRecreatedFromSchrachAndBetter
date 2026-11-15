# Combat

Targeting, weapons, projectiles, damage, veterancy, fire states, line of fire, EMP stun, Overcharge and walls.

**Contracts of this area**
- Units acquire and keep only targets their army detects (`CombatState.CanSee`), forced Attack targets included.
- Max health is always read through `Veterancy.MaxHealth(w, i)`.
- Area damage is never added inside a death hook (`DamageSystem` is iterating the list): queue it, as
  `ExperimentalSystem` and `MissileSystem.QueueDetonation` do.
- Area damage carries the weapon's target layers (`AreaDamageEvent.Layers`), so flak only hurts aircraft.
- Weapon kinds come from blueprint data through `CombatCatalog`; no unit ids in code.

Files and top-level types of this area: [index/combat.txt](index/combat.txt) (generated).

## Combat model (`Rts.Sim/Combat`)
- `CombatState.Of(world)` (attached via ConditionalWeakTable): per-tick cache of each unit's layer and a per-grid-cell
  army bitmask, so scans/collisions skip cells without enemies. `CombatCatalog`: per-blueprint weapon kinds
  (Direct, Homing = "missile", Ballistic, Bomb = ballistic + MuzzleVelocity 0, Beam = MuzzleVelocity 0).
- Targeting priority: units that can shoot back at the shooter's layer, then other armed units, unarmed mobile,
  armed structures, other structures; then distance, then index. Attack orders force targets; attack-ground
  uses `WeaponState.HasGroundTarget`. Aircraft on attack-move/patrol chase their engaged target (OrderSystem).
- Weapons lead moving targets (`MoveState.Velocity`); artillery (IndirectFire) uses the high ballistic arc;
  bombs are released when the free-fall impact point lands on the predicted target position.
- Projectiles: straight shots hit the first enemy collision sphere on their path, shells/bombs explode on the
  ground, flak (`ProjectileFlags.ProximityFuse`) bursts near aircraft. Area damage uses
  `AreaDamageEvent.Layers` (= weapon TargetMask) so flak only hurts aircraft and bombs don't hit planes.
- **Veterancy** (`Combat/Veterancy.cs`, kills counted in `SimWorld` via `Veterancy.AddKill`): a level every
  3 × tech kills (commanders/experimentals 20), max 5. Each level: +10% max health (healed by that much on level-up)
  and regen +1/+3/+6 HP/s (T1/T2/T3; commander 10, experimental 25). Always read max health through
  `Veterancy.MaxHealth(w, i)` (damage, repair, snapshot, upgrade/enhancement swaps keep the health fraction).
- Attack orders on a target the army no longer detects (lost in fog) are dropped (OrderSystem). Idle aircraft with
  no orders engage what their weapons acquire (leashed chase) and return to their orbit point.
- Line of fire (`Combat/LineOfFire.cs`, used by `TargetingSystem`): a NON-forced target is only acquired/kept if the
  weapon can hit it from where the unit stands: straight shots and beams need a clear muzzle→target line over the
  terrain/water surface (same grazing tolerance as projectiles), shells need an arc solution (`Ballistics.SolveArc`,
  as `WeaponSystem` uses). Homing/missiles, bombs, flak, torpedoes and silo weapons are not restricted; forced
  targets (Attack orders, TML/nukes, attack-ground) skip the check. Effect: units below a cliff no longer stop
  on attack-move to shoot into the rock; they walk up the ramp. `WeaponBlueprint.HighArcWhenBlocked` (Mavor): when
  `LineOfFire.IsArcClear` finds terrain on the low arc, `WeaponSystem` lobs the shell on the high arc instead.
- **Fire states** (`Combat/FireStates.cs`, `SetFireStateCommand`, `UnitStore.FireStates`, snapshot `UnitView.FireState`):
  Fire at will (default) / Return fire (acquires targets only while hit in the last 5 s) / Hold fire (never acquires;
  Attack orders and attack-ground still fire). `TargetingSystem` reads it; a unit being captured by an army (or an
  ally) is never auto-acquired by that army's units.

## EMP stun (from "Cybran faction & stealth/EMP", wave 5l)

**EMP stun** (`Combat/EmpStun.cs`). Weapon `StunDuration` (s), `StunRadius` (0 = DamageRadius, else only the unit hit),
`StunMaxTech` (default 3). Applied in the sequential impact phase (projectiles, beams) to enemy units on the weapon's
target layers; commanders, aircraft and units above `StunMaxTech` are immune; a shield dome protects the units inside
from an area stun fired from outside. `UnitFlags.Stunned` (bit 11): no move, acquire, fire, missile defense, build /
assist / repair / reclaim / upgrade / produce / stockpile; shields, intel, economy and stealth keep working.
`EmpStun.Expire` clears it at tick start; `EmpStunState` (until-tick + generation per slot) exists only after the first
stun. Users in data: Medusa (`cybran_t1_artillery`), SCU EMP array (`cybran_scu`).

## Overcharge (from "Commander kit & engineer automation", wave 6a)

The three ACU abilities are blueprint data; the shared introduction is in [commander.md](commander.md) "Commander kit".

- **Overcharge (G1)**: `SetOverchargeCommand(army, units, on)` sets `UnitFlags.Overcharge` on own units with an OC weapon
  (`OverchargeSystem.WeaponIndex(bp)`). `OverchargeSystem` runs before TargetingSystem: while armed and the reload
  (`Reload` 5 s) is ready it reserves stored energy (`MinEnergy` 1000 .. `MaxEnergy` 5000); `WeaponSystem.TryShot`
  turns the next shot into an OC shot: damage = energy x `DamagePerEnergy`, splash `DamageRadius` 2.5, structures x
  `StructureDamageMult` 0.5, commanders capped at `CommanderMaxDamage` 400 (AreaDamageEvent fields). Refund to storage
  on disarm / stun / no target for > 3 s; lost if the ACU dies. Snapshot: `ProjectileFlags.Overcharge`,
  `EffectKind.Overcharge` (muzzle) + `OverchargeHit` (impact), both in addition to the normal fx. The client's "ready"
  = army stored energy >= MinEnergy (reload is not in the snapshot).

## Walls (from "Missing units & walls", 8a)

- **Walls** (`Combat/Walls.cs`): 1×1 structures (`UnitCombatInfo.IsWall`) that stamp occupancy, so paths go round
  them (A* has no corner cutting: a diagonal line is closed too). They absorb direct shots as a box (`SegmentBox`,
  `SegmentHit`; ProjectileSystem's HitVisitor tests walls by box, not sphere, so diagonal lines have no gaps) and
  beams (`FirstOnSegment` = DDA over occupancy cells, checked in WeaponSystem after the shield check). Only ENEMY
  walls stop shots (own and allied walls let them through); ballistic (arcing) shots fly over; the box is
  `Visual.Size.Y` high, so a tall muzzle firing far past a wall can clear it. Walls are never auto-targeted
  (TargetingSystem ScanVisitor + CanEngage skip them) but a forced Attack order works. `WallTests`.

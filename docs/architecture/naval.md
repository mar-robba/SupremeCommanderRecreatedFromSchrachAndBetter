# Naval

Water structures, torpedoes, submarines and diving, ship hulls, seabed walkers and walking ships. Sea lanes on
Archipelago maps are in [map.md](map.md); oncoming ships in channels are in [pathing.md](pathing.md).

**Contracts of this area**
- A water structure needs `MinWaterDepth` under every footprint cell. `NavalRules.CellBuildable` is the one rule, for
  the sim (`OccupancyGrid.CanPlace`) and for the client's placement ghost.
- A unit's layer for targeting and intel is `OrderSystem.LayerOf`: a sub switches halfway through its dive, a walking
  ship is a Land target ashore.
- Submerged units fire only torpedo kinds and `FiresSubmerged` weapons, and only Sub-layer weapons can target them.

Files and top-level types of this area: [index/naval.txt](index/naval.txt) (generated).

## Naval (`Rts.Sim/Naval`)
- Water structures (Structure + `MinWaterDepth` > 0: naval factories T1–T3, sonar, torpedo launcher):
  `NavalRules.CellBuildable` (every footprint cell needs that much water) is used by `OccupancyGrid.CanPlace` and
  by the client ghost (`PlacementGrid`, `PlacementError.NeedsDeepWater`); `NavalRules.FindSite` = nearest valid
  site (AI/tests/scenes). They sit at the water surface. Builders on land walk as close as they can (an unreachable
  goal shrinks the arrive radius by the gap, `MovementSystem`), then build within range.
- Torpedoes (`Naval/Torpedoes.cs`): weapons with `ProjectileVisual` "torpedo" (`WeaponKind.Torpedo`,
  `ProjectileFlags.Torpedo`) drop into the water, run at `RunDepth`, home on the target, hit by a 2D swept circle
  on the Water/Sub layers and die when the sea floor gets close (shore/shallows). Torpedo defense ("antitorpedo",
  `WeaponKind.AntiTorpedo`, never targets units): `Torpedoes.Intercept` (sequential, before projectiles move)
  destroys one torpedo in range per weapon cooldown.
- Subs (`MotionLayer.Sub`, layer `LayerMask.Sub`) are visible only with sonar/omni coverage (`IntelService`), and
  only weapons with the Sub layer (torpedoes) can target them: land/air guns never acquire them.
- Dive/surface (`SetSubmergedCommand` → `Naval/Submarines.cs`, `UnitFlags.Surfaced`): depth changes smoothly;
  the layer (`OrderSystem.LayerOf`) switches halfway. A surfaced sub is a Water-layer unit: seen by vision/radar,
  hit by surface guns, collides with ships. Every `Movement.Layer: "Sub"` unit can dive; `StartSurfaced` divers
  (the Atlantis) spawn at the surface. Submerged, only torpedo weapons work (`Submarines.FiresSubmerged`: Targeting
  and WeaponSystem hold the SAMs/guns). A blueprint swap (InitUnit re-run) keeps the dive state and depth.
- Ship separation (`Naval/NavalMotion.cs`): hulls are capsules along the heading, pushed apart for all armies;
  submerged subs only collide with subs, surfaced subs with ships.
- Client: models in `Rendering/UnitModels.Naval.cs`; `EffectsRenderer.Naval.cs` (torpedo body + surface streak +
  bubble trail, splash/hit/interception bursts, ship wakes scaled by speed and hull), `UnitRenderer.Submerged.cs`
  (submerged units drawn after the water, see-through and blue-tinted), `PropRenderer.Naval.cs` (wrecks on deep
  water sink and fade), sonar ring in `BuildPlacement`. `--scene naval` (`NavalScene.cs`): scripted Archipelago
  fleet battle + naval factory (`--select uef_t1_naval_factory`) for screenshots and tests.

## Seabed walkers (from "Cybran faction & stealth/EMP", wave 5l)

**Seabed walkers** (`Naval/Submarines.cs`). `Movement.Layer: "Amphibious"` units (Wagner, Monkeylord) walk under
water; below the water level they hold every weapon except torpedo kinds and `FiresSubmerged` weapons, and are
targeted like Layer Sub (torpedoes only; sonar/omni detection).

## Salem walks on land (from "Missing units & walls", 8a)

- **Salem walks on land** (`Naval/WalkingShips`): a Naval-category unit with `Movement.Layer: Hover` is a walking ship
  (`IsWalkingShip`). It is a hull for separation on both terrains (`NavalMotion.KindOf` → KindShip, `IsNaval` true),
  a Water-layer target at sea and a Land-layer target ashore (`OrderSystem.LayerOf`), drives at `MaxSpeed ×
  Movement.LandSpeedFactor` (0.35) on dry ground (`MaxSpeedAt` in MovementSystem, also for the stuck check), holds
  torpedoes and its anti-torpedo zapper on land (`TorpedoesHeld`: Torpedoes + WeaponSystem), and is not
  transportable (`CargoSize` 0 for every Naval unit). Naval factories release it like any ship; it leaves an
  ordinary wreck on land. `WalkingShipTests`.

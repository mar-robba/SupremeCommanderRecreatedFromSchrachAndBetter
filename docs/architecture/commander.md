# Experimentals, commanders, enhancements

Experimental units, ACU / SCU enhancements, the quantum gateway, the commander death blast and the personal
teleporter. Overcharge is in [combat.md](combat.md).

**Contracts of this area**
- Abilities are blueprint data (`Experimental`, `Enhancements`, `Overcharge`, `DeathBlast`, `Teleport` blocks): no
  unit ids in code.
- An enhancement is a generated blueprint VARIANT. Installing one swaps `Units.Bp` in place (same handle, health
  fraction kept), so every system just reads the new blueprint; every module's `InitUnit` must be idempotent.
- Death blasts are queued and fired later, never inside `OnUnitDeath`.
- Assassination looks only at `Army.Commander` (an SCU is not a commander).

Files and top-level types of this area: [index/commander.txt](index/commander.txt) (generated).

## Experimentals & commanders (`Rts.Sim/Experimental`, `Rts.Sim/Commander`)
- **Experimentals** = blueprints with category `Experimental` and `Tech` 4 (build menu tier "EXP"). Optional
  `Experimental` block: `CrushRadius`/`CrushDamage`/`CrushMaxTech`/`CrushAllies`, `DeathDamage`/`DeathRadius`/`DeathDelay`.
  `ExperimentalSystem` (sequential, after MovementSystem; per-world `ExperimentalState`, plain lists/arrays) registers
  units in `UnitLifecycle.OnSpawn` (idempotent) and:
  - crush: a moving crusher damages complete enemy land units (Tech ≤ CrushMaxTech, not commander/experimental) within the
    radius (`DamageEvent`s, grid query sorted by index);
  - death blast: `OnUnitDeath` queues a `PendingBlast`; after DeathDelay it becomes an `AreaDamageEvent` + big `Explosion`
    effect (can't add area damage inside OnDeath: DamageSystem is iterating it). The wreck is the normal one
    (BuildCostMass × WreckMassFraction);
  - mobile factories (Fatboy: a mobile unit with the `Factory` category; FactorySystem handles it): the unit under
    construction rides inside; `FactorySystem.Release` lets it out at the back and doesn't copy the factory's own orders;
  - `EnergyPerShot` weapons (Mavor): charge pay-first through the unit's EnergyRequest (structures own it, builders add to it);
    `WeaponSystem` fires only with a full charge (`WeaponEnergy.TryConsume`, parallel-safe: own slot).
  Engineers ordered to build the same mobile unit at the same spot assist one site (`ExperimentalSystem.FindMobileSite`).
  Keep mobile `Radius` ≤ 3 (it sizes every unit's separation query; use `CrushRadius` for contact).
- **Enhancements** (`Enhancements` in the blueprint: Id, Slot LeftArm/RightArm/Back, Prerequisite in the same slot,
  cost/time, Visual/Icon, `Effects`: weapon modifiers, added weapons, health/regen/build rate/production/upkeep/vision,
  `BuildListFrom`, Shield, Silo). `EnhancementCatalog` (at `BlueprintDb.FinalizeDb`) generates one blueprint VARIANT per slot
  combination (id `base+Enh1+Enh2`, JSON clone + effects, prerequisite effects stack; `IsVariant`, `Installed`,
  `EnhancementVariants`). Installing/removing swaps `Units.Bp` in place (same handle, health fraction kept, modules re-init;
  `StrategicSystem.InitUnit` is idempotent and keeps the silo stock and the shield HP/up-down state, clamped), so every
  system just reads the new blueprint.
  `BuildListFrom` merges other builders' final build lists (SCU/T3 Engineering from `uef_t3_engineer`, Fatboy from the T3
  land factory). Building one reuses `UpgradeState`/`UpgradeSystem`: `EnhanceCommand` stops the unit and targets the variant,
  `UpgradeSystem.Cost` returns the enhancement's cost/time, the unit works on itself with its BuildRate, engineers assist
  with Guard, `CancelUpgradeCommand` refunds, any new order or Stop cancels it (with refund); `RemoveEnhancementCommand` clears a slot at once
  (no refund). The snapshot's `UpgradeBp`/`UpgradeFrac` carry the progress (`EnhancementCatalog.Added` = which one).
  Client: `UI/EnhancementPanel.cs`, model attachments in `Rendering/UnitModels.Experimental.cs` (from `Installed` + `Visual`).
- **Quantum Gateway / SCU**: a T3 factory building `uef_scu` (engineer + cannon with its own enhancements). The SCU has no
  `Commander` category: assassination only looks at `Army.Commander`.
- Client: `UI/ExperimentalAlert.cs` warns once per enemy experimental seen in the fog-filtered snapshot (banner + sound);
  `--scene experimental` shows them all. Sandbox lists hide variants (exact variant ids still spawn).

## Commander kit & engineer automation (wave 6a: `Rts.Sim/Combat/OverchargeSystem`, `Rts.Sim/Commander`, `Economy/EngineerPatrol`)
All three ACU abilities are blueprint data (no unit ids in code): a weapon's `"Overcharge": {}` block, a unit's
`"DeathBlast": {}` and `"Teleport"` (through the `PersonalTeleporter` enhancement). Both faction commanders have them;
enhancement variants clone them (EnhancementCatalog). New sim state lives in per-world weak tables (saveable, hashed).

- **Commander death blast (G2)**: `CommanderDeathBlast.OnUnitDeath` (UnitLifecycle.OnDeath hook; skips unfinished units
  and `RemoveUnitSilently`) queues a blast `Delay` 1.5 s later; `Run` (after ExperimentalSystem) fires it through
  `Strategic.MissileSystem.QueueDetonation` = a two-ring nuke (inner 12 / 4000 + outer 26 / 1500, rings add up), own
  units hurt too, wrecks in the inner ring vaporized. NOT in CleanupSystem (never add AreaDamage inside OnDeath). The
  client sees `EffectKind.Nuke` with `BlueprintIndex` = the commander's bp: `EffectsRenderer.IsCommanderBlast(db, bp)`
  (bp.DeathBlast != null) picks the smaller ACU mushroom and a positional sound instead of the map-wide nuke sound.

- **Personal Teleporter (G7)**: enhancement `PersonalTeleporter` (Back slot, 1500 M / 37500 E) adds
  `TeleportBlueprint` (ChargeTime 10 s, EnergyBase 5000 + EnergyPerDistance 50 per unit, MaxRange 0 = unlimited).
  Order `Order.TeleportTo(p)` (`OrderType.Teleport`, shift queues; CommandSystem drops it for units without a
  teleporter). `TeleportSystem` (sequential, after ConstructionSystem): when the order reaches the front the cost is
  fixed and charging starts (energy request x economy efficiency, so a stall slows it; paused/stunned/carried waits);
  any other front order cancels it (energy lost). The jump lands on the nearest standable cell for the unit's clearance
  (map clamp + `NavGrid.FindNearestPassable`; amphibious ACUs may land on the seabed). Snapshot: `UnitView.TeleportFrac`
  (0..1, -1 = not charging, also for enemies), effects `TeleportCharge` (charge start), `TeleportOut` + `TeleportIn`
  (jump tick; `TeleportIn` passes the fog filter for the jumper's team, its vision there is not stamped yet). Client: key J / orders button `TeleportInput` (cost estimate `TeleportInput.Cost`, mirrors
  `TeleportSystem.Target`), `TeleportHud` arc + rings, `EffectsRenderer.Teleport`, back-pack mesh `EnhTeleporter`;
  unit interpolation snaps instead of sliding when a unit moves > 20 between snapshots. The AI does not use it.

- Sandbox console: `overcharge [army|all] [on|off]` (alias oc), `kill [radius] at x,z` (area kill; plain `kill` =
  selection), `teleport [army|all] [at] x,z` (alias tp). Teleporter variants spawn by id:
  `spawn uef_commander+PersonalTeleporter me at 100,128`.

Overcharge is in [combat.md](combat.md), the engineer patrol automation in [economy.md](economy.md).

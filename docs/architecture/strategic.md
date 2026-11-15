# Shields and strategic weapons

Shield domes, missile silos (tactical, nuclear, anti-nuke), missiles and missile defense.

**Contracts of this area**
- `StrategicSystem` is one sequential pipeline step, after movement and before targeting; it costs nothing while no
  strategic unit exists.
- `ShieldIndex` is built in that step and only read in the parallel projectile phase.
- Silo and defense weapons are `WeaponKind.Manual`: they never fire by themselves.
- Detonations are queued (`MissileSystem.QueueDetonation`) and applied at the start of `DamageSystem`.
- `StrategicApi` is the public entry point, for sequential sim code only.
- `StrategicSystem.InitUnit` is idempotent: a blueprint swap keeps the silo stock and the shield state.

Files and top-level types of this area: [index/strategic.txt](index/strategic.txt) (generated).

## Shields & strategic weapons (`Rts.Sim/Strategic`)
- `StrategicSystem.Update` is one sequential pipeline step (after movement, before targeting): interceptor prep →
  silo stockpiles + Launch orders (`SiloSystem`) → shields + `ShieldIndex` (`ShieldSystem`) → missile defenses
  (`MissileSystem`). Per-unit state lives in `StrategicState.Of(world)`; blueprint data in `UnitBlueprint.Strategic.cs`
  (`Shield`, `Silo`, `MissileDefense`). Zero cost when no strategic unit exists.
- **Shields** (ellipsoid domes; T2/T3 generators, `uef_t2_mobile_shield` follows its unit): HP, RegenRate after
  RegenDelay, RechargeTime after collapsing. Energy upkeep via the flow economy; `Econ.Efficiency` < 0.95 (stall)
  collapses them. `ShieldIndex` (32-unit grid, army mask per cell) is read in the parallel projectile phase: a
  projectile segment entering an enemy dome from OUTSIDE is absorbed at the surface and damages the shield; shots
  fired from inside pass out; area blasts outside a dome don't hurt units inside; beams are blocked on the
  muzzle→target line. Nukes ignore domes and knock every shield in their outer ring down. Bombs and shells falling
  onto a dome from above are absorbed too, and a bomb released by an enemy bomber INSIDE a dome hits the shield.
  Collapsed or paused shields pay no upkeep (EconomySystem); recharge only starts while the army's energy efficiency
  is >= 0.95.
- **Silos** (TML, nuke, anti-nuke): stockpile built pay-first like construction (MissileCost × dt/BuildTime per tick,
  scaled by efficiency, up to MaxStorage). `OrderType.Launch` (`Order.LaunchAt(pos)`) at the queue front fires one
  missile when stocked (TML: only within [MinRange, Range], else dropped); with an empty stockpile it waits.
  Silo/defense weapons are `WeaponKind.Manual`: never auto-fire. `SiloSystem.SetStock` for scenes/tests;
  `SiloSystem.InstantStockpile` = sandbox instant build (a free missile per tick up to MaxStorage).
- **Missiles** are ordinary projectiles registered in `StrategicState`: TML = scripted cruise path above the highest
  terrain, shield-stoppable; nuke = high arc anywhere, two-ring blast (`OuterDamage/OuterRadius`) queued by
  `MissileSystem.QueueDetonation` (+ the one `EffectKind.Nuke` effect) and applied at the start of DamageSystem; the
  next tick `StrategicSystem.Run` vaporizes props/wrecks in the inner ring (clean crater). TMD / anti-nuke fire homing interceptors at the nearest unchased enemy missile in Radius (rate
  limited; anti-nuke spends a stockpiled missile per shot).
- **Snapshot** (`Snapshot/StrategicViews.cs`): `Shields` (HP fraction, up, recharge fraction, last hit tick), `Silos` (kind, stock, progress),
  `Launches` + `LaunchSerial` (nuke launches, GLOBAL: not fog-filtered, drive the "STRATEGIC LAUNCH DETECTED"
  banner/klaxon); `EffectKind.Nuke` effects are also global (IntelSnapshotWriter never fog-filters them). Enemy missiles otherwise follow projectile fog rules.
- **Public entry point**: `StrategicApi.LaunchNuke(SimWorld w, int army, Vector2 from, Vector2 target,
  string siloBp = DefaultNukeSilo)` and `LaunchNuke(w, army, target)` (from the commander, else map center) launch
  a nuke without a silo (warning, interceptable, same blast); `StrategicApi.Detonate(w, army, center, rings…)` = the
  blast alone (no missile); `StrategicApi.NukeRings(db)` = the silo missile's rings (client previews). Sequential sim
  code only. The sandbox `nuke` command uses `LaunchNuke`, `nuke instant` uses `Detonate`.
- **Client**: `Rendering/ShieldRenderer.cs` (fresnel + hex dome shader, hit ripples, fade on collapse, fog-aware,
  shield HP bars), `Rendering/EffectsRenderer.Strategic.cs` (missile/nuke trails, interceptors, shield and
  launch/intercept bursts; the detonation itself is `NukeFx`, see Sandbox below), `Input/StrategicOrders.cs` (Launch mode: L hotkey /
  Launch button, picks one in-range launcher with most stock), `UI/StrategicHud.cs` (banner, target markers, range
  and blast rings, silo/shield bars), audio `GameAudio.Strategic.cs`. Visual test bed: `--scene strategic[:shields|:tml|:nuke]`.

## Shield chain (from "Cybran faction & stealth/EMP", wave 5l)

**Shield chain.** ED1 → ED5 via `UpgradesTo` on the shield structures; no sim code: `UpgradeSystem.Complete` →
`UnitLifecycle.OnSpawn` → `StrategicSystem.InitUnit` brings the dome up at the new blueprint's MaxHealth.

## Strategic missile subs (from "Missing units & walls", 8a)

- **Strategic missile subs** = a mobile, diving strategic silo (`Silo.Kind: Strategic`, `Movement.Layer: Sub`): the
  Strategic module builds and launches the nuke while submerged and on the move (queued after a move works too);
  the cruise missiles fire submerged through weapon json `FiresSubmerged` (Naval/Submarines). `MissileSubTests`.

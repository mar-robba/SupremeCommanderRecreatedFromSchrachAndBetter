# Blueprints and factions

Units are data (`data/blueprints/units/*.json`, loaded into `BlueprintDb`); a faction is a data file that maps roles
to blueprints. This file holds the faction contract and the unit rosters.

**Contracts of this area**
- Sim and AI code never names a unit id (`"uef_..."`): it asks a role of the army's faction (CLAUDE.md hard rule 10).
- Every shipped faction fills every required role; code that uses an optional role handles -1.
- `BlueprintDb` and `FactionDb` are immutable after loading and may be read from any thread.
- Build lists are blueprint data: `Builds`, `BuiltBy`, `UpgradesTo` ([core.md](core.md) "Data model").
- Owner decision 2026-10-23: two factions only (UEF and Cybran).

Files and top-level types of this area: [index/factions.txt](index/factions.txt) (generated).

## Factions (`Rts.Sim/Data/FactionDb.cs`, `data/factions/*.json`) — contract for faction/AI agents
- A faction = `data/factions/<id>.json`: `Id` (lowercase, = file name = the blueprint id prefix of its units, e.g.
  `cybran` → `cybran_t1_tank`), `Name`, `Commander` (blueprint id of the starting commander), `Color` ("#RRGGBB" UI
  accent), `Description`, `Roles` {role → blueprint id}. Loaded with the blueprints into `BlueprintDb.Factions`
  (`FactionDb`: file order = ordinal id order, ids case-insensitive, immutable, readable from any thread). Loading
  throws `InvalidDataException` for an unknown role name, a role/commander pointing to a missing blueprint, a bad
  color, duplicate ids or the reserved id `random`. `uef.json` is the reference file.
- `UnitBlueprint.Faction` = JSON `"Faction"`, else the id prefix before the first `_` (`BlueprintDb.FactionOfId`).
- `SkirmishSettings.Factions[army]` (null/"" = `uef`, `random` = `FactionDb.Resolve(choice, seed, army)`, the same on
  every machine; unknown id → `InvalidOperationException`) → `Army.Faction` (saved with the game, so loads and replays
  keep it) → the army starts with its faction's `Commander`. Exposed as `ArmyView.Faction` (snapshot) and
  `PlayerInfo.Faction` (save/replay header).
- **Rule: sim/AI code never hard-codes unit ids** (`"uef_..."`): it asks a role of the army's faction:
  `w.Blueprints.Factions.RoleBlueprint(w.Armies[a].Faction, FactionRoles.TankT1)` → blueprint index, -1 = none
  (`FactionBlueprint.Role(role)` on a resolved faction). `grep -rn '"uef_' src/Rts.Sim` must only find doc comments.
  Tests, client visual scenes (`--scene ...`) and data files may use ids. Example: `AiBrain` resolves its `Bp*` fields
  from roles once per brain; `StrategicApi.NukeSiloOf(w, army)` = the faction's `nuke_silo` (else the first faction
  that has one).
- Roles are `FactionRoles` constants. **Required** roles must be filled by every shipped faction (AI/sim code may rely
  on them); **Optional** roles (`FactionRoles.Optional`) are faction-specific units, code using them must handle -1.
  Tests (`FactionTests`): `EveryFactionFillsEveryRequiredRole`, `EveryFactionBlueprintHasARole` (every non-variant
  blueprint of a faction is its commander or some role, so the AI can find it), `RoleListsAreConsistent`.
  - Required: engineer_t1, engineer_t2, engineer_t3, scu, scout_t1, tank_t1, artillery_t1, mobile_aa_t1,
    amphibious_t2, heavy_tank_t2, mml_t2, mobile_aa_t2, assault_t3, artillery_t3, air_scout_t1, interceptor_t1,
    bomber_t1, transport_t1, gunship_t2, torpedo_bomber_t2, transport_t2, air_staging, air_superiority_t3,
    spy_plane_t3, strat_bomber_t3, frigate_t1, sub_t1, destroyer_t2, cruiser_t2, battleship_t3, land_factory_t1,
    land_factory_t2, land_factory_t3, air_factory_t1, air_factory_t2, air_factory_t3, naval_factory_t1,
    naval_factory_t2, naval_factory_t3, quantum_gateway, mex_t1, mex_t2, mex_t3, pgen_t1, pgen_t2, pgen_t3, hydro,
    mass_fab_t2, mass_fab_t3, mass_storage, energy_storage, pd_t1, pd_t2, aa_t1, aa_t2, aa_t3, torpedo_t1, shield_t2,
    tml, tmd, artillery_installation_t2, artillery_installation_t3, nuke_silo, anti_nuke, radar_t1, radar_t2, omni_t3,
    sonar_t1, sonar_t2, exp_land.
  - Optional: mobile_shield_t2, mobile_stealth_t2, siege_t3, sniper_t3, fighter_bomber_t2, gunship_t3, carrier_t3,
    pd_t3, torpedo_t2, shield_t3, stealth_field_t2, exp_land_2, exp_air, exp_naval, exp_artillery, light_assault_t1,
    shield_ed2_t2, shield_ed3_t2, shield_ed4_t3, shield_ed5_t3, nuke_sub_t3, sonar_t3, wall_t1.
- **New faction** (e.g. Cybran): add `data/factions/cybran.json` + `cybran_*` blueprints in `data/blueprints/units`
  (build lists via `Builds`/`BuiltBy` inside the faction); fill every required role and the optional ones it has; a
  unit that fits no role gets a new (optional) role. Nothing else: the client lists it automatically (setup FACTION
  column, sandbox FACTION filter, `--faction cybran`), the AI plays it through roles.
- **AI agents** ask roles, never ids; prefer required roles, fall back when an optional role is -1 (e.g. no mobile
  shield → skip it, no `exp_air` → build `exp_land`).
- **New role**: a constant in `FactionRoles` + an entry in `FactionRoles.All` (+ `Optional` if not every faction has
  one) + the role in every `data/factions/*.json` + the lists above. Role names are lowercase `<kind>_t<tier>`.
- Client: setup screen FACTION column per slot (`GameConfig.FactionChoices` = every faction, then Random; unknown ids
  block the start via `GameConfig.Problem(factions)`), `--faction ID` (local player) / `--factions a,b,...` (slots in
  order), sandbox spawn panel FACTION filter (`SandboxPanel.MatchesFaction`), slot factions kept in `ClientGameData`
  and rebuilt from save headers.

## Cybran faction & stealth/EMP (wave 5l)

**Data.** `data/factions/cybran.json` + 80 `cybran_*` blueprints (SupCom1 roster: ACU/SCU with enhancements,
engineers T1–T3, land/air/naval T1–T3, Monkeylord, Soul Ripper, Scathis, full base set). Every required role is
filled; Cybran-only optional roles: `light_assault_t1` (Hunter), `mobile_stealth_t2` (Deceiver), `shield_ed2_t2` …
`shield_ed5_t3`. UEF-only roles stay empty, and `shield_t3` is empty (ED4/ED5 are upgrade-only, as in SupCom1).
Balance mirrors the UEF unit of the same role (builders same speed; combat units a little faster). Every
`Visual.Shape` starts with `cy_`; air factories reuse the land factory shapes. `CybranRosterTests` checks roles,
in-faction build lists, reachability from the commander, `UpgradesTo` chains and the Cybran specifics.

The other parts of that section are in their areas: stealth, cloak and stealth fields in [intel.md](intel.md), EMP stun
in [combat.md](combat.md), seabed walkers in [naval.md](naval.md), the shield chain in [strategic.md](strategic.md),
models and status visuals in [rendering.md](rendering.md), the AI note in [ai.md](ai.md).

## Missing units & walls (8a: `Rts.Sim/Combat/Walls.cs`, `Naval/WalkingShips.cs`, `data/blueprints/units`)

The SupCom 1 vanilla units UEF and Cybran still lacked (gap rows G8, G9). Almost all data: the existing modules
(silos, subs, Atlantis cargo, intel fields, upgrades) carry them. Faction roles are optional (`FactionRoles`
`NukeSubT3`, `SonarT3`, `WallT1`; json also `light_assault_t1`, `stealth_field_t2`, `carrier_t3`): the AI does not
build them. Tests: `MissingUnitsRosterTests` (roles, reachable from the commander, build lists, sonar upgrade chain).
- **Units**: UEF Mech Marine `uef_t1_mech_marine` (T1 land factory bot); UEF T2 stealth field generator; T3 sonar
  platforms (`uef_t3_sonar`, `cybran_t3_sonar`, `UpgradesTo` from the T2 sonar; Cybran's is radar/sonar stealthed);
  T3 strategic missile subs `uef_t3_strategic_missile_sub` / `cybran_t3_strategic_missile_sub` (T3 naval factory);
  Cybran T3 carrier `cybran_t3_aircraft_carrier`; T1 walls `uef_t1_wall` / `cybran_t1_wall` (commander + engineers).

The other parts of that section are in their areas: strategic missile subs in [strategic.md](strategic.md), the Cybran
carrier in [transport.md](transport.md), the Salem on land in [naval.md](naval.md), walls in [combat.md](combat.md),
models in [rendering.md](rendering.md).

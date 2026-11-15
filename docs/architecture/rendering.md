# Rendering

Everything drawn in the 3D view: terrain, water and sky, units and their models, effects and particles, fog of war,
wrecks and nature props, shadows, LOD, death sequences, biomes, range rings.

**Contracts of this area**
- Renderers draw from snapshots, blueprints and the terrain only; none of this changes the sim.
- raylib matrices are built with System.Numerics and transposed before `DrawMesh` (CLAUDE.md hard rule 5).
- Units are GPU-instanced, one draw per blueprint mesh. Vertex colour alpha is a material tag (255 metal, 0 team
  colour, 128 glow).
- A new unit model goes in a new partial file, with one `case` line in `UnitModels` (a shared file).
- Effects and wreck animations run in game time (`EffectsRenderer.GameDt`): frozen while paused.

Files and top-level types of this area: [index/rendering.txt](index/rendering.txt) (generated).

## Renderers (from "Client")

- `Rendering/`: `TerrainRenderer` (chunk meshes + per-pixel terrain shader, sky, animated water, picking),
  `UnitRenderer` (GPU-instanced procedural models from `UnitModels`/`MeshBuilder`, one draw per blueprint mesh,
  team color per instance, turrets, construction holograms, sun `ShadowMap`), `EffectsRenderer` (projectiles +
  pooled `ParticleSystem` for `EffectEvent`s, `QuadBatch` quads), `IconRenderer` + `IconAtlas` (procedural
  strategic icons, crossfade with meshes), `Shaders` (GLSL 330 sources, `Atmosphere` lighting/haze),
  `FrameView` (per-frame matrices, frustum culling), `PerfOverlay` (frame time, F8).
  Unit model vertex colors: rgb = albedo, alpha = material tag (255 metal, 0 team color, 128 glow).
  Debug: `--gallery` (all shapes), `--fxtest`/F9 (fake battle effects), F10 (explosion), `--stress N`.
  `PropRenderer` (wrecks from `SimSnapshot.Props`: the unit's model with a burnt-metal shader, sunk/tilted, embers +
  smoke when fresh, sinking while reclaimed; gray dots in the strategic view). Effects/wreck animations run in game
  time (`EffectsRenderer.GameDt`: frozen while paused, faster at 2x).

## Fog of war on the client (from "Client")

- `Rendering/FogRenderer`: fog of war on the client. Uploads `SimSnapshot.Fog` into two R8 textures (previous/current
  tick, bilinear) sampled by terrain/water/unit shaders via `Shaders.FogOfWarFs` (unexplored dark, explored
  dimmed/desaturated, 5-tap soft edges, blended by the interpolation alpha); radar blips as army-colored ring sprites;
  minimap fog/ghosts/blips; ghost structures are drawn greyed by `UnitRenderer`/`IconRenderer`. F7 / `--nofog`: reveal
  all. Picking/selection need no special code: unseen enemies are simply not in the snapshot.

## Cybran models and status visuals (from "Cybran faction & stealth/EMP", wave 5l)

**Client.** Models in `Rendering/UnitModels.Cybran*.cs` (`Cybran.cs` = shape dispatch `IsCybranShape`/`CybranShape`,
`BaseShape` maps `cy_air_staging`/naval shapes to the UEF names that cargo, icons and naval code check; `CybranAir`,
`CybranNaval`, `CybranStructures`, `CybranDefense`, `CybranExperimental`); one `case` line in `UnitModels.Build`.
Status visuals (`Rendering/StatusFx.cs` = pure logic, `UnitRenderer.Status.cs` = `DrawStatus`, called after
`DrawSubmerged`) from `UnitView.Status`: cloaked units skip the opaque pass and are drawn with a translucent fresnel
shimmer (no depth writes, no shadow; a cloaked unit under water stays a normal submerged unit); stunned units get
blue-white spark bolts; a stealth field shows a violet terrain-following ring at `bp.Intel.StealthFieldRadius`
(enhancements are blueprint variants, so `UnitView.Bp` carries their radius). Radar/sonar stealth alone has no visual.
`--scene cybran[:land|:air|:naval|:structures1..3|:experimental]` (`CybranScene.cs`) lays out every Cybran blueprint.
Tests: `CybranModelTests`, `CybranSceneTests`, `StatusFxTests`.

## Models of the 8a units, walking Salem (from "Missing units & walls", 8a)

- **Client**: models in `UnitModels.Units8a.cs` (UEF) and `UnitModels.CybranUnits8a.cs` (Cybran, incl. carrier deck
  spots `CyCarrierSpot`, used by `UnitRenderer.Cargo` for shape `cy_carrier`: `BaseShape` keeps that name). Walking
  Salem (`UnitRenderer.WalkingShip.cs`, from snapshot + blueprint + terrain only): on dry ground the hull is lifted by
  its draft (`WalkingShipY` = max(snapshot Y, ground − model MinY + 0.35 h)) and 4 spider legs step with a diagonal
  gait, amplitude = speed / (MaxSpeed × LandSpeedFactor); `WalkingShipRenderTests`. Wall lines use the existing
  Shift-drag line placement (`Input/PlacementLine.Layout`: one 1×1 section per cell, diagonal drags give
  corner-touching sections, which still block paths and shots). Visual test scene `--scene units8a[:land|:walls|:naval|:salem]`
  (`Units8aScene.cs`).

## Death animations & ACU warp-in (9b-1: `Rts.Client/Rendering/DeathAnim.cs`, `DeathAnimator*.cs`, client only)
- No sim change: the client reads `EffectKind.UnitDeath` (Pos = death point = wreck point, Army, BlueprintIndex) and
  matches each one to the unit of the previous snapshot missing from the current one (same blueprint and army,
  nearest, < 6 u; `DeathAnimator.Sync`, idempotent per snapshot, called by every renderer that needs it because the
  frame order is audio → units → props → effects). Reclaimed / cancelled units (`RemoveUnitSilently`) raise no event,
  so they never animate. A replay seek (tick backwards or > 50 ticks ahead) clears everything.
- `DeathAnim` (pure, `DeathAnimTests`): `Choose` picks the kind per unit: AirCrash (aircraft ≥ 1.5 u over the
  surface: one descending loop of shrinking radius, lerped from the last drawn position to the death point so it ends
  ON its wreck, 1.5-3.2 s, ×1.6 for experimentals), ShipSink (surface ships/surfaced subs over water: lists, then sinks
  bow up, char + dither fade; deep water (≥ 1 u) as long as the wreck's own sinking in `PropRenderer.Naval`),
  Collapse (structures: shake, squash, sink into the wreck), BigFall (land experimentals: stagger until the death
  blast after `Experimental.DeathDelay`, then topple), CommanderFall (kneel with an overloading core until the
  `DeathBlast.Delay` nuke), None (land units, submerged subs: the regular explosion and the wreck at once).
  `WreckVisibility` hides / fades in the wreck (`PropRenderer.DeathFx.cs`) while the sequence plays.
- Limits that keep big battles cheap: at most `MaxActive` = 64 sequences in a fixed array, none for deaths off-screen
  or under fog (those get the wreck at once). Dying units go into the same instanced draws as the living
  (`UnitRenderer.Death.cs`): the unit shader's instance w packs fade (integer part, 16 dither levels) and char
  (fraction) with `DeathAnim.PackShaderW`; living units send 0, so they render exactly as before.
- Particles and sounds per kind in `DeathAnimator.Fx.cs`; sounds go through the fixed queue `Audio/UnitFxSounds`
  (drained by `GameAudio.Death.cs`, recipes `SoundBank.Death.cs`: AircraftFall, AircraftCrash, ShipSink,
  StructureCrumble, WarpIn). An animated aircraft gets a smaller mid-air burst (`EffectsRenderer.Death.cs`).
- ACU warp-in (`DeathAnimator.WarpIn.cs`): only when the first snapshot the client sees is at tick ≤ 5
  (`WarpsAtStart`; never after a load or a seek): flash, light column, expanding rings, sparks, sound, and the model
  rises bottom-up as a hologram (`WarpFrac` read by `UnitRenderer`) over 0.35 + 1.5 s.
- Test scene `--scene deaths[:air|:naval|:structures|:big|:acu|:warp][:N]` (`DeathScene.cs`): army 1 groups killed
  together by a sandbox Kill 1.2 s after start, the run pauses N ticks later (defaults per group) for screenshots.

## Biome visuals (from "Maps in multiplayer and biomes", 8b-3)

- Biome visuals (client only): `BiomePalettes.For(biome)` (`TerrainRenderer.Biome.cs`, record `BiomePalette`: 8
  ground colours bound as terrain shader uniforms, shore glow (lava), water shallow/deep/foam/glow (lava water = dark
  drifting crust over a molten glow, nearly opaque), atmosphere sun/sky/fog colours set on `Atmosphere`'s static
  fields in `ApplyBiome()`, called every Draw so the Temperate menu backdrop resets them); `BiomePalettes.Minimap(b)`
  for the minimap and previews. Props (`PropRenderer.Biome.cs`): `NatureLook` (rock colours, moss, crack glow) and
  `BuildBiomeTree`: desert saguaros / dry shrubs / dead trees, tundra snowy conifers / birches, lava charred conifers /
  dead and ash trees, tropical palms / jungle trees; meshes rebuild when the biome changes. Temperate = the pre-8b-3
  constants (`BiomeVisualsTests`).

## Terrain self-shadows, unit LOD, minimap options (9b-2, client only)
- Terrain self-shadows: `TerrainShadowBake` (pure, tests in `TerrainShadowLodMinimapTests`) marches each height
  vertex towards `TerrainRenderer.SunDir` (the unit shadow map's sun) and stores the shadow line S = max(h(p + t·sun)
  − t·tan(elev)), first sample at `FirstStep` 0.75 (closer samples see only the vertex's own slope; flat ground under
  a low sun stays lit), then `Blur` (3×3, 2 passes) softens the stair steps. `Lit(y, S)` = 1 − smoothstep(0, 1.2, S −
  y) (`Softness` 0.6; the edge starts AT the line). Baked once per game in the `TerrainRenderer` constructor (always,
  even with the setting off; 1025²: ~30 ms Release, ~150 ms Debug; printed as `terrain shadows: baked`).
- Ground: `GroundLit` per vertex goes into the terrain LOD chunk meshes as vertex colour G (R/B = cavity AO); a
  stride-s vertex takes `BoxMean` over its (s+1)² footprint (like a mip level: no aliasing or pop on LOD changes).
  The terrain shader uses `terrainShadowGround(vColor.g)`: no texture fetch (a per-pixel fetch cost +0.55 ms GPU at a
  full-map view on 1024 maps). Units and aircraft (not props: own shader): S in an R32F texture (slot 7, bilinear, no mips: unit meshes are
  drawn only up to camera 260) compared with the fragment height (`terrainShadowAt`), so a tank in a valley's shadow
  darkens and an aircraft above it stays lit. Toggle: uniform `terrainShadowOn` (`TerrainRenderer.SelfShadows`,
  setting "Terrain shadows", `--terrain-shadows on|off`), no rebuild. `--sun-elevation DEG` moves the sun (testing).
- Unit LOD: `MeshLod.Simplify` (vertex clustering: cells of extent/N, vertices split by cell + normal bucket +
  material tag so flat shading, team colour and glow tags survive) builds `UnitModel.LodBody/LodTurret` on first use;
  used only if it has at most a set fraction of the full triangles (tank 296 → 82). `UnitLod.UseLod`: projected
  bounding-sphere radius < `PixelRadius` 7 px, hysteresis ±12 % per unit index (switch distance ∝ screen height:
  ~106 u for a tank at 540p). Only the opaque shadow + lit passes use LOD meshes (`UnitRenderer.Lod.cs`, same
  instance data incl. death fade); hologram, cloak, submerged and cargo passes keep full meshes. Setting "Unit detail
  LOD", `--unit-lod on|off|PIXELS` (a number = threshold radius, shows LOD up close); `--perf-log` prints LOD units
  and triangles.

The minimap options of that section are in [input-ui.md](input-ui.md).

## Selection overlays (10b-2, client only: `Rendering/RangeRingSet.cs`, `SelectionRangeOverlay.cs`, `WreckFogMemory.cs`, `UI/EnhancementChain.cs`)
- **Range / intel rings** (G20): for the selected own units and structures, `RangeRingSet` (pure) collects weapon
  max / min range, radar / sonar / omni, shield radius, stealth / jammer field. Equal rings merge by kind + radius
  (0.5 steps) + a cell of radius/3, so 500 tanks give a few outlines, not 500 circles; at most 128 rings
  (`DefaultCap`), kept by priority (source, then kind rank, then count). Rings of one (kind, radius) group are drawn
  as their union outline (arcs inside another ring of the group are skipped). `SelectionRangeOverlay` draws them with
  Rlgl lines following the terrain; hold Shift = also own + allied structures in view (dimmer). Toggle: key I
  (`Hotkeys.RangeRings`), setting `AppSettings.RangeRings` (Gameplay tab). No per-frame allocation (lists reused; a
  test asserts < 1 KB). Profiler lap "rings".
- **Fog memory of wrecks and deposits** (G11): the sim sends every prop (no intel filter). `WreckFogMemory` (pure)
  keeps, per wreck, the state last seen in a visible cell; `PropRenderer` draws remembered wrecks dim under the fog
  (wreck shader binds `FogRenderer`), hides never-seen ones, and updates them when seen again. Deposits in
  never-explored cells are hidden on the terrain and the minimap. The memory is client state, not saved.

The enhancement chain and the leftovers of that section are in [input-ui.md](input-ui.md).

# Maps: generator, files, props, biomes

The procedural map generator and its guarantees, map files and the map pack, trees / rocks / wrecks, biomes.

**Contracts of this area**
- `Terrain` is immutable and may be read from any thread.
- The generator's guarantees (starts connected, deposits buildable, flat start areas, deterministic per seed) are
  also checked on every map file (`MapChecks`).
- Map files: a new format version is a new decode branch, old files keep loading; the round trip is bit-exact.
- A biome is a look only: it never reaches the sim.
- Props never block movement or placement. Query them with `QueryRadius`, never by scanning the store.

Files and top-level types of this area: [index/map.txt](index/map.txt) (generated).

## Map generator (`Rts.Sim/Map`)
- `MapGenerator.Generate(MapGenSettings)` → immutable `Terrain`. Presets (`MapPreset`): Plains, Lakes, Highlands
  (terraces, cliffs, natural + carved ramps), Canyon (valley network in mesas), Archipelago (islands, needs naval).
  `WaterAmount` 0..1 (negative = preset default). Stages in `MapBuilder*.cs`: symmetric noise fields
  (`MapSymmetry`: N-fold rotation about the center, sector blending), preset relief, lakes (percentile of a lake
  field), flat start plateaus, connectivity repair (`MapConnectivity`: land components with clearance 2, coarse
  Dijkstra, slope-limited corridor carving in all N rotations), deposits (base / expansion clusters / scattered,
  validated on flat main-component land in every rotated copy), final flood-fill verification.
- Guarantee (tests in `MapGeneratorTests`): on every preset except Archipelago all starts are connected over
  `Terrain.CanTraverse(Land)`; deposits are buildable; start areas are flat; deterministic per seed; 1024 < 0.5 s.
- Archipelago instead gets sea lanes (`MapBuilder.Archipelago.cs`): `EnsureSeaLanes` labels the deep water eroded to
  battleship clearance (r=3) and digs symmetric channels (coarse Dijkstra; only the cheapest one per round, so few
  land bridges are cut) or inlets until every start has the same big sea within reach; `ShapeBeaches` cuts a
  slope-limited "harbor beach" from each start plateau to its sea (the plateau core is kept) so builders can walk
  to the coast. Tests in `NavalTests` (island size, naval site near each start, battleship-sized sea lane).

## Map props: trees and rocks (8b-1: `Rts.Sim/Map/MapProps.cs`, `World/PropStore.cs` + `PropSystem.cs`, `Rts.Client/Rendering/PropRenderer.Nature.cs`)
- `PropStore` holds wrecks, trees and rocks (`PropKind`, `PropFlags` Fallen/Burning/Burnt, `Variant`, `FallHeading`,
  `StateTick` = tick of the last flag change, `BurnEnd`; `Burning` list saved). Spatial grid (cell 4) is `[NoSave]`,
  built lazily, kept by Spawn/Free; `FindNearest` / `QueryRadius` return the same picks as a full scan (ascending
  index). Use `QueryRadius` instead of scanning `AliveList` (maps have up to ~11k props).
- Placement (`MapProps.Place`, called by `MapBuilder.Build` into `Terrain.Props`, spawned by `GameSetup`): jittered
  grid in sector 0 of `MapSymmetry`, forest/rock noise densities with preset factors, copied in all N rotations and
  kept only if every copy fits (land above water + 0.4, not steep, off start plateaus and deposits, spacing).
  Values: tree mass 0.4·size, energy 10·size (size 0.75..1.35); rock mass 5·r² (r 0.7..2.2). Counts (4 players,
  Plains): 256 = 460, 512 = 2660, 1024 = 10816. `SimWorld` sizes the store as max(4096, unit cap) + map props.
- `PropSystem.Run` (after ExperimentalSystem): ground movers (speed ≥ 0.3, not structures) knock over trees they
  touch (parallel detection → ChunkedBuffer → sequential `Fell`); burning trees spread fire before burning out
  (p 0.3 within 2.6) and become Burnt (energy × 0.25, mass × 0.5). `OnDamage` (first line of DamageSystem): area
  damage ≥ 25 ignites, ≥ 400 also knocks over away from the blast; nuke detonations knock over and ignite their
  outer radius. Trees never block movement or placement (structures absorb props under their footprint).
- Snapshot: every prop every tick (`PropView` incl. Kind/Flags/Variant/FallHeading/StateTick/BurnEnd).
- Client: `PropRenderer.Nature` buckets the nature props into 32-unit cells once per snapshot (counting sort), culls
  cells then props against the frustum, and fills 8 instanced meshes (4 tree species: 0,1 conifers, 2,3 broadleaf;
  4 rock shapes) with a near or a low-poly far mesh (beyond 150 from the camera). Fall: 1.3 s ease-in rotation about
  (dz,0,-dx) from `StateTick`; burning = glowing crown + `ParticleSystem.Fireball`/`Smoke` (≤ 64 trees per emit);
  burnt = charred, foliage mostly discarded. Own shader: vertex alpha = material (255 foliage with sway, 128 bark,
  0 rock), shadows received, fog of war, dithered out with the unit meshes. The wreck code skips non-wrecks (no
  strategic dots for trees). `ContextOrders`: "Reclaim tree +N energy" / "Reclaim rock +N mass"; `PickProp` only
  projects props near the ground point under the mouse.

## Map files (8b-2: `Rts.Sim/Map/MapFile.cs`, `MapLibrary.cs`, `MapChecks.cs`, `tools/Rts.Bench/MapExport.cs`, `data/maps`)
- Format `*.rtsmap` v1 (`MapFile.Version`; a new version = a new decode branch, old files must keep loading): magic
  "RTSMAP", u16 version, header block (`MapFileInfo`: name, description, players = start count, width, height,
  HeightBits, origin Generated/Preset/Seed/WaterAmount, informational only), u64 content hash, raw length, Brotli
  payload (size, water level, terrain seed, heights, deposits, starts, props in column order). Heights are integers
  (float bits, or multiples of 2^-HeightBits) as gradient-predicted (left + up - up-left) zigzag varints. The id is the
  file name, not stored. `Decode` checks magic/version/sizes and the hash (`MapFileException` = damaged); `ReadHeader`
  reads only the header (menus). The round trip is bit-exact (heights, deposits, starts, props; passability is
  recomputed by `Terrain`'s constructor). Content hash = `Hash64` of the raw payload (`MapFile.ContentHash`).
- `MapFile.Quantize(t, 8)` rounds heights to 1/256 (files ~4x smaller); the exported file is then an exact copy of the
  quantized terrain. Rounded values must be normalized with `+ 0f` (a negative value that rounds to 0 gives -0,
  whose bits differ, and the round trip broke on Archipelago).
- `MapLibrary`: `DefaultDir` = data/maps, `List` (headers, damaged files skipped), `Resolve(id or path)` (exact id,
  then case-insensitive), `Load`. `MapChecks.Check(terrain, db, naval)` = the generator's guarantees for any terrain
  (same rules as `MapGeneratorTests`): 2-8 starts connected over land (naval maps: land + water surface), every
  deposit buildable by every faction's extractor / hydrocarbon role, a flat start area (r 16) with a factory site and
  base resources per start. Faction roles only.
- Games: `SkirmishSettings.MapFile` (null = generator). `GameSetup.TerrainFor(s)` = `MapFile.TerrainFor(players)`:
  the whole file when every start is used, else a copy with `SpreadStarts` (the subset, always with start 0, with the
  largest minimum pairwise distance, then the largest distance sum; exhaustive, ascending index), so
  `StartPositions[army]` is the army's start everywhere. More players than starts throws. Props come from the file.
  A file exported with `--exact` gives the same `StateHash` as the generated map (`MapFileTests`).
- Saves/replays: `GameInfo.MapFile` (id) + `MapFileHash` + `MapName`, `Map` (generator settings) = null and no
  embedded terrain. Loading (`SaveGame.MapFileTerrain`) refuses with `IncompatibleSaveException` a map that is not
  installed or whose header hash differs, then rebuilds `TerrainFor(info.Players.Count)`; the usual terrain hash check
  follows. Fields are optional in the JSON: older saves of generated maps load unchanged. Replay file names use the
  map id (`SaveStore`).
- `Rts.Bench -- mapexport <out> <size> <seed> <players> [preset] [name] [--water X] [--exact] [--desc T]`; `--pack
  tools/maps/pack.txt data/maps` writes the pack (one map per line: id size seed players preset "Name" "Desc");
  `--scan <size> <players> <preset> <seed0> <count>` lists which seeds pass and their file sizes. Each map is checked
  on the terrain read back from the encoded bytes (exact round trip + `MapChecks`) and refused (exit 1) if it fails.
  Re-exporting after a generator change changes the files and their hashes (saves on the old files are refused).
- Pack (10 maps, 2373 KB): 256: twin_meadows 2p, red_gorge 2p, four_ponds 4p, three_terraces 3p (54-64 KB);
  512: coral_strait 2p (Archipelago), highland_cross 4p, six_fields 6p, mesa_pentagon 5p (186-210 KB); 1024:
  great_lakes 8p, badlands 4p (637-701 KB). Pinned test hash: twin_meadows 0AF9AD17AF615153.
- Client: `Menus/MapFileCache` (headers sorted by players / size / name, decoded files cached, thread-safe),
  `GameConfig.MapFile` (Problem(): "X has room for N players", "not found"), `--mapfile NAME` (also with `--setup`).
  Skirmish setup row "Map" (Cycle: Random map | pack maps); a file map hides the generator rows and shows its
  description; `MapPreview.RequestFile(id, players)` draws the starts the game will use, unused ones as empty slots.

## Maps in multiplayer and biomes (8b-3: `Rts.Net/Session/*Session.Maps.cs`, `Rts.Sim/Map/Biome.cs`, `Rts.Client/Menus/LobbyScreen.Maps.cs`, `Rendering/*.Biome.cs`)
- Biome (`Biome` enum: Temperate, Desert, Tundra, Lava, Tropical; `Biomes.Id/Name/TryParse/Step`) is a look only: it
  never reaches the sim (`BiomeMapTests`: same `StateHash` on every biome). Map files store it as an optional trailing
  header byte (older v1 files read as Temperate); the content hash covers the payload only, so the same terrain has the
  same hash on every biome (`MapFile.WithHeader`, `mapexport --biome B`). Choice: `MapGenSettings.Biome`,
  `SkirmishSettings.Biome` (null = the map file's biome, else Temperate: `GameSetup.BiomeFor`), `GameInfo.Biome` (id,
  null = temperate) in saves/replays. Client: `GameConfig.Biome` / `EffectiveBiome`, `--biome X`, skirmish setup row
  "Biome" (cycles Map default | the five), lobby row (host edits, clients read).

The other parts of that section: lobby, map transfer and game start in [multiplayer.md](multiplayer.md), biome visuals
in [rendering.md](rendering.md).

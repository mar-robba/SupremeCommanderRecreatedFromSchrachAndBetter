# Multiplayer (lockstep)

`src/Rts.Net`: the lobby, the lockstep turns, LAN discovery, map and save transfer, observers, drop votes, and the
client's multiplayer screens.

**Contracts of this area**
- Deterministic lockstep: every peer runs the full sim, only commands travel, the host relays every turn.
- `Rts.Net` has no raylib dependency.
- Every peer steps identical command lists: turns are merged in player order and each player's commands are filtered
  to its own army.
- A protocol change bumps `Msg.ProtocolVersion` and appends fields; joining needs the same sim build.
- Speed, pause, delay and observers never affect determinism. An observer is not an army: the sim never waits for it.
- Floats must be bit-identical on every machine: [core.md](core.md) "Deterministic math (cross-OS)".

Files and top-level types of this area: [index/multiplayer.txt](index/multiplayer.txt) (generated).

## Multiplayer (`src/Rts.Net`, no raylib; client screens in `Rts.Client/Menus`)
- **Model**: deterministic lockstep on top of the replay machinery. Every peer runs the full sim; only commands
  travel. Star topology: the host relays every turn (no peer-to-peer). TCP, length-framed binary messages
  (`Wire/Framing.cs`, `Wire/Messages.cs`: Hello, Welcome, Reject, Lobby, LobbyRequest, Chat, Start, Loaded, Go, Turn,
  Hash, Desync, Drop, Speed, Delay, Ping/Pong, Pings, Leave, Abort). Port 47624 TCP (game), 47625 UDP (LAN
  discovery: `LanAnnouncer` broadcasts once a second, `LanBrowser` lists the games; protocol + sim build compared).
  Joining needs the same `GameInfo.CurrentSimBuild` (the Rts.Sim assembly's MVID) and protocol version.
- **Lobby** (`Lobby/LobbyState.cs`): 8 slots (Open / Human / AI / Closed) with faction, team, color, AI difficulty,
  ready; `LobbyOptions` (map size, preset, water, seed, `OnDrop`). The host owns the state and applies requests
  (`HostSession.SetSlotKind/SetSlot/MoveHost/SetOptions/Kick`, clients send `LobbyRequest`); every change is broadcast
  and published as an immutable copy (`NetSession.Lobby`, any thread). UI threads read events with `TryGetEvent`.
  (6b) `LobbyOptions.Game` carries the game options (protocol version 2; host edits them in the lobby, clients read
  only); chat lines can be allies-only. See "Game options & team play".
- **Start**: `HostSession.StartGame` (checks `LobbyState.Problem()` and `FactionKnown`) broadcasts `GameStart` (lobby
  copy + initial delay). Army slots in slot order = armies 0..n-1; `NetGameSetup.ToSkirmish` maps slots to
  `SkirmishSettings` (AI flags, difficulty, teams, colors, names, factions; "random" resolved from the seed). Each peer
  builds the sim on a worker, reports `Loaded(StateHash)`; the host sends Go when every hash equals its own, else
  Abort (everybody back to the lobby).
- **Turns** (`Lockstep/LockstepHooks.cs`, the runner's `ITickHooks`, wrapping the replay recorder): every player
  sends exactly one turn per tick, contiguous. Local commands of the frame stepping to T+1 go into the turn for
  T+1+delay (ticks 1..delay are pre-sent empty at Go). `Hold()` = no Go yet, or some player's turn for tick+1 is
  missing (`WaitingFor`). `BeforeStep` merges the turns in player order, keeps only commands of that player's army,
  renumbers `Sequence`/`ExecuteTick` → identical command lists on every peer (and in every replay). The local copy is
  decoded from the bytes sent, so all peers step identical objects. Turn payload = `TurnEncoder`/`TurnDecoder`:
  the persistence graph writer with a per-sender type table (type names/layouts go over the wire once per game).
- **Delay**: sender-side, host-adaptive: `DelayFor(maxRttMs, speed)` = 1 + (max RTT + 20 ms) / tick ms (1..30 ticks),
  recomputed every 250 ms from the pings and broadcast (Delay). A larger delay leaves a gap filled with empty turns;
  a smaller one makes the sender wait until the ticks catch up (commands stay pending).
- **Speed/pause**: requests to the host (`NetGame.RequestSpeed`), broadcast (Speed) and applied to every runner
  (`NetGame.AttachRunner`); they don't affect determinism.
- **Desync detection**: every 10 ticks each client sends `StateHash` to the host; the host compares with its own →
  Desync broadcast (game continues; the client shows a banner and writes the replay so far: `GameRecording.WriteReplayNow`).
- **Disconnects**: a connection silent for `TimeoutSeconds` (10 s) or closed → the host picks lastTick = the last turn it
  relayed for that player, broadcasts Drop after those turns; every peer applies empty turns afterwards and a
  `PlayerLeftCommand(army, OnDrop)` at lastTick+1 (`Rts.Sim/Orders/MultiplayerCommands.cs`, `MultiplayerSystem`):
  AI takeover (`Army.IsAI`) or defeat. Host lost → a client continues offline (drops every other player after its
  last received turn; `NetGame.Offline`). Reconnecting is not supported.
- **Replays**: the MP game's `GameRecording` (`NetGameConfig.Recording`) is a normal skirmish replay of the merged
  command log (remote humans are non-AI armies whose commands come from the log): `Rts.Bench -- replay` verifies it.
- **Client** (`Menus/MultiplayerScreen` → `LobbyScreen` → `NetLoadingScreen` → `NetInGameScreen`): the screen that
  holds the session passes it on (`_handedOff`), any other exit calls `Leave()`. `GameSession` gets the lockstep
  hooks through `wrapHooks`, `NetKeys` (Space/+/− become requests; no debug spawns, no F7 fog reveal; Enter = chat, `Hotkeys.Chat`) and
  `KeyboardCaptured` (chat line open → game hotkeys blocked). No saving and no Restart in MP; Esc menu doesn't pause;
  local defeat → observer (the sim must keep running so the others don't wait); game over → end screen (runner
  stopped: the others may then wait if they continue).
- **Tests**: `tests/Rts.Net.Tests` (framing, codecs, lobby round trip, factions, loopback TCP matches of 2–4 peers with
  random commands → identical hashes + MP replay re-simulates, drops, desync, speed, LAN discovery);
  `Rts.Bench -- mp [peers] [ticks] [mapSize] [aiSlots] [threads] [drop]` (headless self-test, exit 1 on desync).
- **Float determinism review** (2026-10-16). Lockstep needs bit-identical floats on every machine:
  - Basic IEEE ops (+ − × ÷, sqrt, float↔int casts) are deterministic: RyuJIT uses scalar SSE2 on x64 and NEON on
    ARM64, no x87 and no implicit FMA contraction; .NET 9+ float→int casts saturate the same on x64 and ARM64. No
    `MathF.FusedMultiplyAdd`, no explicit SIMD, no `GetHashCode` in Rts.Sim (checked).
  - `System.Numerics.Vector2/3` (≈200 uses): same results on the same .NET + ISA; their intrinsic code paths could in
    principle round differently between x64 and ARM64 (dot products) — unverified.
  - **Risk** (fixed 2026-11-12 by 10a-1, section "Deterministic math (cross-OS)"): transcendental functions (`MathF.Sin/Cos/Atan2/Pow/Log2`, ~40 calls in Rts.Sim: weapons, projectiles,
    missiles, movement, AI memory decay, map generation) call the OS C runtime (glibc libm, Windows UCRT, macOS
    libSystem), which may differ in the last bit → Linux vs Windows games can desync. Same OS + same build: identical.
  - Safety net: `Loaded` hashes at start, `StateHash` every 10 ticks, desync banner + replay dump.
  - Done (10a-1): all sim trig goes through software `DMath`; still to do: verify Linux↔Windows↔ARM64 with the
    golden test and `Rts.Bench -- replay` of the same file on each machine.

## Maps in multiplayer: lobby, transfer, game start (from "Maps in multiplayer and biomes", 8b-3)

- Lobby (`Msg.ProtocolVersion` 3; a v2 peer is refused with both versions in the text): `LobbyOptions` += `MapFile`
  (id, null = generated), `MapHash`, `MapName`, `MapPlayers`, `MapBytes`, `MapBiome` (the file's), `Biome` (0 = map
  default, 1 + biome) → `HasMapFile`, `EffectiveBiome`; `NetSlot` += `MapReady`, `MapProgress`. The host resolves the
  file outside the lock (`HostSession.SetOptions` → `ResolveMap`: by hash in data/maps then `MapDir`, else by id) and
  fills the header fields; a new map resets every client's map state. `LobbyState.Problem()` adds "too many armies for
  the map" and "X is downloading the map (N %)"; the host refuses Ready from a slot without the map. The LAN listing
  shows the map name.
- Transfer: on each lobby the client (`ClientSession.CheckMap`) looks for the file with `MapLibrary.Find(id, hash,
  MapDir)` (data/maps, then the download folder; same id first, then any file with that hash). Missing → `MapRequest`
  (id, hash); the host uploads `MapChunk`s of 32 KB (`MapChunkBytes`), at most 256 KB (`MapWindowBytes`) beyond what the
  client acknowledged; the client acks every chunk with `MapStatus` (state 0 receiving / 1 have it / 2 failed, bytes),
  which also drives the host's slot progress. Uploads are pumped by the host's monitor thread; env
  `RTS_MAP_UPLOAD_KBPS` throttles them (screenshots/tests). Files over 64 MB (`MaxMapBytes`) are refused. The complete
  file is verified on a worker (`MapFile.Decode` + header hash = the lobby's hash) and stored as `<id>.rtsmap` (via a
  `.part` file and a move; `<id>_<HASH>.rtsmap` when another file already has that name) in `MapDir` =
  `MapLibrary.DownloadDir` (dev: `./downloaded_maps`, release / `RTS_USER_DIR`: next to `saves/`). Status for the UI:
  `NetSession.MapTransfer` (immutable `MapTransferStatus`: Present / Downloading / Verifying / Failed, received, total,
  path), `MapPath`. A broken download starts again from zero.
- Game start: `NetGameSetup.LoadMap(start, mapDir)` / `ToSkirmish(start, map)` / `CreateSimulation(db, start, mapDir)`
  (`HeadlessPeer` and `NetLoadingScreen` use the session's `MapDir`); `NetGameConfig` sets `MapFile` + `Biome`; the
  net replay records the map id and hash like a skirmish (`Recording(..., map)`). `Rts.Bench -- mp ... map=ID
  biome=X` runs the self-test on a file map (named args anywhere; peers wait for the transfer). Tests:
  `MapTransferTests` (codec, pack map on both sides, missing map downloaded + used in sync, corrupted upload refused,
  old protocol refused, big map timing), `BiomeMapTests`.
- Lobby UI (`LobbyScreen.Maps.cs`): host "Map" row cycles Random map | `MapFileCache` maps, "Biome" row; a file hides
  the generator rows (generated maps: 7 rows, fit at 720p); the preview loads the file once it is here; the info line
  and the bottom bar show "Downloading map N % (x/y KB)"; Ready stays off until the map is present (`--autostart`
  clients wait for it). Previews (`MapPreview`, setup and lobby) bake in the biome's minimap colours.

## Observers, drop votes, lobby pings (10a-2: `Rts.Net/Session/*Observers.cs`, `Menus/LobbyScreen.Observers.cs`, `Menus/NetInGameScreen.Observers.cs`)
- Protocol 5 (`Wire/Messages.cs`): `MsgType` DropVote=24, LobbyPings=25, Resign=26; `Hello` has a trailing u8 observe
  flag; `LobbyRequest.Observe` (-1 keep / 0 play / 1 observe, appended).
- Lobby: `LobbyState.Observers` (NetSlot Kind=Observer, max 16) is written after the slots; `People` = humans in slots +
  observers, `EntryOf(pid)`. Observers need the map (MapReady) but no Ready. A join to a full game lands among the
  observers (before: refused). Host: `SetObserver(pid, on, slot)` moves anybody, `KickObserver(pid)`; clients ask with
  `NetSession.Observe(on, slot)`. An observer is not an army: `GameStart.TurnPlayers` never contains it.
- In game an observer has `NetGame.LocalTurn = -1`: it receives every turn (early turns are buffered until its hooks
  exist, it may load after Go), sends none, and the sim never waits for it. Its Loaded hash is checked like a player's,
  but a mismatch only warns that observer. Leaving drops nobody. Client: read-only session (`Ctx.ReadOnly`), fog off,
  players panel rows switch the view (`Ctx.LocalArmy` + fog on, like replays); no pause/speed requests;
  `NetLoadingScreen` hands off on an early Go once the session exists; the session uses army 0 as its camera start.
- Resign (`NetGame.Resign`, `LockstepHooks.RequestResign`): a defeated player stops sending; its last tick = the last
  produced turn (applied in Hold or BeforeStep, so a held game resigns too), the host relays a Drop (Defeat) to the
  others, so nobody waits for it again; it keeps watching as an observer.
- Drop votes (`HostSession.Observers.cs`): a player may vote against the turn player everybody waits for
  (`NetGame.CanVoteDrop`: not itself, not the host (no host migration), neither dropped). Votes count only after
  `VoteMinStallSeconds` (3 s) without a turn from the target, any turn from it clears them; a strict majority of the
  remaining non-dropped players other than the target (`NetGame.VotesNeeded`) closes its connection and drops it with
  the lobby's `OnDrop` action. The host broadcasts the tally (`DropVoteTally`); the client banner counts down
  `DropVoteDelaySeconds` (5 s) before showing "Drop X". The host's silence timeout (`TimeoutSeconds`) still applies.
- Pings: the host sends `LobbyPings` (player id → ms) every second; `NetSession.PingOf(pid)` (host = 0, shown blank).
- Sounds reuse the UI cues (`Audio/UiSounds`): chat from others = click; joined / ready / game start = confirm;
  left / dropped = error. Lobby join/leave/ready come from diffs of `People` per frame.
- Tests: `tests/Rts.Net.Tests/ObserverTests.cs` (codec, lobby moves, full game, pings, late observer in sync, resign,
  votes); `Rts.Bench -- mp ... obs=N`.

## Multiplayer save/load (10a-3: `Rts.Net/Session/*Saves.cs`, `Rts.Net/Lobby/LoadLobby.cs`, `Rts.Sim/Persistence/MultiplayerSaveInfo.cs`, `Menus/*Saves.cs`, `Menus/LobbyScreen.Load.cs`, `Menus/NetLoadListScreen.cs`)

- **Order**: any peer asks (`NetGame.RequestSave(name)`: Esc → Save Game, F5 = `mp_quicksave`), the host decides
  (`HostSession.OrderSave`). Refused while desynced, while another save is pending, or when the host is not a live turn
  player and fewer than 2 live turn players remain (a lone client is gated by nobody). The save tick is
  T = 1 + the highest turn the host holds from anybody (its own included), computed and broadcast under the session
  gate, so no peer can step past T before the order arrives. A paused game steps up to T (`NetGame.HoldForPause`).
- **Same save**: at T every peer (players and observers) captures the world on its sim thread (`NetGame.CaptureSave`
  = `GameRecording.CaptureForNet`: replay so far + this peer's camera), writes `<SaveDir>/<name>.rtssave` on a worker
  and reports Hash64 of the state bytes (`SaveDone`); the host compares them and broadcasts `SaveResult` ("Game
  saved", or a system chat line naming who differs / failed, timeout 30 s). Identity = save id + state hash; headers
  differ per peer (camera, `SavedBy`).
- **Header**: `GameInfo.Multiplayer` (`MultiplayerSaveInfo`: SaveId, StateHash, Tick, the encoded lobby, SavedBy +
  SavedBySlot, Auto, Humans). Single-player saves keep it null; the save list tags these files MULTIPLAYER.
- **Load lobby**: Multiplayer → Load Saved Game (`NetSession.ListMultiplayerSaves`) or `--host [port] --load-mp FILE`
  → `HostSession.OpenLoadLobby(path)` (refuses another `CurrentSimBuild`, a missing map file). `LobbyState.ForLoad`:
  factions, teams, colours and options come from the save (only On drop can change); saved humans become Open slots with
  `SavedName`, taken by a joiner of that name (case-insensitive; the host prefers `SavedBySlot`), "Take", or handed to
  the AI by the host ("AI plays": an AI takeover command at StartTick+1). `Problem()` blocks the start while a saved slot
  is free or a person's copy isn't ready.
- **Transfer**: each client looks for its copy by save id + recomputed state hash (`ClientSession.Saves.cs`); else (or a
  changed copy) the host sends the file in chunks like a map file (`SaveTransfer`, progress `SaveProgress` in the slot),
  verified before it is stored.
- **Resume**: `NetSession.LoadSavedGame` checks id + hash + tick and decodes; every peer reports its loaded state hash,
  Go as usual, lockstep resumes at T. The camera is restored only from a file this peer wrote (`WroteSave`); the replay
  of the loaded game continues the saved replay.
- **Autosave**: the host's Autosave setting (`HostSession.Autosave`, `--autosave-every TICKS` forces it) orders
  `mp_autosave_1..3` every 3000 ticks, the same order path (every peer writes it).
- Checks: `tests/Rts.Net.Tests/SaveLoadTests.cs`; `Rts.Bench -- mp ... save=TICK` (DEV_GUIDE "Multiplayer checks").

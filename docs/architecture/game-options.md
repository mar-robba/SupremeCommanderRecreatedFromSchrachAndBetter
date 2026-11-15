# Game options, victory, teams, score

The lobby options of a game (victory rule, unit cap, resources, fog, speed, cheats, prebuilt base), team play
(giving units and resources, overflow sharing, allied chat) and the score history.

**Contracts of this area**
- `new GameOptions()` is the behavior before options existed (bench, tests, CLI games, the reference match); the
  menus start from `SetupDefaults()`.
- Options are immutable during a game, saved with the world, and copied into the save header and the snapshot.
- Victory is decided in `CleanupSystem` by `Options.Victory`; `CleanupSystem.Defeat` is also the multiplayer drop path.
- The speed lock is client / runner only, never the sim.
- Enums are append-only.

Files and top-level types of this area: [index/game-options.txt](index/game-options.txt) (generated).

## Game options & team play (wave 6b: `Rts.Sim/GameOptions.cs`, `PrebuiltBase.cs`, `Economy/DiplomacySystem.cs` + `EconomyShare.cs`, `Rts.Client/UI/DiplomacyPanel.cs`)
- **`GameOptions`** (SupCom lobby options): `Victory` (Assassination / Supremacy / Annihilation / Sandbox), `UnitCap`
  (default 1000, setup choices 250/500/750/1000, CLI 1..16384), `Resources` (Normal / Low / High), `Fog` (Normal /
  Explored / None), `Speed` (Adjustable / Normal = locked 1x / Fast = locked 2x), `Cheats`, `Prebuilt`. Enums are
  append-only. **`new GameOptions()` = today's behavior** (cheats on: bench, tests, CLI games, the reference match);
  the setup screen and the lobby start from `SetupDefaults()` (cheats off). `Parse/ToSpec` = the CLI spec, `Describe`
  = one-line summary (LAN beacon, menus), `SameAs`, `Clone`, `LockedSpeed`.
- **Flow**: `SkirmishSettings.Options` → `GameSetup.CreateSkirmish` applies them → `SimWorld.Options` (saved with the
  world, immutable during the game) + `GameInfo.Options` (save/replay header copy; null in pre-6b files = defaults) +
  `SimSnapshot.Options` (same object, read only). Explicit `SkirmishSettings.StartMass/StartEnergy >= 0` still win.
- **Victory** in `CleanupSystem` by `w.Options.Victory`: assassination = commander death defeats the army (its units
  self-destruct); supremacy = no finished structure, engineer or commander (SCUs count); annihilation = no finished
  unit; both scan every slot (carried and just-spawned units count, unfinished never). Sandbox = nobody is defeated,
  no winner. `CleanupSystem.Defeat(w, army)` (Defeated + kill all, carried too) is also the multiplayer drop path.
- **Unit cap**: `Army.UnitCap` (structures included); factories/engineers don't start units at the cap; sandbox lifts
  it. Snapshot `ArmyView.UnitCap`; HUD `UnitCapCounter` right of the resource bar (amber at 90%, red + one toast at
  the cap). **Start resources**: Low = half the commander storage; High = full storage + a per-army bank
  (`Army.StartBankMass/Energy` = 2x storage) that `EconomyShare` releases into free storage every tick.
- **Fog**: None → `Intel.Enabled = false` (client `FogRenderer` off); Explored → every team's fog cells start as
  `FogExplored` (enemy units still hidden outside vision). **Speed lock**: client/runner only (never the sim): single
  player sets the locked speed at start and +/- only toast; in multiplayer `NetGame.LockedSpeed` is the initial speed
  on every peer and `HostSession.ApplySpeed` refuses changes (pause still works).
- **Cheats**: `SimWorld.CheatsEnabled = Options.Cheats` gates debug spawns. In a game with cheats on, F9/F10 send
  `EnableCheatToolsCommand` (sandbox panel/console in a normal game); with cheats off a toast says no. The old effect
  test keys on F9/F10 only work with `--fxtest`. **Prebuilt base** (`PrebuiltBase.cs`, faction roles only): 4 mex on
  the nearest deposits (≤ 40), 4 power generators behind, a land factory toward the map center (ring search, clear
  of commanders).
- **Team play** (`Orders/DiplomacyCommands.cs` → `DiplomacySystem`, sequential case lines in `CommandSystem`):
  `GiveUnitsCommand` (to a living ally; refuses commanders, carriers holding one, units under construction and
  carried units; skips units over the receiver's cap; a factory's unfinished unit and a carrier's cargo go along,
  `UnitTransfer.Transfer`), `GiveResourcesCommand` (clamped
  to the stock and the ally's free storage), `EnableCheatToolsCommand` (once, only with cheats on; keeps the victory rule and the unit caps). `ArmyStats.Mass/EnergyGiven/Received` count
  gifts. **Overflow sharing** (`EconomyShare`): `EconomySystem` reports the raw pre-clamp surplus
  (`NoteOverflow`), then `Distribute` (after the army loop) splits it equally among living allies with room; order
  per tick: overflow first, then the High-resources bank. Armies without allies get identical arithmetic, so the
  reference hash did not change.
- **Lobby & net**: `LobbyOptions.Game` (codec appended after `OnDrop`: victory U8, cap VarU clamped, resources/fog/
  speed U8, flags cheats|prebuilt), `Msg.ProtocolVersion` 2; `NetGameSetup` passes them to the skirmish; LAN beacon
  shows `Describe()`. **Allies-only chat**: `ChatLine.AlliesOnly` (a trailing byte of the Chat message); the host
  relays it only to the sender and the players on the sender's lobby team (team 0 = no allies, only the sender);
  in game Shift+Enter opens "To allies:", lines show as "[Allies] name: text".
- **Client UI**: `Menus/GameOptionsBlock.cs` (setup screen 3 columns, lobby 2 columns, tooltips); F4 / top-bar
  "Diplomacy" panel (players, team, ally/enemy/defeated, units n/cap for you and allies, score, give units / 25% of
  stored mass or energy to living allies; hidden in replays and for defeated players).
- **Score history (G6)**: `ArmyStats.History` = a `ScoreSample` (tick, mass/energy income, units, kills, mass
  collected, score) per army every 300 ticks, recorded in `CleanupSystem` (saved, not in `StateHash`).
  `ArmyStats.Score` = mass collected / 10 + energy collected / 100 + 10 per kill + 2 per unit or structure built
  (snapshot `ArmyView.Score`). End screen: `Menus/ScoreGraph.cs` line chart per army (army colors, metric tabs,
  live last point).

# Commands and orders

How intent enters the sim: `Command`s, applied at the start of a tick, and the per-unit order queues they fill. The
client side (selection, drags, markers, notifications) is in [input-ui.md](input-ui.md).

**Contracts of this area**
- Every change to the sim from outside is a `Command` applied by `CommandSystem` at the start of a tick. That is what
  makes replays and lockstep work: they record and send command lists, nothing else.
- A new command type adds one `case` line in `CommandSystem` (a shared file: keep the edit minimal).
- The sim checks every unit of a command against the command's army.
- Enums (`OrderType` and the like) are append-only: saves, replays and the wire format store their values.

Files and top-level types of this area: [index/orders.txt](index/orders.txt) (generated).

## Orders & selection UX (wave 7b: `Rts.Sim/Orders/QueueEditCommands.cs` + `PingCommands.cs`, `Rts.Client/Input`, `UI/Alert*`, `UI/PingMarkers.cs`)

- **Queue editing (sim, sequential, one `case` each in CommandSystem):** `MoveWaypointCommand(Army, Units, From, To)`
  moves every queued Move/AttackMove/Patrol/Ferry/Unload order of those units whose group point `Order.Pos` is within
  `QueueEditSystem.MatchEpsilon` (0.05) of From (Ferry: also the drop point `Aux`; patrol `Aux` chains follow; a moved
  front order re-paths). `DeleteWaypointCommand` removes the same matches. `FormationMoveCommand(..., From, To, Facing,
  Queue)` = line formation across a right-drag: `QueueEditSystem.LineOffsets` (spacing = largest radius × 2 + 1.2,
  rows ordered by side position so paths do not cross), `Aux` = facing, and a unit that arrives turns to it
  (`StillTurning`, ≤ 60 ticks). The snapshot's `OrderView.Point` / `Aux` carry the raw group point for the markers.
- **Pings:** `MapPingCommand(Army, PingKind Alert|Move|Attack, Pos)` → `PingState` (per-world CWT, plain list, saved;
  not in StateHash), throttled 3 per army per 3 s, live 10 s, max 32. `SimSnapshot.Pings/PingCount/PingSerial` holds
  only the view army's non-enemies (observers / replays: all). Like every command they ride replays and lockstep
  (`PingTests.ReplayRecreatesThePing`, `PingNetTests`).

The client parts of that section (input, notifications) are in [input-ui.md](input-ui.md).

# Transports and cargo

Land transports, air staging, carriers and the Atlantis: loading, unloading, ferries, docking, fuel.

**Contracts of this area**
- One mechanic for all of them: the `Cargo` block of the carrier's blueprint.
- A carried unit has `UnitFlags.Carried`: alive, its handle valid, but out of the `AliveList`, the grid, intel, the
  economy and `snap.Units`. `TransportSystem` runs before the alive-list rebuild so boarding takes effect the same tick.
- A destroyed carrier kills its cargo, recursively.
- The state is `SimWorld.Cargo`: plain arrays and lists (saveable).

Files and top-level types of this area: [index/transport.txt](index/transport.txt) (generated).

## Transports & cargo (`Rts.Sim/Transport`)
- One mechanic for land transports, air staging and the Atlantis: a **carrier** has `Cargo {Layer Land|Air,
  Capacity (slots), LoadRange, RepairRate, RefuelRate, KeepDocked, HoverHeight}` in its blueprint
  (`Data/UnitBlueprint.Transport.cs`). What a unit weighs as cargo is `TransportSize` (-1 = derived: land T1 = 1,
  T2 = 2, T3/commander = 4 slots; aircraft T1–T2 = 1, T3 = 2; structures, ships and experimentals can't be carried).
- **Carried units** get `UnitFlags.Carried`: they stay alive (handles valid, `IsActive` true) but `RebuildAliveList`
  skips them, so they are off the grid, untargetable, out of intel/economy and out of `snap.Units`. Their position
  follows the carrier each tick. When a unit boards, weapon locks and front Attack orders on it are dropped
  (`PurgeReferences`; Attack orders issued later are dropped by `DropAttackOnCarried`). A destroyed carrier kills
  its cargo, recursively (no emergency drop, like SupCom).
- State in `SimWorld.Cargo` (`CargoState`: `Carrier[]`, `Holds[]` lists, `Fuel[]`, `AirMode[]`, timers) — plain
  arrays/lists (save/load friendly).
- Orders (`TransportOrders` builds and issues them; `CommandSystem` hands Load/Unload/TransportTo/Ferry/Dock to it):
  `Load` (land unit → walk to the transport and board; transport → fly to the unit and pick it up), `Unload` (land
  transports fly there, land and drop the cargo in rings; air carriers launch their aircraft), `TransportTo`
  (issue-time only: cargo is split among the selected transports, biggest units first, into Load + Unload; no
  transports = Move), `Ferry` (transport: shuttles between a pickup beacon and a drop point forever, leaves 3 s after
  the last boarding or when full; land unit: walks to the beacon and boards a ferry transport), `Dock` (aircraft:
  to a given/nearest air carrier). Queued orders after the transport order resume after the drop.
- VTOL (`Systems/MovementSystem.Vtol.cs`): air units with land cargo hover and land (`AirMode`) to load/unload.
- Fuel: aircraft burn `FuelSeconds` (0 = derived 400/600/800 s by tech; < 0 = unlimited) while flying; out of fuel
  = half speed. Idle aircraft below 50% HP or out of fuel auto-dock at the nearest air carrier with room.
  Air Staging repairs/refuels for free and releases aircraft when full; `KeepDocked` carriers (the Atlantis) keep
  them until Unload (Launch) or until the aircraft gets new queued orders.
- Mobile factory (Atlantis): builds aircraft on deck (`ExperimentalSystem.CarryCurrent` keeps the unit being built on the carrier)
  and carries them. Pausable like any factory (H). It can dive (`Movement.Layer: "Sub"` + `StartSurfaced`, see Naval):
  while submerged (`Submarines.IsSubmerged`) the deck is closed: no docking (aircraft wait above it; `FindAirCarrier`
  skips it), no launching (Unload waits, docked aircraft stay aboard), a finished aircraft goes straight into the hold
  with a Dock order in front of its orders (`TransportSystem.DockFinished`) and takes off after surfacing; with a
  full hold production waits (`WaitsForRoom`).
- Capture (`UnitTransfer`): a captured carrier's cargo changes army with it (hold order, recursive); carried units
  can't be captured and a carrier holding a commander can't be captured. Boarding ends a factory roll-out
  (`MoveState.LeaveTime = 0`). Air staging repairs veterans to their raised max health.

- Snapshot (`Snapshot/TransportViews.cs`, written by `TransportSnapshotWriter`): `CargoViews` (carrier, used/capacity
  slots, landing) + `CargoUnits` (blueprint, health, fuel, size of each carried unit), `Ferries` (army, pickup, drop,
  transports), `UnitView.Fuel`.
- Client: models in `Rendering/UnitModels.Transport.cs`, cargo drawn under transports / on the pads and the deck in
  `Rendering/UnitRenderer.Cargo.cs`, ferry routes in `Rendering/TransportOverlay.cs`, orders/context clicks/cargo
  and fuel in the UI; `--scene transport` (`TransportScene.cs`) shows everything.

Assist chains moved to [economy.md](economy.md).

## Cybran carrier (from "Missing units & walls", 8a)

- **Cybran carrier** = the generic Atlantis code (Cargo Air + Factory + Mobile, KeepDocked) on a `Water`-layer ship
  that cannot dive: builds aircraft at sea, 16 deck slots, repairs/carries/launches them, docked aircraft die with
  it. `CarrierTests`.

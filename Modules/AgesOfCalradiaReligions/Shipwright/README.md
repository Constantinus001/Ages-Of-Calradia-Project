# Ages of Calradia Religions — Shipwright sidecar

This War Sails-dependent sidecar adds **Commission a custom Dromon** to the
town menu when the player is visiting a port. A nine-step wizard configures
Fore, Aft, Bow, Hull, Side, Deck, Sail, Roof, and Name. It creates one
`empire_heavy_ship` candidate, temporarily attaches it to the current port, and
opens War Sails' native port screen in Trade mode. The native screen remains
responsible for the ship preview, compatible
upgrade pieces, naming, prices, affordability, confirmation, cancellation, and
the final ownership transfer.

## Architecture and compatibility

- Supported contract: Bannerlord v1.4.8 and NavalDLC (War Sails) v1.2.8.
- Native targets: the public `Ship(ShipHull)` constructor, the four-argument
  callback `PortState` constructor, `GameStateManager.CreateState`, and
  `PortScreenModes.TradeMode`.
- No Harmony patches, copied Gauntlet screens, direct gold mutation, or manual
  party-fleet mutation are used.
- Part choices come from War Sails' own
  `GetAvailableShipUpgradePieces(Town)` result through an isolated optional-mod
  reflection boundary. Culture, shipyard level, merchandise status, slot
  compatibility, stats, and native prices therefore remain authoritative.
- Trade mode reads each owner's real ship roster rather than the lists carried
  by `PortState`. The candidate is therefore attached to the port only for the
  lifetime of the screen. Cancelling detaches it; confirming uses War Sails'
  native trade action and cost model to transfer it to the player.
- A runtime contract audit hides or disables the entry when the required hull,
  upgrade slots, constructor, or Trade mode is unavailable. Failures are logged
  to `%LOCALAPPDATA%\Mount and Blade II Bannerlord\Logs\AgesOfCalradiaReligions.Shipwright.log`.

This first slice intentionally supports one vanilla hull. A later catalog UI
can select other vanilla hull IDs and feed the selected hull through the same
native commissioning boundary without changing save data or owning the port UI.

The feature is a separate assembly,
`AgesOfCalradiaReligions.Shipwright.dll`, inside the Religions module and does
not modify the protected
`AgesOfCalradia.dll` or `GUI\Prefabs\WorldCalendar\WorldCalendar.xml` artifacts.

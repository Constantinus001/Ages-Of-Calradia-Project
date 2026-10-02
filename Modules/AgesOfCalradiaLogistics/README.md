# Ages of Calradia Logistics

A standalone add-on module for Ages of Calradia. It is deliberately separate
while the logistics systems are developed and tested. It remains a sidecar
assembly when packaged inside AOC SYSTEMS L & R; it must not be merged into or
overwrite the protected political/calendar assembly.

## Current milestone

### Core Campaign Systems integration candidate

The optional ABI `AgesOfCalradiaLogistics.CampaignSystemsApi` v1 exposes existing
party reserves read-only. Core discovers it without a hard assembly dependency;
queries do not initialize reserves or consume stock. Unknown/no campaign is -1,
not zero. Existing standalone and Systems L & R identities/save keys are retained.
There is no workshop freight provider yet; Core explicitly reports transport as
unsupported rather than creating a second delivery path. Candidate verification
is under `Modules/AgesOfCalradiaCampaignSystems/Tests/Verify-Candidate.ps1`.

### Existing gameplay

- Adds the merchantable Supply inventory item.
- Reuses Bannerlord's built-in crate_a mesh (the same crate visual used by
  vanilla Stolen Goods) while keeping the vanilla item unchanged.
- Persists a versioned 0-100 reserve for each eligible lord, caravan, and player
  party while preserving the legacy `aoc_logistics_reserves` save key. Corrupt
  values are clamped and older saves gain tracking state without retroactive
  consumption.
- Operational Supply is one abstract resource covering ammunition, maintenance,
  fodder, medicine, tents, and camp consumables. Native food remains the sole
  authority for eating and starvation.
- Parties in the field consume 0.5-6 reserve per day based on healthy troop
  count (`healthy troops / 40`), with fractional demand carried forward rather
  than rounded away. Fortifications provision parties locally, so field reserve
  does not drain while they are inside a town or castle.
- A Supply crate contributes 20 reserve. New parties receive one emergency
  crate-equivalent rather than the former free reserve of 60.
- Town markets replenish Supply crates gradually, up to 3 per normal town and
  5 in prosperous towns. Production is weekly, prosperity-scaled, and disabled
  by siege, starvation, or very low security. At a town, use **Load supplies
  into baggage train** to consume carried crates and refill the player reserve;
  the menu reports projected field days remaining. A second quartermaster
  option buys directly from real town stock at the current market price toward
  the same ten-day target used by AI.
- AI parties use the same reserve. In a safe friendly town they buy from actual
  town stock at the current market price toward ten projected days and retain a
  5,000-denar operating floor. Player-clan parties never spend clan money
  automatically.
- Supply condition affects land travel after native modifiers and is based on
  projected field days, not an arbitrary reserve total: Supported (7+ days)
  has no penalty, Strained (3-7 days) is -5%, Critical (under 3 days) is -12%,
  and Empty is -20%. Empty reserve also prevents battlefield resupply.
- Calibrates neutral land travel to map speed 4.0 and caps campaign-map
  movement at 8.0. Native terrain, weather, cargo, herd, army, prisoner,
  wound, skill, and party-composition modifiers remain active.
- In player field battles, every non-bandit side with an eligible lord,
  caravan, or player party spawns a physical baggage train using the native
  cart prefabs and scattered-goods props. Two to six wagons and one to three
  ground supply piles scale with participating troops and reserve; the central
  wagon retains a 6m resupply radius. Native prefab/scene failures are logged
  and disable that side's logistics rather than failing the mission.
- After battle deployment, up to eight existing troops are detached into an AI
  guard formation and ordered to hold at their side's central wagon.
- Wagon visuals rotate through every native intact cart, cargo-heap, hay-cart,
  and olive-cart variant. Broken carts are reserved for future train-damage
  states.
- Every 3 seconds, agents within their own six-metre supply radius can receive up to three
  rounds for one reserve point. This includes the player; it stops immediately
  when the side's finite reserve is empty. The current milestone has no visible
  ground ring yet.
- Coalition battles debit a deterministic pool of every eligible participating
  party, ordered by party ID and spent round-robin, instead of charging
  whichever party happened to appear first.
- The player receives a one-time campaign notification on entering Strained,
  Critical, or Empty Supply states. This avoids modifying protected Gauntlet
  assets; a persistent HUD remains future work.
- An undefended enemy occupation of a train for 12 seconds captures it, disables
  its resupply, and burns 25% of the defending battle ledger's remaining reserve.

## Planned implementation order

1. Add a visible supply indicator, warnings, and a quartermaster auto-buy toggle
   for the player, following the proven ten-day provisioning pattern without
   copying Banner Kings' nine-category micromanagement.
2. Scale baggage visuals to force size and reserve, and cap ammunition transfer
   per agent/weapon so thrown weapons cannot exploit flat per-round costs.
3. Add AI-aware train raids/capture and idempotent post-battle accounting.
4. Add settlement/castle depots, siege demand, winter pressure, and low-supply
   campaign-objective scoring after telemetry validates the base loop.

## Design inspiration

Banner Kings demonstrates four useful patterns: daily composition-based demand,
a fixed days-of-provision target, real market purchases, and identical AI/player
rules. This module adopts those patterns in a deliberately smaller form. One
Supply good is retained because Bannerlord already owns food and because nine
separate item debts would add inventory work before this system has proven its
strategic value.

## Load order

Load after Native, SandBoxCore, Sandbox, and AgesOfCalradia.

## Build and verify

Run:

    dotnet msbuild .\AgesOfCalradiaLogistics.csproj /t:Rebuild /p:Configuration=Release
    .\Tests\Verify-SupplyItem.ps1

The compiled DLL is written to bin\Win64_Shipping_Client. For a manual game
test, copy this module folder to Bannerlord's Modules directory, excluding
intermediate build folders.

## Diagnostics

The module writes bounded diagnostics to:

    %LOCALAPPDATA%\Mount and Blade II Bannerlord\Logs\AgesOfCalradiaLogistics.log

When it exceeds 2 MB, the previous log is retained as
AgesOfCalradiaLogistics.log.previous. It records reserve loading, market
restocks, AI procurement, train spawn decisions, and 30-second battle
resupply summaries.

## Battle test

Start a new player-involved **field battle** after loading the module. Town and
siege missions intentionally do not create baggage trains.

If a battle crashes, retain the newest folder under
%PROGRAMDATA%\Mount and Blade II Bannerlord\crashes and the logistics log.
The train spawn diagnostics include deployment frame and native-prefab
checkpoints for diagnosis.

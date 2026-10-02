# Campaign Systems candidate installation

PRIVATE ACCEPTANCE CANDIDATE — NOT A PUBLIC NEXUS RELEASE.

This update requires the exact AOC installation recorded by its verification.json.
Its Core manifest preserves that installation's additional assemblies. Do not
install it over a different AOC download or distribute it as a standalone mod.
Bannerlord v1.4.8 is the tested native API target.

## Archives and order

- Player: Core companion/calendar fixes and Workshop Procurement inside Core.
  No diagnostics or Logistics module is included. Core registers the framework
  before procurement; no separate procurement launcher selection is needed.
- Logistics: optional Logistics v0.3.0 candidate. Requires Core, and must not be
  enabled alongside another distribution providing the same Logistics assembly.
- Diagnostics: opt-in observation module, not required for gameplay. Its manifest
  additionally requires Bannerlord.Harmony. Install only for planned diagnostics.

Each archive has a Modules directory. Do not install until the exact baseline has
been verified and the game is closed. Back up the existing affected module files
and a pre-upgrade save first. Merge into the game's Modules directory; never delete
your existing Core folder. Enable selected modules in dependency order.

No protected AgesOfCalradia.dll or WorldCalendar prefab is included or replaced.
Never remove Core registrations to work around a missing assembly error.
Disable the older Workshop Procurement candidate in the launcher before testing
this integrated version. Keep a pre-upgrade save and the backed-up old module;
do not delete it until migration is accepted. The assembly, behavior and save key
remain unchanged. Duplicate startup is rejected, not treated as a second service.

## Configuration

Configuration is optional. Copy Core's CampaignSystems.example.xml to
Documents/Mount and Blade II Bannerlord/Configs/AgesOfCalradia/CampaignSystems.xml.
Keep an existing configuration rather than overwriting it. Changes take effect
at campaign start and affect new orders; saved obligations retain their policy.
Invalid settings block new procurement orders but do not erase existing orders.

## Acceptance and rollback

Use a separate save slot: verify startup, existing-save loading, status commands,
save/reload and pending-order continuity. Commands: aoc.systems_status,
aoc.quest_times, aoc.logistics_status <party-id>. Record package hashes with results.
Do not replace a long-term save until this exact build is accepted.

To roll back, restore backed-up module files and load the pre-upgrade save.
Removing procurement from a save with outstanding orders is not certified safe.
Do not delete ledgers, reset campaign dates, or assume disabling a module refunds
reserved money and cargo. No automatic rollback of campaign state is provided.

## Scope

Logistics integration currently reads party reserves; it does not transport
workshop freight. Quest deadlines are observed, not recreated. Noble wealth,
six-month army growth and opening-war balancing are not part of this candidate.

# AOC SYSTEMS R & S

This runtime module combines Religions/population with hereditary Succession.
It loads after `AOC CORE` and deliberately loads Religions before Succession,
because Succession consumes the Religion assembly's public service.

The existing `AgesOfCalradiaReligions.dll` and
`AgesOfCalradiaSuccession.dll` assembly, namespace, Harmony, and save-key
identities are preserved. A third, independent
`AgesOfCalradiaReligions.Shipwright.dll` adds native War Sails Dromon
commissioning at port towns when NavalDLC v1.2.8 is enabled. War Sails remains
optional; a missing or incompatible naval contract hides the commissioning
entry without disabling religions or succession.

Version 1.0.2 creates the port screen through the native game-state manager and
temporarily exposes the candidate through the port's real ship roster, matching
the invariants used internally by War Sails Trade mode. The candidate is
detached on cancellation or launch failure.

Version 1.1.0 adds the ordered Fore, Aft, Bow, Hull, Side, Deck, Sail, Roof,
and Name builder. It uses the current port's native War Sails merchandise list,
then hands the configured Dromon to the 3D Trade-mode preview for final purchase.

Do not enable either legacy standalone module with this package. Back up and
test existing campaigns because Bannerlord can show a module-mismatch warning
when their recorded standalone IDs are replaced by `AgesOfCalradiaSystemsRS`.

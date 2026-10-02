# Campaign Systems offline candidate — 2026-09-20

## Scope and status

Core companion plus procurement, optional Logistics ABI and bounded diagnostics.
Not a replacement/rebuild of the protected Core renderer. No game launch, close,
save, speed change, capture arming or deployment. Full-Core release approval and
live acceptance are separate from this offline candidate gate.

Candidate run: `C:\Users\fpicc\AocRelease\framework-d40d77f39197`.
Snapshot commit: `f4c62323defb8a1c72f056199a724d191b089566`.
Archive SHA-256:
`3B985B9156C42B6E0A06435CCD0982E3157405E4C9643BE78F9E06DC5343CACB`.
The run completed with `Stage=offline_candidate_pass` and
`Security=no_detection_during_scan_and_hold`: package and archive Defender scans,
the full ten-minute hold, source/external/native/package/archive hash rechecks and
the final protected-baseline gate passed. `Deployed=false`. This is a bounded
scan result, not a guarantee about future detections or live behavior.

## Changes and evidence

- Supply capture now records Core assembly MVID, effective policy fingerprint,
  valid/rejected/missing/no-campaign status, native saved quest deadlines and
  remaining campaign days at start, daily sampling and terminal snapshot.
- Native Never is explicit, not a numeric deadline. Empty quest lists are a
  coverage gap. Deadline changes are reported for context, not declared bugs.
- `Analyze-EconomyAcceptance.py` includes framework/quest evidence alongside the
  existing four economy questions. 43 Python analyzer tests pass.
- Native adapter fixture uses actual QuestBase/QuestManager objects with isolated
  campaign/time inputs. Finite, Never and expired deadlines remain unchanged.
  This does not prove native quest creation/completion or live deadline scaling.
- Core/procurement/Logistics/diagnostics/calendar-companion Release builds pass
  with zero warnings. 84 procurement, 40 framework, 11 Core lifecycle assertions;
  native production, transfer rollback, optional ABI and protected regressions pass.
- Additional checks against byte-identical packaged diagnostics pass: 57 native
  transaction targets, actual delta/clamp/parent reconciliation, typed out-value
  preservation, exception propagation, controller boundaries and reload receipts.

## Packaging boundary correction

The checkout Core manifest is not interchangeable with the installed manifest.
It lacks installed PoliticalFillSeamFix, PoliticalRenderDiagnostics,
CoastSurfaceFix and PublishedBorderSubModule entries. Candidate packaging now
clones the installed manifest and adds only the framework entry. Full normalized
XML comparison after removing that entry protects existing registrations,
dependencies, XML data and ordering. Existing referenced DLLs must exist.

Negative package fixtures reject duplicate framework copies, changed Core
identity, removed published-border registration and missing Core dependency.
The first candidate `framework-bb3f37879b45` is explicitly blocked/superseded and
must not be installed. Its security hold was stopped, not the game.

The candidate is a delta against the hashed existing installation, not a complete
fresh-install distribution. It includes a single framework DLL under Core and
separate production/diagnostic modules. No protected renderer or prefab is in it.

## Offline performance sample

Exact corrected package, 11 warmed admission samples per size:

| Existing orders | Serialized characters | Median ms | Maximum ms |
| ---: | ---: | ---: | ---: |
| 0 | 60 | 0.006 | 0.031 |
| 500 | 208949 | 1.692 | 5.858 |
| 2000 | 836949 | 8.582 | 9.810 |
| 4000 | 1674949 | 17.186 | 19.233 |

All populated states round-trip; admission never adds the proposed order itself.
Synthetic single-input ledgers are not live frame-time or worst-case recipe proof.

Supplemental verification scripts added after the candidate snapshot do not alter
the package: `Verify-NativeQuestObservation.ps1`, `Verify-PackageRejection.ps1`
and `Measure-LedgerCapacity.ps1` run directly against its exact DLLs. The fixture
log-routing fix changes test tooling only. Packaged production sources remain the
committed snapshot; these later tests are additional evidence, not an unrecorded
rebuild. The framework DLL hash is
`4D4349EF6E9E65CF51DDAA7962CE21485BF773E16480BB0F2EE42E00B97B5A60`;
diagnostics DLL hash is
`8656BF36B0910F935535E9A581803E335D4DA129488E22052623A42FE51DB95B`.

## Fixture log provenance

Older SupplyCapture writer fixtures used the normal SoakLog sink for status
messages despite writing capture TSVs into temporary folders. Future fixtures now
redirect both sinks into their temporary directory. The normal AocSoakEvents.tsv
contains a `SUPPLY_CAPTURE_FAILURE` at `2026-09-20T18:04:11.3627565Z` caused by this
offline native-quest fixture before its time-unit initialization was fixed. It is
not campaign evidence. Existing log rows were not removed. Fixture completion
messages from this work likewise do not signify an actual game capture.

## Remaining acceptance limits

1. Exact-package Defender scan/hold and final input/hash rechecks: completed.
2. Full release-gate integration/approval is not replaced by this offline package.
3. In-game launcher ordering, third-party coexistence, overhead and economic
   outcomes require actual acceptance. Synthetic coverage is not live balance.
4. Logistics currently exposes read-only party reserves, not workshop transport.
   No physical freight hand-off or universal third-party authority is claimed.

Do not request another campaign run merely because an uncommon event was absent.
Use one bounded combined capture for all planned questions; retain NOT_EXERCISED
for absent quest/route/war branches and inspect existing evidence first.

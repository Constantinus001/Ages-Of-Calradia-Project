# Naval acceptance investigation — 28 September 2026

## Verdict

The reported `battle_policy_wallet_reconciliation_mismatch` is a confirmed
false positive in the new acceptance analyzer. One native-ineligible naval
patrol was incorrectly treated as entitled to a cash payment. This event does
not demonstrate lost money, duplicate money or an incorrect gameplay payout.
No game code, configuration, save or capture was changed during investigation.

## Evidence scope

Source: `C:/Users/fpicc/Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/AocFramework-current.tsv`.
Session: `7f1cb08aba0c49518d8ed0605ef61516`.
Candidate: `C:/Users/fpicc/AocRelease/framework-2f2d21230697`.

- 16.0002966979 campaign days; 734.9108969 wall seconds, including pauses.
- Normal terminal reason: `acceptance_window_complete_coverage_not_certified`.
- 2,856,916 records; 1,043,327,280 bytes; zero discarded records.
- Writer time lower bound: 13.3348036 seconds (1.81% of elapsed time), not total
  observer overhead. Observed mean: 45.93 seconds per campaign day, not a
  calibrated unpaused speed measurement.
- Policy startup receipt enabled hull basis 0.01; no late rejection receipt
  was found in this capture.

## Exact failure chain

Map event 662, map party 1592, `naval_patrol_party_1_party_418`, reward ID
`539e63e151b246c78bf53a3277344e16`, at 18:38:19.5354374 UTC:

| Record sequence | Evidence |
| --- | --- |
| 2404277 | Policy allocation: nativeTotal=17663, effectiveTotal=17663, applied=False |
| 2404308 | No recipient hero or clan; battlePaymentEligible=False; opening trade wallet=0 |
| 2404309 | Allocated gain=17663, loss=0; this is an allocation, not cash |
| 2404310 | Remaining native gain and loss both reset to zero |
| 2404311 | Native original ran; closing wallet=0; no associated wallet mutation |

Read-only inspection of the installed Bannerlord `MapEventParty.CommitGoldChanges`
IL confirms it pays the leader hero when one exists, otherwise adjusts a mobile
party's trade wallet only when `IsPartyTradeActive` is true. It then resets
PlunderedGold and GoldLost. This patrol was ineligible, so zero cash is expected.

`Analyze-NavalAcceptance.py` joins the policy allocation correctly and confirms
the commit input, but then unconditionally expects allocated gain minus loss.
It does not consult the captured `battlePaymentEligible` flag. That incorrectly
turns this valid zero-payment branch into a 17,663-gold mismatch.

An independent replay found 30 policy-linked battle commits: 29 paying commits
totaling 29,939 gold, all matching their canonical wallet mutations, plus this
one expected zero-payment commit. The unconditional allocation sum is 47,602;
its entire 17,663 difference is explained by the ineligible patrol.

Cross-check: existing `Analyze-RewardAccounting.py` explicitly handles
eligibility. On this same capture it reports no problems, zero wallet residual
for this reward, zero eligibility-adjusted difference, and the correct reset.
It independently flags player penalty coverage as missing.

## Other observed results and limitations

The existing acceptance report reconciles 17 sales with transfers, 29 battle
payments and six recovery/cleanup operations. Its arithmetic checks found no
additional issue. Mixed player/AI battles were not reconciled. There were zero
player naval reward scopes, so live player selling-penalty behavior remains
unverified. Passing AI receipts does not establish player behavior.

Recorded workshop/procurement counters: 12,135 workshop attempts, 8,739 successes,
3,396 unsuccessful attempts; 109 procurement commitments, 94 arrivals and 430
consumption receipts. These are activity counts, not a full supply-chain or
profitability reconciliation. Unsuccessful attempts are not automatically
shortages, and the difference between commitments and arrivals is not proof of
lost cargo. This investigation does not explain the historical 28.19-million
wealth increase or certify economy-wide balance.

## Recommended correction, in order

1. Correct the offline acceptance analyzer to distinguish native payment
   eligibility from policy eligibility. Require explicit eligibility evidence;
   missing eligibility must remain a coverage gap, not default to zero or paid.
2. Report ineligible zero-effect commits separately from actual payments.
   Require original execution, zero wallet effect and cleared allocations.
   Nonzero cash in an ineligible scope must still fail. Preserve existing
   policy-to-commit and canonical-wallet checks for eligible recipients.
3. Add regression cases for this patrol, eligible hero and trade recipients,
   missing eligibility, unexpected cash, uncleared allocations and original
   skips. Keep native clamping/negative-input semantics explicit rather than
   blindly assuming every allocated value becomes cash.
4. Replay this exact closed capture after the reporting correction. No new game
   run or deployment is necessary to resolve this false positive.
5. Keep mixed-player and player-penalty live coverage open. If release
   certification requires it, use a targeted scenario rather than another
   unchanged 16-day AI soak. Do not change the 0.01 factor from this flag.

The investigation above was read-only. The subsequently authorized reporting
correction is recorded below. Overall acceptance is not yet certified.

## Implemented correction and replay

Affected boundary: offline diagnostic interpretation only. No Harmony target,
deployed DLL, policy factor, save or game control changed. Existing capture
fields supply the native eligibility and reset evidence; older logs missing
those fields now explicitly report coverage gaps.

The analyzer now requires explicit eligibility, successful native execution,
canonical wallet identity and zero remaining allocations. An ineligible commit
must have zero wallet delta and no nonzero canonical wallet mutation, including
offsetting credit/debit activity. It is counted separately from actual payments.
Eligible commits retain policy-to-input and wallet reconciliation. Negative
eligible inputs remain a native-semantics review gap instead of guessing a clamp.
Mixed-player acceptance still requires actual joined player/AI payments.

Verification:

- 194 Python diagnostic tests passed (nine added eligibility regression tests).
- Diagnostics Release build: zero warnings and errors, isolated output only.
- Protected political renderer and WorldCalendar baseline check passed.
- Same completed session replay: `COVERAGE_GAPS`, no reported issues;
  29 battle payments, one ineligible zero-effect commit, 17 sales/transfers,
  six recovery/cleanup operations and zero mixed-player battles.
- Missing coverage in this report: `mixed_player_battle_not_reconciled`.
  The independent reward investigation also leaves player selling penalties
  unverified because no player naval scopes occurred.

No new capture, restart or deployment was required. This corrects the false
positive; it does not certify unobserved player behavior or economy-wide tuning.

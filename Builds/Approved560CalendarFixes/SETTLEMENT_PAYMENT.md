# Settlement payment conservation

## Evidence and boundary

Capture `763434d247474f25a264015c3f75594f` completed 15.000236 campaign
days on 2026-09-20, with no detected scope/stock/wallet reconciliation errors.
Forty gross transfer receipts nevertheless show 12,185 more gold credited than
debited. They share the PartiesBuyHorseCampaignBehavior.OnSettlementEntered ->
SellItemsAction -> ApplyForSettlementToCharacter path. Despite the behavior's
name, it sells non-food loot as well as horses; this is not proof all 40 items
were horses. Zero unexplained wallet residual means mutations were observed,
not that the mutations conserved money.

The installed 1.4.8 native implementation encodes settlement-to-character
payments by calling ApplyInternal with the character as giver and a negative
amount. Its giver-side Min does not bound the actual town payer. Settlement
ChangeGold floors the town at zero while the character receives the full quote.
The native fixture reproduces the captured 858 quote / 148 cash example:
710 gold is created without the public wrapper patch.

## Focused correction

`SettlementPaymentConservationFix` is a last-priority prefix on the exact public
signature `(Settlement, Hero, int, bool)`. It bounds only positive payments
with real settlement and hero endpoints to the settlement's available cash.
Zero and signed refunds keep native handling. The original method and its
event dispatch execute normally with the adjusted amount. There is no save
state, debt, escrow, Core modification or broad income/resource multiplier.

Compatibility: other prefixes changing the amount after this prefix or patches
to the underlying payment methods need independent review. Missing signatures
fail sidecar preflight through the existing logged rollback boundary. Tests
run against the installed native assembly. The patch preserves native partial
payment semantics: it does NOT roll back or limit already-transferred goods.
Changing sale quantities/escrow would be a separate behavioral change, not
silently included in this money-conservation correction. Recipient integer
saturation and arbitrary negative-input edge cases are not newly redefined.

## Verification and release status

Release build succeeded with warnings treated as errors, into
`C:\Users\fpicc\AocRelease\settlement-payment-check`.
`Verify-SettlementPayment.ps1` reproduces the native defect, then checks eight
patched native transfers including empty/exact/abundant town cash, zero,
refunds and insufficient refund-payer cash. It checks both balances and native
event amount/count. Campaign event delivery is stubbed; wallet mutation and
transfer code are native. No live game is opened or changed by the fixture.
The test is now included in the scoped release gate.

This candidate is NOT deployed or security-release-certified. The installed
previous package remains unchanged. Deployment still requires the scoped
release build/test/Defender scan and hold, and a closed game. No new campaign
run is requested to finish this captured-data investigation.

## Other three priorities, same capture

The new read-only Analyze-WorkshopPayments.py joins sequential cycle boundaries,
accepted output/input quotes, and actual workshop/town cash deltas. It rejects
ambiguous nesting and unmatched cycles. 5,460 successful capital-affecting
batches match approved outputs, paid outputs, input costs, and paired town
cash exactly. No further workshop-payment patch is justified in this capture.
This coverage does not certify unobserved cases or all future runs.

Pravend (town_V3) has zero opening/closing wool and no recorded wool stock
movements. Its wool recipes fail all 20 observed input checks across almost
14 days. Charas (town_V7) receives eight wool, exports two and consumes six in
workshops, with zero stock residual. Its cow/sheep recipes fail 15 checks each,
with zero opening/closing inventory and no recorded stock flows. This identifies
real local input-access failures, not missing inventory or proof of broken
routing. A generic resource injection or route override is not warranted.

Observed production margins before expenses are positive in aggregate for wine
(7,470), smithy (69,685) and linen (52,804). These are actual paid outputs minus
paid inputs, NOT net profitability. Other capital changes include operating
expenses and owner distributions; eight nonproducing wallet identities are not
mapped by the cycle join. Do not mistake owner payouts for operating losses or
aggregate gains for every workshop being healthy. No speculative rebalance was
implemented. Full evidence is in output/workshop-payments-763434.json and the
earlier output/economy-capture-763434-analysis.json.

## Deployment follow-up

The settlement-payment correction was deployed successfully by scoped release
`C:\Users\fpicc\AocRelease\4cad613bc5b3\verification.json` after all checks,
Defender scan F68CE914-55C1-4381-A6E6-C34429A5776F and the ten-minute clean hold.
Installed calendar-fixes SHA-256:
`6106E1A44CE6D35D61F41ED25DE30DF3DED94E5DEB84C4B57C5E5D0753F6F348`.
Protected baseline passed before/after copying. Earlier not-deployed statements
above describe the pre-release candidate. No game/save/settings were changed.
The next focused correction is documented separately in AI_SALE_QUANTITY.md.

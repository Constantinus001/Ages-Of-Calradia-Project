"""Join observed production/expenses to recognized inventory basis, not dispatch cash."""
import collections
import math


def wallet_gaps(evidence):
    """Missing receipts and window mismatches are not zero expenses or zero cash."""
    if not evidence:
        return ['workshop_wallet_evidence_missing']
    gaps = []
    for field, expected in (('baseline_day', 'capture_start'), ('final_check_day', 'capture_end')):
        a, b = evidence.get(field), evidence.get(expected)
        if a is None or b is None or not math.isfinite(a) or not math.isfinite(b) or abs(a-b) > 1e-8:
            gaps.append('workshop_wallet_window_incomplete')
    for field in ('final_residual', 'maximum_absolute_residual'):
        value = evidence.get(field)
        if value is None or not math.isfinite(value): gaps.append('workshop_wallet_check_missing')
        elif abs(value) > 0.0001: gaps.append('workshop_wallet_unexplained_change')
    observed = evidence.get('executed_boundaries', {})
    for category in ('operating_expense', 'owner_payout'):
        if observed.get(category, 0) <= 0: gaps.append('workshop_'+category+'_coverage_missing')
    return sorted(set(gaps))


def assess(workshops, accrual, transactions):
    results, gaps = {}, set()
    if not workshops:
        return dict(by_workshop={},coverage_gaps=['workshop_evidence_missing'])
    keys = workshops.get('procurement_shop_keys',{})
    costs, cash = collections.Counter(), collections.Counter()
    for receipt in accrual.get('accounting_receipts',[]):
        costs[receipt['shop']] += receipt['recognized_cost_or_loss']
        cash[receipt['shop']] += receipt['cash_delta']
    if accrual.get('problems') or accrual.get('coverage_gaps'):
        gaps.add('accrual_evidence_incomplete')
    if transactions.get('problems') or transactions.get('coverage_gaps'):
        gaps.add('transaction_evidence_incomplete')
    if workshops.get('problems') or workshops.get('batch_discrepancies') or workshops.get('unmapped_nonproduction_wallets'):
        gaps.add('workshop_payment_evidence_incomplete')
    common_gaps = set(gaps)
    for owner, values in workshops.get('by_workshop',{}).items():
        local_gaps = sorted(common_gaps | set(wallet_gaps(workshops.get('workshop_wallet_evidence', {}).get(owner))))
        gaps.update(local_gaps)
        key = keys.get(owner)
        if key is None:
            gaps.add('workshop_procurement_identity_missing')
            results[owner] = dict(status='IDENTITY_MISSING'); continue
        # Owner payouts are equity distributions. Dispatch is inventory purchase.
        # Production quote estimates are replaced by committed allocated basis.
        operating = (values.get('paid_outputs',0)-values.get('paid_inputs',0)
                     -costs[key]+values.get('operating_expense_capital_delta',0)
                     +values.get('daily_tick_expense_inferred_native_capital_delta',0))
        unknown = values.get('other_capital_delta',0)-cash[key]
        if unknown:
            gaps.add('unclassified_capital_change')
            local_gaps.append('unclassified_capital_change')
        results[owner] = dict(procurement_key=key,
            status='INCOMPLETE_CASH_EVIDENCE' if local_gaps or unknown else 'OBSERVED_RECONCILED_NOT_BALANCE_CERTIFIED',
            coverage_gaps=local_gaps,
            reconciled_operating_result=None if local_gaps else operating,
            wallet_evidence=workshops.get('workshop_wallet_evidence', {}).get(owner),
            observed_operating_result_with_native_daily_expense_inference=operating,
            recognized_procurement_cost_or_loss=costs[key],
            procurement_cash_not_additional_expense=cash[key],
            owner_payout_capital_delta_not_expense=values.get('owner_payout_capital_delta',0),
            unclassified_capital_delta=unknown)
    if set(costs)-set(keys.values()): gaps.add('procurement_workshop_not_in_payment_report')
    if not results: gaps.add('workshops_not_exercised')
    if workshops.get('problems') or workshops.get('batch_discrepancies') or workshops.get('unmapped_nonproduction_wallets'):
        gaps.add('workshop_payment_evidence_incomplete')
    return dict(by_workshop=results,coverage_gaps=sorted(gaps),
                limits='Observed operating result, not certified total profit. Daily-tick expense attribution is inferred. Unknown capital and incomplete cash/cargo evidence require review.')

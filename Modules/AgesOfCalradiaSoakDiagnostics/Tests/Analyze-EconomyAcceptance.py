"""One read-only report for the four open economy questions; never certifies balance."""
import importlib.util
import json
from pathlib import Path
import sys


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def assess(base, broad, cases, workshops=None):
    blockers = list(base.get('problems', [])) + list(broad.get('problems', []))
    if base.get('integrity') != 'CLOSED_NO_DETECTED_INTEGRITY_ERRORS':
        blockers.append('Closed, integrity-checked capture required')
    if cases.get('schema') != 'supply_v7':
        blockers.append('v7 canonical-wallet capture required; older runs remain historical evidence')
    receipts = broad.get('transfer_receipts_not_additive_income', {})
    receipt_issues = list(receipts.get('problems', []))
    uncertain = {key: row.get('interpretation') for key, row in receipts.get('by_id', {}).items()
                 if row.get('interpretation') != 'balanced_gross_endpoints_not_additive_income'}
    wallet_residuals = broad.get('wallet_unexplained_final_residuals', {})
    workshop_checked = (workshops is not None and workshops.get('closed')
                        and not workshops.get('problems') and not blockers
                        and workshops.get('matched_capital_affecting_successful_batches', 0) > 0)
    payment_status = ('CAPTURED_BATCHES_MATCHED' if workshop_checked and not workshops['batch_discrepancies']
                      else 'CAPTURED_BATCH_DISCREPANCIES' if workshop_checked else 'LIVE_BATCH_RECONCILIATION_REQUIRED')

    def missing(*groups):
        result = []
        for group in groups:
            coverage = broad.get('coverage', {}).get(group)
            result.extend(coverage.get('not_exercised', []) if coverage else [group + ':coverage_missing'])
        return result

    return {
        'capture_blockers': blockers,
        'measured_seconds_per_day_including_pauses': base.get('measured_seconds_per_day_including_pauses'),
        'campaign_days': base.get('campaign_days'),
        'stop_reason': base.get('stop_reason'),
        '1_clan_hero_gold': {
            'status': 'ATTRIBUTION_REVIEW_REQUIRED',
            'missing_evidence': missing('cash'),
            'wallet_residuals': wallet_residuals,
            'receipt_problems': receipt_issues,
            'receipts_requiring_caller_review': uncertain,
            'observed_caller_deltas': broad.get('cash_caller_chains', {}),
            'decision': 'Do not reduce income until unexplained changes are attributed; external finance is not automatically inflation.'},
        '2_workshop_payments': {
            'status': payment_status,
            'batch_evidence': workshops,
            'missing_evidence': missing('workshops', 'cash'),
            'decision': 'Batch evidence compares approvals, paid outputs, town debits and input costs. A pass applies to matched captured batches, not unobserved cases or future runs.'},
        '3_input_shortages': {
            'status': 'TIME_AND_DELIVERY_REVIEW_REQUIRED',
            'missing_evidence': missing('production_shipments', 'purchasing', 'routing', 'selling', 'workshops'),
            'stock_residuals': broad.get('stock_unexplained_residuals', {}),
            'stock_change_causes': broad.get('stock_change_causes', {}),
            'workshop_input_events_by_recipe_not_additive_market_flows': broad.get('workshop_input_events_by_recipe_not_additive_market_flows', {}),
            'competing_demand_limits': broad.get('competing_demand_limits'),
            'unaligned_stock_windows': broad.get('stock_windows_unaligned', []),
            'charas_wool_route_evidence': cases.get('charas', {}),
            'decision': 'Correlate failed inputs with deliveries and subsequent attempts; zero stock or repeated failed gates alone are not sustained shortage proof.'},
        '4_workshop_profitability': {
            'status': 'REALIZED_MARGIN_AND_EXPENSE_REVIEW_REQUIRED',
            'missing_evidence': missing('workshops', 'cash', 'consumption_prices'),
            'recipe_outcomes': broad.get('recipe_cycle_outcomes', {}),
            'recipes_without_attempts': broad.get('recipes_without_attempts', []),
            'sheep_quote_evidence_not_realized_profit': cases.get('sheep_totals', {}),
            'decision': 'Compare actual inputs, paid outputs, wages and capital flows by wine/smithy/linen/sheep workshop. Capital growth and rejected quote margins are not realized profit.'},
        'verdict': 'REVIEW_PLAN_WITH_EVIDENCE_NOT_FOUR_FIXES_CERTIFIED',
        'next_step': 'Analyze this same capture before requesting another run. Missing rare events do not automatically require a rerun.'}


def analyze(path):
    base = load('Analyze-SupplyCapture').analyze(path)
    broad = load('Analyze-BroadSupplyCapture').analyze(path)
    cases = load('Analyze-FourEconomyCases').analyze(str(path))
    workshops = load('Analyze-WorkshopPayments').analyze(str(path))
    result = assess(base, broad, cases, workshops)
    result['core_framework_and_quests'] = load('Analyze-CampaignSystems').analyze(path)
    return result


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2, allow_nan=False))

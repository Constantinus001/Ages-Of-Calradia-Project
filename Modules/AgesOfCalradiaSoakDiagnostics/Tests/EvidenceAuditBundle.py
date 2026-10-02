"""Run existing independent audits and keep every result/error inspectable.

Only a stable, closed, single-session capture is accepted. Never merges sessions
or re-labels analyzer completion as a passing economy. No game interaction.
"""
import importlib.util
import hashlib
from pathlib import Path
import time


AUDITS = {
    'capture_integrity_and_shipments': 'Analyze-SupplyCapture',
    'stock_and_wallet_reconciliation': 'Analyze-BroadSupplyCapture',
    'livestock_wool_and_town_cash': 'Analyze-FourEconomyCases',
    'workshop_payments_and_profitability': 'Analyze-WorkshopPayments',
    'procurement_transactions': 'Analyze-ProcurementTransactions',
    'procurement_cost_basis': 'Analyze-ProcurementAccrual',
    'reward_wallet_attribution': 'Analyze-RewardAccounting',
    'planning_and_actual_outcomes': 'Analyze-AcceptanceDetails',
    'naval_rewards_and_cleanup': 'Analyze-NavalAcceptance',
    'naval_policy_and_player_sales': 'Analyze-NavalPolicy',
    'battle_allocations': 'Analyze-BattleAllocations',
    'quest_and_framework_state': 'Analyze-CampaignSystems',
    'optional_and_rare_lifecycle_branches': 'Analyze-LifecycleCoverage',
}


def audit(path, loader=None, expected_sha256=None):
    def load(name):
        spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        return module.analyze

    loader = loader or load
    path = Path(path)
    initial = path.stat()
    if expected_sha256 is not None:
        digest = hashlib.sha256()
        with path.open('rb') as source:
            for block in iter(lambda: source.read(1024 * 1024), b''): digest.update(block)
        if digest.hexdigest() != expected_sha256:
            raise ValueError('Capture differs from triage evidence; refusing mixed-source investigation')
    results = {}
    for area, name in AUDITS.items():
        started = time.monotonic()
        try:
            evidence = loader(name)(str(path))
            results[area] = dict(status='analysis_completed_not_a_pass_verdict', analyzer=name,
                                 seconds=time.monotonic() - started, evidence=evidence)
        except Exception as error:
            # Optional analyzer/file boundary: preserve the failure, never substitute
            # zero issues or abort the remaining independent investigations.
            results[area] = dict(status='analysis_failed', analyzer=name,
                                 error=type(error).__name__ + ': ' + str(error), seconds=time.monotonic() - started)
    final = path.stat()
    if (initial.st_size, initial.st_mtime_ns) != (final.st_size, final.st_mtime_ns):
        raise ValueError('Capture changed during independent audits; refusing mixed evidence')
    return results


def summarize(evidence):
    """Small index, with exact counts and explicit truncation of examples."""
    summary = {}
    for key, value in evidence.items():
        if isinstance(value, (str, int, float, bool)) or value is None:
            summary[key] = value
        elif isinstance(value, (list, dict)):
            summary[key] = {'entries': len(value)}
            if key in ('problems', 'coverage_gaps', 'limits', 'next_actions'):
                summary[key]['examples'] = list(value)[:12]
                summary[key]['examples_omitted'] = max(0, len(value) - 12)
    return summary

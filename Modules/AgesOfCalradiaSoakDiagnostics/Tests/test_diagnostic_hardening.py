import unittest
import test_causal_evidence as base
from EvidenceAcceptance import checklist
from EvidenceCashPurpose import resolve


class HardeningTests(unittest.TestCase):
    def rows(self, rows, close=True):
        return base.CausalEvidenceTests.run_rows(self, rows, close)

    def test_missing_observer_is_detected_but_not_called_inflation(self):
        result = self.rows([('OBSERVER_COVERAGE', 'w', 'outer_boundary_recovered_activity', -14, -7,
                            'recoveredNet=-7')])
        finding = result['findings'][0]
        self.assertEqual(finding['issue'], 'observer_missed_nested_activity')
        self.assertIn('not proven', finding['false_alarm_checks'][0])
        self.assertEqual(checklist(result)['scenarios']['nested_wallet_observer_coverage']['status'], 'failed')

    def test_no_activity_is_not_broken_or_passed_execution(self):
        result = self.rows([('OBSERVER_COVERAGE', 'w', 'no_net_activity', 0, 0, '')])
        self.assertEqual(result['findings'], [])
        self.assertEqual(checklist(result)['scenarios']['nested_wallet_observer_coverage']['status'], 'not_exercised')

    def test_reconciled_activity_passes_only_narrow_check(self):
        result = self.rows([('OBSERVER_COVERAGE', 'w', 'nested_activity_reconciled', -7, -7, '')])
        checks = checklist(result)
        self.assertEqual(checks['scenarios']['nested_wallet_observer_coverage']['status'], 'passed')
        self.assertEqual(checks['status'], 'coverage_gaps')

    def test_money_purposes_require_executed_matching_scope(self):
        for purpose in ('native_kingdom_budget_grant', 'native_daily_finance_settlement',
                        'tribute_wallet_settlement', 'kingdom_budget_distribution', 'native_party_wage_payment', 'native_party_funding', 'market_trade'):
            for ran in ('True', 'False'):
                rows = [('CASH_PURPOSE_BEGIN', 'cash_boundary', purpose, 0, 0, 'operation=p; parentOperation=none'),
                        ('WALLET_CHANGE', 'w', 'Gold', 0, 12, 'cashPurposeOperation=p'),
                        ('CASH_PURPOSE_END', 'cash_boundary', purpose, 0, 0, 'operation=p; parentOperation=none; originalRan=' + ran + '; error=none')]
                result = self.rows(rows)
                self.assertEqual(checklist(result)['scenarios']['money_source/'+purpose]['status'], 'passed' if ran == 'True' else 'not_exercised')

    def test_generic_transfer_inherits_exact_grant_not_guessed_caller(self):
        outcomes = {'inner': dict(purpose='gold_transfer_unclassified', valid=True, parent='grant'),
                    'grant': dict(purpose='native_kingdom_budget_grant', valid=True, parent='none')}
        self.assertEqual(resolve('inner', outcomes), 'native_kingdom_budget_grant')
        outcomes['grant']['valid'] = False
        self.assertEqual(resolve('inner', outcomes), 'skipped_or_failed_boundary_unclassified')

    def test_wage_assessment_not_added_as_money(self):
        result = self.rows([('WAGE_ASSESSMENT', 'party', 'native_return_not_cash_payment', 100, 50, 'applyWithdrawals=True')])
        self.assertEqual(result['diagnostic_health']['wallet_purposes'], [])

    def test_incident_budget_and_io_failure_are_explicit(self):
        for state in ('failed', 'storage_or_issue_budget_exhausted', 'ready'):
            result = self.rows([('DIAGNOSTIC_COST', 'capture', 'cumulative_lower_bound', 0, 0,
                'bytes=100; incidentRecorder={"status":"'+state+'","incidents":1}')])
            self.assertEqual(checklist(result)['scenarios']['incident_retention']['status'],
                             'not_exercised' if state == 'ready' else 'failed')

    def test_completion_or_absent_audit_is_not_pass(self):
        result = self.rows([])
        self.assertEqual(checklist(result)['scenarios']['procurement_transactions']['status'], 'not_exercised')
        result['independent_audits'] = {'procurement_transactions': dict(status='analysis_completed_not_a_pass_verdict', evidence={})}
        self.assertEqual(checklist(result)['scenarios']['procurement_transactions']['status'], 'unsupported')

    def test_independent_contract_requires_activity_no_errors_no_gaps(self):
        result = self.rows([])
        evidence = dict(transactions={'t': {}}, problems=[], coverage_gaps=[])
        result['independent_audits'] = {'procurement_transactions': dict(status='analysis_completed_not_a_pass_verdict',
            analyzer='Analyze-ProcurementTransactions', evidence=evidence)}
        self.assertEqual(checklist(result)['scenarios']['procurement_transactions']['status'], 'passed')
        evidence['coverage_gaps'] = ['no opening balance']
        self.assertEqual(checklist(result)['scenarios']['procurement_transactions']['status'], 'not_exercised')
        evidence['problems'] = ['wrong amount']
        self.assertEqual(checklist(result)['scenarios']['procurement_transactions']['status'], 'failed')

    def test_partial_active_capture_not_corruption_or_certified(self):
        result = self.rows([], close=False)
        self.assertEqual(checklist(result)['scenarios']['capture_integrity']['status'], 'not_exercised')

    def test_discarded_accounting_cannot_pass_integrity(self):
        result = self.rows([('DIAGNOSTIC_COST', 'capture', 'cumulative_lower_bound', 0, 0, 'discardedRows=1')])
        self.assertEqual(checklist(result)['scenarios']['capture_integrity']['status'], 'failed')

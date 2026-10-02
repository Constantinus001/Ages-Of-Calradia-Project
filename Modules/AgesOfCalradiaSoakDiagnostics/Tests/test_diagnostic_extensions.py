"""Fault replay without Bannerlord: bounded observations never invent causality."""
import unittest
import test_causal_evidence as base
from EvidenceHealth import Health
from EvidenceCashPurpose import resolve


class DiagnosticExtensionTests(unittest.TestCase):
    def run_rows(self, rows, close=True, tail=b''):
        return base.CausalEvidenceTests.run_rows(self, rows, close, tail)

    def test_missing_dispatch_is_opening_ledger_gap_not_loss(self):
        result = self.run_rows([('PROCUREMENT_MOVEMENT', 'c', 'consumption', 0, 0, 'order=o; receipt=r')])
        order = result['timelines']['orders']['o']
        self.assertEqual(order['first_missing_or_inconsistent_step'], 'dispatch_not_observed_check_opening_ledger')
        self.assertNotIn('lost', order['review_gaps'])

    def test_duplicate_receipt_not_double_counted(self):
        row = ('PROCUREMENT_MOVEMENT', 'c', 'dispatch', 10, 0, 'order=o; receipt=r')
        result = self.run_rows([row, row])['timelines']
        self.assertEqual(result['duplicate_receipts'], 1)
        self.assertEqual(result['orders']['o']['stages']['movement:dispatch']['count'], 1)

    def test_multicommodity_distinct_receipts_are_not_duplicates(self):
        result = self.run_rows([('PROCUREMENT_MOVEMENT', 'c', 'arrival', 0, 0, 'order=o; receipt=' + r) for r in ('r1', 'r2')])
        self.assertEqual(result['timelines']['duplicate_receipts'], 0)

    def test_open_transaction_pending_closed_transaction_reviewed(self):
        row = ('PROCUREMENT_TRANSFER', 's', 'begin', 0, 0, 'order=o; transaction=t; stage=dispatch')
        for closed in (False, True):
            result = self.run_rows([row], closed)['timelines']['orders']['o']
            self.assertEqual(bool(result['review_gaps']), closed)

    def test_rollback_is_not_failed_transaction_or_sale(self):
        result = self.run_rows([('PROCUREMENT_TRANSFER', 's', outcome, 0, 0, 'order=o; transaction=t; stage=dispatch')
                                for outcome in ('begin', 'rolled_back')])
        self.assertEqual(result['timelines']['orders']['o']['review_gaps'], [])

    def test_plan_join_requires_id(self):
        rows = [('PROCUREMENT_PLAN', 's', 'offer', 0, 0, 'plan=p; selectedSource=town'),
                ('PROCUREMENT_PLAN', 's', 'dispatch_link', 0, 0, 'plan=p; order=o')]
        result = self.run_rows(rows)['timelines']
        self.assertIn('selection', result['orders']['o'])
        self.assertEqual(result['unlinked_plan_counts']['offer'], 0)
        result = self.run_rows([rows[0], ('PROCUREMENT_PLAN', 's', 'dispatch_link', 0, 0, 'plan=other; order=o')])
        self.assertNotIn('selection', result['timelines']['orders']['o'])

    def test_residual_lifecycle_preserves_unknown_cause(self):
        observations = [(10, 11), (10, 11), (11, 11), (11, 12)]
        expected = ['new', 'persistent_across_samples', 'no_longer_observed_not_explained', 'recurring']
        for end, state in enumerate(expected, 1):
            report = self.run_rows([('WALLET_CHECK', 'w', 'Gold', before, after, '') for before, after in observations[:end]])
            lifecycle = report['diagnostic_health']['residual_lifecycle']['WALLET_CHECK:w:Gold']
            self.assertEqual(lifecycle['state'], state)
            self.assertEqual(lifecycle['cause'], 'unknown')

    def test_private_stock_is_not_all_usable(self):
        result = self.run_rows([('WORKSHOP_INPUT_WITNESS', 'shop', 'wool', 2, 0,
            'privateStock=20; privateState=in_transit; eligiblePrivate=0; inTransitPrivate=20; blockedPrivate=0; reservedPrivate=0; unknownPrivate=0; nativeAccepted=False')])
        witness = result['diagnostic_health']['latest_input_evidence']['shop:wool']
        self.assertEqual(witness['eligible_this_attempt'], 0)
        self.assertEqual(witness['in_transit'], 20)
        self.assertEqual(witness['verdict'], 'attempt_evidence_not_sustained_shortage')

    def test_legacy_stock_eligibility_is_unknown_not_zero(self):
        result = self.run_rows([('WORKSHOP_INPUT_WITNESS', 's', 'wool', 2, 0, 'privateStock=20')])
        self.assertIsNone(result['diagnostic_health']['latest_input_evidence']['s:wool']['eligible_this_attempt'])

    def test_cash_context_is_not_profit_and_stack_is_not_grant(self):
        result = self.run_rows([
            ('WALLET_CHANGE', 'w', 'Gold', 100, 90, 'cashCategory=owner_payout'),
            ('WALLET_CHANGE', 'w', 'Gold', 90, 100, 'source=GiveGold; giverHero=none; callers=GrantMoney'),
            ('WORKSHOP_CASH_BOUNDARY', 'w', 'owner_payout', 100, 90, '')])
        purposes = {x['purpose']: x for x in result['diagnostic_health']['wallet_purposes']}
        self.assertEqual(purposes['owner_payout']['net'], -10)
        self.assertEqual(purposes['unexplained']['net'], 10)
        self.assertEqual(len(purposes), 2)

    def test_unknown_has_identity_evidence_and_does_not_blame_nearby_transaction(self):
        result = self.run_rows([
            ('WALLET_BASELINE', 'w', 'Gold', 10, 10, 'stringId=hero_1'),
            ('PROCUREMENT_TRANSFER', 's', 'committed', 0, 0, 'transaction=t; order=o; stage=dispatch'),
            ('WALLET_CHANGE', 'w', 'Gold', 10, 20, 'source=set_Gold; procurementTransfer=t'),
            ('WALLET_CHECK', 'w', 'Gold', 20, 25, '')])
        case = result['unknown_investigations']['cases']['wallet_residual:w']
        self.assertEqual(case['status'], 'unresolved')
        self.assertEqual(case['identity_evidence']['identity']['stringId'], 'hero_1')
        self.assertEqual(case['linked_transactions'][0]['order'], 'o')
        self.assertTrue(all(x['confidence'] == 'hypothesis_not_proven' for x in case['candidates']))

    def test_old_capture_cannot_claim_new_hooks_executed(self):
        result = self.run_rows([('FEATURE_COVERAGE', 'logistics_lifecycle', 'present', 0, 0, '')])
        self.assertEqual(result['diagnostic_health']['watchdog']['missing_execution']['logistics_lifecycle'], ['LOGISTICS_BEGIN', 'LOGISTICS_END'])

    def test_writer_cost_not_added_to_observer_cost(self):
        result = self.run_rows([('DIAGNOSTIC_COST', 'c', 'cost', 0, 5,
            'bytes=1000; wallSeconds=100; discardedRows=2; measuredObserverCosts={"q":{"exclusiveSeconds":3}}')])
        cost = result['diagnostic_health']['watchdog']['cost']
        self.assertEqual(cost['writer_fraction_lower_bound'], 0.05)
        self.assertEqual(cost['discarded_rows'], 2)
        self.assertNotIn('total_overhead', cost)

    def test_growth_and_weighted_day_rate(self):
        health = Health()
        for day, kind, before, after, detail in [(1, 'DAY_TIMING', 0, 1, dict(wallSeconds='20')),
                (1, 'DIAGNOSTIC_COST', 0, 0, dict(bytes='1000', wallSeconds='20')),
                (3, 'DAY_TIMING', 1, 3, dict(wallSeconds='60')),
                (3, 'DIAGNOSTIC_COST', 0, 1, dict(bytes='1200', wallSeconds='80'))]:
            health.observe(dict(day=str(day), kind=kind, before=before, after=after), detail, base.m.number)
        cost = health.report({})['watchdog']['cost']
        self.assertEqual(cost['projected_30_day_bytes_if_rate_continues'], 3000)
        self.assertAlmostEqual(cost['seconds_per_day_including_pauses'], 80 / 3)

    def test_unfinished_lifecycle_is_pending_only_while_open(self):
        row = ('QUEST_LIFECYCLE_BEGIN', 'q', 'StartQuest', 0, 0, 'operation=o')
        self.assertEqual(self.run_rows([row], False)['integrity'], [])
        self.assertIn('Closed session has unmatched lifecycle operations', self.run_rows([row])['integrity'])

    def test_lifecycle_pair_different_owner_fails_integrity(self):
        rows = [('LOGISTICS_BEGIN', 'p1', 'TryBuy', 0, 0, 'operation=o'),
                ('LOGISTICS_END', 'p2', 'TryBuy', 0, 0, 'operation=o')]
        self.assertEqual(self.run_rows(rows)['status'], 'integrity_review_required')

    def test_many_unknown_changes_keep_bounded_examples_exact_count(self):
        rows = [('WALLET_CHANGE', 'w', 'Gold', 1, 2, 'source=set_Gold')] * 20
        rows += [('WALLET_CHECK', 'w', 'Gold', 21, 25, '')]
        result = self.run_rows(rows)['unknown_investigations']['cases']
        self.assertEqual(result['unclassified_cash:w:set_Gold']['observations'], 20)
        example = result['wallet_residual:w']['supporting_evidence'][0]
        self.assertEqual(example['omitted_changes'], 12)
        self.assertEqual(len(example['nearby_changes']), 8)

    def test_model_witness_identifies_mechanism_but_not_other_residuals(self):
        for delta in (10, 12):
            result = self.run_rows([
                ('TOWN_CASH_BEGIN', 'town', 'UpdateTownGold', 100, 100, 'townCashOperation=o'),
                ('TOWN_CASH_END', 'town', 'UpdateTownGold', 100, 100 + delta,
                 'townCashOperation=o; originalRan=True; error=none; modelCalls=1; modelResult=10')])
            mechanisms = result['unknown_investigations']['identified_mechanisms']
            evidence = next(iter(mechanisms.values()))
            self.assertEqual(bool(evidence['conflicting_evidence']), delta != 10)
            self.assertEqual(bool(evidence['supporting_evidence']), delta == 10)

    def test_cash_unknown_promotes_only_after_matching_executed_scope(self):
        rows = [('CASH_PURPOSE_BEGIN', 'b', 'owner_payout', 0, 0, 'operation=p; parentOperation=none'),
                ('WALLET_CHANGE', 'w', 'Gold', 100, 90, 'source=set_Gold; cashPurposeOperation=p')]
        open_report = self.run_rows(rows, False)
        self.assertEqual(open_report['diagnostic_health']['wallet_purposes'][0]['purpose'], 'unexplained_pending_boundary')
        for ran, expected in [('True', 'owner_payout'), ('False', 'skipped_or_failed_boundary_unclassified')]:
            result = self.run_rows(rows + [('CASH_PURPOSE_END', 'b', 'owner_payout', 0, 0,
                                            'operation=p; parentOperation=none; originalRan=' + ran + '; error=none')])
            self.assertEqual(result['diagnostic_health']['wallet_purposes'][0]['purpose'], expected)
            case = result['unknown_investigations']['cases']['unclassified_cash:w:set_Gold']
            self.assertEqual(case['status'] == 'purpose_boundary_identified_not_exclusive_cause', ran == 'True')
            self.assertEqual(bool(case['conflicting_evidence']), ran == 'False')

    def test_nested_transfer_uses_proven_parent_not_a_guess(self):
        outcomes = {'child': dict(valid=True, purpose='gold_transfer_unclassified', parent='parent'),
                    'parent': dict(valid=True, purpose='owner_payout', parent='none')}
        self.assertEqual(resolve('child', outcomes), 'owner_payout')
        outcomes['parent']['valid'] = False
        self.assertEqual(resolve('child', outcomes), 'skipped_or_failed_boundary_unclassified')
        del outcomes['parent']
        self.assertEqual(resolve('child', outcomes), 'unexplained_pending_boundary')
        outcomes['child']['parent'] = 'child'
        self.assertEqual(resolve('child', outcomes), 'unexplained_invalid_parent_chain')

    def test_hook_presence_does_not_certify_unexercised_completion(self):
        result = self.run_rows([('CAPTURE_HOOK', 'TaleWorlds.CampaignSystem.QuestBase', 'CompleteQuestWithSuccess', 0, 1,
                                'mvid=fixture; token=1')])
        hooks = result['diagnostic_health']['watchdog']['lifecycle_hook_execution']
        self.assertEqual(next(iter(hooks.values()))['status'], 'installed_but_execution_not_witnessed')

    def test_accepted_order_links_output_and_wallet_not_an_unrelated_cycle(self):
        rows = [('WORKSHOP_INPUT_WITNESS', 's', 'wool', 1, 0, 'privateState=accepted_current_attempt; order=o; cycle=3'),
                ('PROCUREMENT_MOVEMENT', 's', 'consumption', 0, 0, 'order=o; receipt=r'),
                ('WORKSHOP_PRODUCED', 's', 'felt', 0, 1, 'cycle=3'),
                ('WALLET_CHANGE', 'w', 'Gold', 0, 10, 'workshopCycle=3'),
                ('WORKSHOP_CYCLE', 's', 'succeeded', 0, 1, 'cycle=3'),
                ('WALLET_CHANGE', 'w', 'Gold', 10, 20, 'workshopCycle=3')]
        order = self.run_rows(rows)['timelines']['orders']['o']
        self.assertEqual(order['related_wallet_flows_not_profit']['w']['net'], 10)
        self.assertEqual(order['stages']['workshop_produced:felt']['count'], 1)

    def test_full_audit_rejects_evidence_replaced_after_triage(self):
        import tempfile
        import hashlib
        from pathlib import Path
        from EvidenceAuditBundle import audit
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'capture'
            path.write_bytes(b'new-data')
            with self.assertRaises(ValueError):
                audit(path, loader=lambda name: lambda source: {}, expected_sha256=hashlib.sha256(b'old-data').hexdigest())

    def test_extended_evidence_is_linked_not_discarded_from_compact_report(self):
        import tempfile
        import json
        from pathlib import Path
        report = self.run_rows([('WALLET_CHECK', 'w', 'Gold', 10, 12, '')])
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'report'
            base.m.write_report(report, path)
            index = json.loads(path.with_suffix('.json').read_text())
            details = json.loads(Path(index['unknown_investigations']['detail_path']).read_text())
            self.assertIn('wallet_residual:w', details['cases'])
            self.assertIn('Unknown-cause investigations: 1.', path.with_suffix('.md').read_text())

    def test_changed_parent_cannot_promote_unknown_cash(self):
        rows = [('CASH_PURPOSE_BEGIN', 'b', 'owner_payout', 0, 0, 'operation=p; parentOperation=original'),
                ('WALLET_CHANGE', 'w', 'Gold', 0, 10, 'cashPurposeOperation=p; source=set_Gold'),
                ('CASH_PURPOSE_END', 'b', 'owner_payout', 0, 0,
                 'operation=p; parentOperation=different; originalRan=True; error=none')]
        result = self.run_rows(rows)
        self.assertEqual(result['status'], 'integrity_review_required')
        self.assertEqual(result['diagnostic_health']['wallet_purposes'][0]['purpose'], 'skipped_or_failed_boundary_unclassified')

    def test_exception_receipt_is_explicit_not_success(self):
        result = self.run_rows([
            ('QUEST_LIFECYCLE_BEGIN', 'q', 'StartQuest', 0, 0, 'operation=p'),
            ('QUEST_LIFECYCLE_END', 'q', 'StartQuest', 0, 0, 'operation=p; originalRan=True; error=NativeFailure')])
        self.assertEqual(result['findings'][0]['issue'], 'lifecycle_exception')


if __name__ == '__main__':
    unittest.main()

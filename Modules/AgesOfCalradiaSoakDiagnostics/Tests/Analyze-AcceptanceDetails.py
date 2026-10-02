"""One streaming evidence pass. Context labels are not reconstructed transactions.

Use on one session (Analyze-FrameworkPeriods separates reloads). No balance
changes, synthetic replacement receipts or automatic demand/shortage diagnoses.
"""
import collections
import csv
import math

csv.field_size_limit(24000000)


def fields(text):
    return dict(p.strip().split('=', 1) for p in text.split(';') if '=' in p)


def category(d):
    explicit = d.get('cashCategory')
    if explicit:
        return explicit
    chain = d.get('callers', d.get('source', ''))
    # Retain chain beside the label; none of these labels proves a native cause.
    if 'CalculateHeroIncomeFromWorkshops' in chain:
        return 'workshop_distribution_context'
    if 'CalculateClanExpenses' in chain:
        return 'expense_calculation_context'
    if any(n in chain for n in ('ClanFinance', 'DailyTickHero', 'DailyTickClan')):
        return 'finance_settlement_context'
    if any(n in chain for n in ('SellItemsAction', 'SellGoodsForTradeAction', 'CaravansCampaignBehavior')):
        return 'trade_context'
    if 'Grant' in chain or 'Cheat' in chain:
        return 'grant_named_caller_requires_review'
    if d.get('reward') not in (None, 'none'):
        return 'reward_context'
    return 'unclassified'


def analyze(path):
    wallets, recipes, removals = {}, {}, {}
    stock = collections.defaultdict(collections.Counter)
    procurement = collections.Counter()
    plans = {}
    ship_counts, owner_changes = collections.Counter(), []
    problems, gaps, sessions = [], set(), set()
    costs, timing, hook_ids = [], [], set()
    start = end = None
    closed = False
    gate_seen = set()
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            kind, owner, metric = row['kind'], row['owner'], row['metric']
            d = fields(row['detail'])
            day, before, after = (float(row[k]) for k in ('day', 'before', 'after'))
            if not all(math.isfinite(x) for x in (day, before, after)):
                raise ValueError('Nonfinite acceptance evidence')
            sessions.add(row['session'])
            if len(sessions) > 1:
                raise ValueError('Separate reload sessions before acceptance analysis')
            if kind == 'SESSION_START': start = day
            if kind == 'SESSION_END': end, closed = day, True
            if kind == 'CAPTURE_HOOK': hook_ids.add((owner, metric, d.get('token')))
            if kind == 'PROCUREMENT_PLAN':
                plan = plans.setdefault(owner, dict(decisions=0, reasons=collections.Counter(),
                    candidate_rejections=collections.Counter(), last_forecast={}, policies=[]))
                plan['decisions'] += 1
                if 'plannerComputeMilliseconds' in d:
                    elapsed = float(d['plannerComputeMilliseconds'])
                    if not math.isfinite(elapsed) or elapsed < 0:
                        raise ValueError('Invalid planner timing evidence')
                    cost = plan.setdefault('compute_cost_excludes_logging', dict(samples=0, total_ms=0, maximum_ms=0))
                    cost['samples'] += 1
                    cost['total_ms'] += elapsed
                    cost['maximum_ms'] = max(cost['maximum_ms'], elapsed)
                plan['reasons'][metric] += 1
                for key, value in d.items():
                    if key.endswith('Rejected') or key == 'leadStockSatisfied':
                        count = int(value)
                        if count < 0: raise ValueError('Negative planner candidate count')
                        plan['candidate_rejections'][key] += count
                if d.get('policy') not in plan['policies']: plan['policies'].append(d.get('policy'))
                if 'selectedDailyRate' in d:
                    plan['last_forecast'] = {k:d[k] for k in (
                        'selectedDailyRate', 'selectedThresholdBatches', 'selectedLeadDays',
                        'selectedSource', 'selectedRecipe', 'selectedBatches', 'selectedScoreEstimate',
                        'selectedConservativeOutput', 'selectedDailyExpense', 'selectedLandedCost', 'selectedRequiredMargin',
                        'ironSupplierReserveDays', 'maximumAdaptiveBatches', 'deliveryDelayCostWeight', 'wineExpenseCoverage') if k in d}
            if kind == 'DIAGNOSTIC_COST':
                costs.append(dict(day=day, writer_seconds_lower_bound=after, detail=d))
            if kind == 'DAY_TIMING':
                timing.append(dict(day=day, interval_days=after-before, wall_seconds=float(d['wallSeconds']),
                                   bytes=int(d.get('bytes', 0))))
            if kind.startswith('WALLET_') and owner.startswith(('Hero/', 'Kingdom/', 'Clan/')):
                w = wallets.setdefault(owner, dict(identity={}, baseline_day=None, check_day=None,
                    maximum_absolute_residual=0, final_residual=None, observed_in=0, observed_out=0,
                    sources={}, identity_changed=False))
                if kind == 'WALLET_IDENTITY':
                    if w['identity'] and w['identity'] != d: w['identity_changed'] = True
                    w['identity'] = d
                elif kind == 'WALLET_BASELINE':
                    w['baseline_day'] = day
                    w['baseline_identity'] = d
                elif kind == 'WALLET_CHECK':
                    w['check_day'], w['final_residual'] = day, after-before
                    w['maximum_absolute_residual'] = max(w['maximum_absolute_residual'], abs(after-before))
                elif kind == 'WALLET_CHANGE':
                    delta = after-before
                    w['observed_in'] += max(0, delta)
                    w['observed_out'] += max(0, -delta)
                    key = category(d) + '/' + d.get('callers', d.get('source', 'unknown'))
                    source = w['sources'].setdefault(key, dict(count=0, incoming=0, outgoing=0))
                    source['count'] += 1; source['incoming'] += max(0, delta); source['outgoing'] += max(0, -delta)
            if kind.startswith('WORKSHOP_') and d.get('recipe') is not None:
                key = owner + '/recipe:' + d['recipe']
                r = recipes.setdefault(key, dict(type=d.get('type'), inputs=d.get('inputs'), outputs=d.get('outputs'),
                    cycle_outcomes=collections.Counter(), gate_cycles=collections.Counter(), consumed=collections.Counter(),
                    produced=collections.Counter(), progress_samples=0, progress_without_attempt=0))
                if kind == 'WORKSHOP_CYCLE':
                    r['cycle_outcomes'][metric] += 1
                    gate_seen = {entry for entry in gate_seen if entry[:2] != (owner, d.get('cycle'))}
                elif kind == 'WORKSHOP_GATE':
                    label = None
                    if 'DetermineItemRosterHasSufficientInputs' in metric and after == 0:
                        label = 'input_gate_rejected_not_independent_shortage'
                    elif metric.startswith('Can') and after == 0:
                        output = float(d.get('outputIncome', 'nan'))
                        hurdle = float(d.get('effectiveProfitHurdle', d.get('nativeProfitHurdle', 'nan')))
                        gold = float(d.get('townGold', 'nan'))
                        if math.isfinite(output) and math.isfinite(hurdle) and output <= hurdle:
                            label = ('effective' if 'effectiveProfitHurdle' in d else 'native') + '_margin_hurdle_candidate_not_weak_demand_proof'
                        elif d.get('effectCapital') == 'True' and math.isfinite(output) and gold < output:
                            label = 'town_cash_gate_candidate'
                        else: label = 'unclassified_gate_rejection'
                    token = owner, d.get('cycle'), label
                    if label and token not in gate_seen:
                        gate_seen.add(token); r['gate_cycles'][label] += 1
                elif kind == 'WORKSHOP_CONSUMED': r['consumed'][metric] += after-before
                elif kind == 'WORKSHOP_PRODUCED': r['produced'][metric] += after-before
                elif kind == 'WORKSHOP_PROGRESS':
                    r['progress_samples'] += 1
                    if int(d.get('attempts', 0)) == 0: r['progress_without_attempt'] += 1
            if kind == 'MARKET_DELTA' and d.get('parentMarket') == '0':
                stock[d.get('settlement', owner) + '/' + metric][d.get('operation', 'unknown')] += after-before
            if kind == 'PROCUREMENT_TRANSFER': procurement['transfer/' + metric] += 1
            if kind == 'PROCUREMENT_MOVEMENT': procurement['movement/' + metric] += 1
            if kind == 'SHIP_OWNER_CHANGE':
                ship_counts['owner/' + metric] += 1
                owner_changes.append(dict(ship=d.get('ship'), before=d.get('from'), after=d.get('to'),
                                          original_ran=d.get('originalRan'), day=day))
            if kind == 'SHIP_LIFECYCLE_BEGIN':
                key = d.get('removal')
                if not key or key in removals: problems.append('Missing/duplicate removal identity')
                else: removals[key] = dict(party=owner, before={}, after={}, original_ran=None)
            if kind in ('SHIP_MEMBERSHIP', 'SHIP_LIFECYCLE_END'):
                r = removals.get(d.get('removal'))
                if r is None: problems.append('Ship observation without removal begin'); continue
                if kind == 'SHIP_MEMBERSHIP':
                    if metric not in ('before', 'after') or not d.get('ship'):
                        problems.append('Invalid ship membership'); continue
                    if d['ship'] in r[metric]: problems.append('Duplicate ship membership')
                    r[metric][d['ship']] = dict(member=bool(after), owner=d.get('owner'))
                else:
                    r['original_ran'] = d.get('originalRan')
                    if r['original_ran'] not in ('True', 'False'): problems.append('Missing removal original-run flag')
                    r['party_active'] = d.get('partyActive')
                    if r['original_ran'] == 'True':
                        ship_counts['executed_removal'] += 1
                        for ship, state in r['before'].items():
                            final = r['after'].get(ship)
                            if final is None: gaps.add('removal_after_membership_missing')
                            elif final['member'] or final['owner'] == 'party:' + owner:
                                gaps.add('removed_party_retains_ship_requires_review')
                    else: ship_counts['skipped_removal'] += 1
    noble_clans = {}
    for owner, w in wallets.items():
        local = []
        if w['final_residual'] is None: local.append('wallet_check_missing')
        if w['maximum_absolute_residual']: local.append('unexplained_wallet_change')
        if start is None or end is None or w['baseline_day'] != start or w['check_day'] != end:
            local.append('wallet_window_incomplete')
        if w['identity_changed']: local.append('clan_membership_changed_use_transaction_identity')
        if owner.startswith('Hero/') and not w['identity']: local.append('hero_clan_identity_missing')
        w['coverage_gaps'] = local
        identity = w['identity']
        if identity.get('noble') == 'True' and identity.get('clan') and not w['identity_changed']:
            clan = noble_clans.setdefault(identity['clan'], dict(observed_in=0, observed_out=0,
                wallet_ids=[], coverage_gaps=set(), maximum_wallet_residual_sum=0))
            clan['observed_in'] += w['observed_in']; clan['observed_out'] += w['observed_out']
            clan['wallet_ids'].append(owner); clan['coverage_gaps'].update(local)
            clan['maximum_wallet_residual_sum'] += w['maximum_absolute_residual']
    for clan in noble_clans.values(): clan['coverage_gaps'] = sorted(clan['coverage_gaps'])
    if not closed: gaps.add('capture_open_not_final_acceptance')
    if not hook_ids: gaps.add('runtime_readiness_hooks_not_recorded')
    if not costs: gaps.add('writer_cost_not_measured')
    for sample in costs:
        if int(sample['detail'].get('discardedRows', 0)) > 0:
            gaps.add('discarded_diagnostic_records')
        if sample['writer_seconds_lower_bound'] < 0: problems.append('Negative writer cost')
    if not wallets: gaps.add('wealth_wallets_not_exercised')
    if not removals: gaps.add('naval_removal_not_exercised')
    if any(r['original_ran'] is None for r in removals.values()):
        if closed: problems.append('Unclosed ship removal')
        else: gaps.add('open_ship_removal_not_yet_integrity_failure')
    # Never infer demand from one rejected quote or absence of a selling event.
    chains = {'livestock_to_meat': {}, 'wool_outputs': {}}
    focus = {}
    for key, r in recipes.items():
        inputs = {p.split(':')[0] for p in (r['inputs'] or '').split(',')}
        outputs = {p.split(':')[0] for p in (r['outputs'] or '').split(',')}
        if inputs & {'sheep', 'cow', 'hog'} and 'meat' in outputs: chains['livestock_to_meat'][key] = r
        if 'wool' in inputs: chains['wool_outputs'][key] = r
        if any(n in (r['type'] or '') for n in ('wine', 'smith', 'linen')): focus[key] = r
        gates = r['gate_cycles']
        r['next_checks'] = []
        if gates['input_gate_rejected_not_independent_shortage']:
            r['next_checks'].append('Match failed input attempts to local stock, private cargo arrivals and later successful consumption; do not count gates as independent shortages.')
        if gates['town_cash_gate_candidate']:
            r['next_checks'].append('Inspect town cash at the rejected attempt before changing material supply.')
        if gates['native_margin_hurdle_candidate_not_weak_demand_proof'] or gates['effective_margin_hurdle_candidate_not_weak_demand_proof']:
            r['next_checks'].append('Compare paid output, actual inputs and recorded margin threshold; inspect market consumption/removal before calling demand weak.')
        if r['progress_without_attempt']:
            r['next_checks'].append('Inspect recorded recipe progress/cadence and day coverage; no-attempt samples are not input failures.')
        if not r['cycle_outcomes']: r['next_checks'].append('Recipe had no observed completed cycle; no profitability verdict.')
    for name, evidence in chains.items():
        if not evidence: gaps.add(name + '_not_exercised')
    rates = []
    for previous, current in zip(timing, timing[1:]):
        elapsed = current['day'] - previous['day']
        growth = current['bytes'] - previous['bytes']
        if elapsed > 0 and growth >= 0:
            rates.append(dict(day=current['day'], bytes_per_day=growth/elapsed,
                              projected_30_day_bytes=growth/elapsed*30,
                              seconds_per_day_including_pauses=current['wall_seconds']/current['interval_days']
                              if current['interval_days'] > 0 else None))
    if not plans: gaps.add('procurement_plan_decisions_not_observed')
    return dict(problems=problems, coverage_gaps=sorted(gaps), wealth=dict(wallets=wallets, stable_noble_clans=noble_clans,
        limit='Canonical net wallet flows only. Gross transfers and workshop distributions are not added again. Context labels require caller review; residuals are not inflation proof.'),
        supply_chains=chains, focus_workshops=focus, procurement_outcomes=dict(procurement),
        procurement_planning=dict(workshops=plans, limit='Candidate/recipe filter counts are not independent shortages. An offer is not a committed shipment.'),
        market_causes={k:dict(v) for k,v in stock.items()},
        naval=dict(counts=dict(ship_counts), removals=removals, owner_changes=owner_changes,
                   limit='Membership/owner clearance is not destruction; use reward accounting for actual destruction and payout joins.'),
        diagnostic_cost=dict(samples=costs, day_timing=timing, measured_growth=rates,
            limit='Writer+flush time is a measured lower bound, not total observer overhead. Wall day timing includes pauses. No claimed FPS cost.'),
        decision='One acceptance evidence bundle; no automatic balance adjustments or rare-event reruns.')

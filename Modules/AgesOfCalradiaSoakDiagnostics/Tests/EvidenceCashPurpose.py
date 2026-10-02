"""Resolve only matched, executed financial scopes; never guess from names."""
KNOWN_PURPOSES = {'owner_payout', 'operating_expense', 'capital_reset',
                  'native_kingdom_budget_grant', 'native_daily_finance_settlement',
                  'tribute_wallet_settlement', 'kingdom_budget_distribution',
                  'party_wages_and_funding', 'market_trade', 'native_party_wage_payment', 'native_party_funding'}


def resolve(operation, outcomes):
    visited = set()
    fallback = 'unexplained'
    while operation not in (None, '', 'none'):
        if operation in visited or len(visited) >= 64:
            return 'unexplained_invalid_parent_chain'
        visited.add(operation)
        outcome = outcomes.get(operation)
        if outcome is None:
            return 'unexplained_pending_boundary'
        if not outcome['valid']:
            return 'skipped_or_failed_boundary_unclassified'
        purpose = outcome['purpose']
        if purpose in KNOWN_PURPOSES:
            return purpose
        fallback = purpose
        operation = outcome.get('parent')
    return fallback

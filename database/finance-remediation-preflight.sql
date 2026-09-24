-- Read-only review before applying the Finance/Payments opening uniqueness migrations.
-- Run against a reviewed copy of the target database. This script changes no data.
-- The Finance query requires the AddFinanceRemediationRecords migration's table.

SELECT 'finance-opening-roots' AS issue, finance_account_id::text AS owner_id,
       count(*) AS conflicting_roots,
       string_agg(id::text || ' (' || status || ', ' || amount::text || ')', '; ' ORDER BY created_at) AS records
FROM finance.finance_opening_balances
WHERE reverses_opening_balance_id IS NULL AND status <> 'Rejected'
GROUP BY finance_account_id
HAVING count(*) > 1;

SELECT 'merchant-opening-roots' AS issue, merchant_id::text AS owner_id,
       count(*) AS conflicting_roots,
       string_agg(id::text || ' (' || status || ', ' || amount::text || ')', '; ' ORDER BY created_at) AS records
FROM payments.merchant_opening_balance_charges
WHERE reverses_charge_id IS NULL AND status <> 'Rejected'
GROUP BY merchant_id
HAVING count(*) > 1;

-- Existing negative posting-time balances are review items, not automatic fixes.
WITH movements AS (
    SELECT finance_account_id, id, created_at, direction, amount,
           sum(CASE WHEN direction = 'Credit' THEN amount ELSE -amount END)
               OVER (PARTITION BY finance_account_id ORDER BY created_at,
                     CASE WHEN direction = 'Credit' THEN 0 ELSE 1 END, id) AS running_balance
    FROM finance.finance_ledger_entries
    WHERE status = 'Posted'
)
SELECT finance_account_id, min(running_balance) AS lowest_historical_balance,
       min(created_at) FILTER (WHERE running_balance < 0) AS first_negative_at
FROM movements
GROUP BY finance_account_id
HAVING min(running_balance) < 0;

BEGIN;

SET client_encoding = 'UTF8';

WITH seed_accounts(name, type, reference, details) AS (
    VALUES
        ('Main Cash', 'CashOnHand', 'SEED-CASH', 'Manual testing safe / cash-in-hand account'),
        ('CIB Bank', 'BankAccount', 'SEED-BANK', 'Manual testing bank account'),
        ('Vodafone Cash', 'Wallet', 'SEED-WALLET', 'Manual testing wallet account')
)
INSERT INTO finance.finance_accounts
    (id, name, type, is_active, reference, details, created_by, created_at, updated_at)
SELECT
    uuid_generate_v4(),
    name,
    type,
    true,
    reference,
    details,
    '00000000-0000-0000-0000-000000000000',
    now()::timestamp,
    NULL::timestamp
FROM seed_accounts
ON CONFLICT (name, type) DO UPDATE
SET
    is_active = true,
    reference = EXCLUDED.reference,
    details = EXCLUDED.details,
    updated_at = now()::timestamp;

COMMIT;

SELECT
    name,
    type,
    is_active,
    reference,
    details
FROM finance.finance_accounts
WHERE (name, type) IN (
    ('Main Cash', 'CashOnHand'),
    ('CIB Bank', 'BankAccount'),
    ('Vodafone Cash', 'Wallet')
)
ORDER BY
    CASE type
        WHEN 'CashOnHand' THEN 1
        WHEN 'BankAccount' THEN 2
        WHEN 'Wallet' THEN 3
        ELSE 99
    END,
    name;

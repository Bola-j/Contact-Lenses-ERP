BEGIN;

SET client_encoding = 'UTF8';

WITH top_categories(kind, code, english_name, arabic_name, sort_order) AS (
    VALUES
        ('Expense', 'Supply', 'Supply', 'التوريد', 10),
        ('Expense', 'Salaries', 'Salaries', 'الرواتب', 20),
        ('Expense', 'SystemServer', 'System Server', 'خادم النظام', 30),
        ('Expense', 'Media', 'Media', 'الإعلام', 40),
        ('Expense', 'ShopifySubscription', 'Shopify Subscription', 'اشتراك شوبيفاي', 50),
        ('Expense', 'WarehouseRental', 'Warehouse Rental', 'إيجار المخزن', 60),
        ('Expense', 'Bills', 'Bills', 'الفواتير', 70),
        ('Expense', 'TransferFee', 'Transfer Fee', 'رسوم التحويل', 80)
),
upsert_top AS (
    INSERT INTO finance.finance_categories
        (id, kind, code, english_name, arabic_name, parent_id, sort_order, is_active, created_by, created_at, updated_at)
    SELECT
        uuid_generate_v4(),
        kind,
        code,
        english_name,
        arabic_name,
        NULL::uuid,
        sort_order,
        true,
        '00000000-0000-0000-0000-000000000000',
        now()::timestamp,
        NULL::timestamp
    FROM top_categories
    ON CONFLICT (kind, code) DO UPDATE
    SET
        english_name = EXCLUDED.english_name,
        arabic_name = EXCLUDED.arabic_name,
        parent_id = NULL,
        sort_order = EXCLUDED.sort_order,
        is_active = true,
        updated_at = now()::timestamp
    RETURNING id, kind, code
),
bill_parent AS (
    SELECT id
    FROM upsert_top
    WHERE kind = 'Expense' AND code = 'Bills'
    UNION ALL
    SELECT id
    FROM finance.finance_categories
    WHERE kind = 'Expense' AND code = 'Bills'
    LIMIT 1
),
bill_children(kind, code, english_name, arabic_name, sort_order) AS (
    VALUES
        ('Expense', 'Electricity', 'Electricity', 'الكهرباء', 71),
        ('Expense', 'Gas', 'Gas', 'الغاز', 72),
        ('Expense', 'Internet', 'Internet', 'الإنترنت', 73),
        ('Expense', 'Water', 'Water', 'المياه', 74)
)
INSERT INTO finance.finance_categories
    (id, kind, code, english_name, arabic_name, parent_id, sort_order, is_active, created_by, created_at, updated_at)
SELECT
    uuid_generate_v4(),
    child.kind,
    child.code,
    child.english_name,
    child.arabic_name,
    parent.id,
    child.sort_order,
    true,
    '00000000-0000-0000-0000-000000000000',
    now()::timestamp,
    NULL::timestamp
FROM bill_children child
CROSS JOIN bill_parent parent
ON CONFLICT (kind, code) DO UPDATE
SET
    english_name = EXCLUDED.english_name,
    arabic_name = EXCLUDED.arabic_name,
    parent_id = EXCLUDED.parent_id,
    sort_order = EXCLUDED.sort_order,
    is_active = true,
    updated_at = now()::timestamp;

COMMIT;

SELECT
    category.code,
    category.english_name,
    category.arabic_name,
    COALESCE(parent.code, '') AS parent_code,
    category.sort_order,
    category.is_active
FROM finance.finance_categories category
LEFT JOIN finance.finance_categories parent ON parent.id = category.parent_id
WHERE category.kind = 'Expense'
  AND category.code IN (
      'Supply',
      'Salaries',
      'SystemServer',
      'Media',
      'ShopifySubscription',
      'WarehouseRental',
      'Bills',
      'TransferFee',
      'Electricity',
      'Gas',
      'Internet',
      'Water'
  )
ORDER BY category.sort_order, category.code;

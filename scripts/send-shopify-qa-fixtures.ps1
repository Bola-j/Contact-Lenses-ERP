param(
    [ValidateSet('admin','erp','company')][string]$Instance,
    [Parameter(Mandatory)][string]$SkuCode
)
$ErrorActionPreference = 'Stop'
$apiPort = @{ admin=55001; erp=55002; company=55003 }[$Instance]
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repo "artifacts/certification/shopify-20261019/$Instance-runtime.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.database -ne "lensee_shopify_qa_$Instance" -or $manifest.apiUrl -ne "http://127.0.0.1:$apiPort") { throw 'Only the isolated Shopify QA target is allowed.' }
$run = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
$fixture = @{ role=$Instance; targetSku=$SkuCode; variantSku="TEST-UNRESOLVED-$Instance-$run"; routineVariantId="variant-routine-$run" }
foreach ($kind in @('attention','paid','pending')) {
    $orderId = "TEST-$Instance-$kind-$run"
    $code = if ($kind -eq 'attention') { $fixture.variantSku } else { $SkuCode }
    $now = [DateTime]::UtcNow.ToString('o')
    $payload = @{
        id=$orderId; name="#$orderId"; created_at=$now; updated_at=$now; test=$true
        currency='EGP'; presentment_currency='EGP'; financial_status=$(if($kind -eq 'pending'){'pending'}else{'paid'})
        subtotal_price='20.00'; total_discounts='0.00'; total_shipping_price='5.00'; total_tax='0.00'; total_price='25.00'; taxes_included=$false
        payment_gateway_names=@('TEST synthetic provider notice'); email='qa-buyer@example.invalid'; phone='01000000000'
        billing_address=@{first_name='TEST';last_name='Buyer'}; shipping_address=@{address1='TEST isolated address';city='Cairo';country='Egypt'}
        line_items=@(@{id="line-$kind-$run";variant_id=$(if($kind -eq 'attention'){"variant-attention-$run"}else{$fixture.routineVariantId});sku=$code;title='TEST lens';quantity=2;price='10.00';total_discount='0.00';discount_allocations=@();tax_lines=@()})
        shipping_lines=@(@{id="shipping-$run";title='TEST delivery';price='5.00';discounted_price='5.00';tax_lines=@()})
    } | ConvertTo-Json -Depth 12 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    # Synthetic signing material belongs only to this isolated harness. No provider verification is created.
    $mac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('IsolatedQaSyntheticWebhookOnly20261019!'))
    try { $signature = [Convert]::ToBase64String($mac.ComputeHash($bytes)) } finally { $mac.Dispose() }
    $headers = @{'X-Shopify-Hmac-Sha256'=$signature;'X-Shopify-Webhook-Id'="delivery-$orderId";'X-Shopify-Topic'='orders/create';'X-Shopify-Shop-Domain'='lensee-erp-qa-20261018.myshopify.com';'X-Shopify-Triggered-At'=$now}
    $receipt = Invoke-RestMethod "$($manifest.apiUrl)/api/v1/integrations/shopify/webhooks" -Method Post -Headers $headers -Body $bytes -ContentType application/json
    if ($receipt.status -notin @('Queued','Duplicate')) { throw "Unexpected QA receipt status: $($receipt.status)" }
    $fixture[$kind+'Order'] = $orderId
}
$fixturePath = Join-Path $repo "artifacts/certification/shopify-20261019/$Instance-fixtures.json"
$fixture | ConvertTo-Json | Set-Content -LiteralPath $fixturePath
Write-Output "Synthetic signed receipts queued through the real isolated receiver. Fixture references: $fixturePath"

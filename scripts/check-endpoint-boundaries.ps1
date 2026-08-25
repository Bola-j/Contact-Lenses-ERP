$ErrorActionPreference = "Stop"

# Expand-first enforcement: existing endpoint debt is frozen while command/query
# services replace it. Lower a limit only after the corresponding extraction.
$limits = @{
    "AuthEndpoints.cs" = 4
    "CatalogEndpoints.cs" = 10
    "CrmEndpoints.cs" = 7
    "InventoryEndpoints.cs" = 2
    "NotificationsEndpoints.cs" = 6
    "OperationsEndpoints.cs" = 22
    "PaymentsEndpoints.cs" = 22
    "ReportsEndpoints.cs" = 2
    "ShopifyEndpoints.cs" = 2
    "StocktakeEndpoints.cs" = 4
    "SupplyEndpoints.cs" = 7
    "UserEndpoints.cs" = 9
}

$pattern = '\b(?:SaveChangesAsync|SaveChanges|ExecuteUpdateAsync|ExecuteUpdate|BeginTransactionAsync|BeginTransaction|UseTransactionAsync|UseTransaction)\s*\('
$endpointRoot = Join-Path $PSScriptRoot "..\backend\Lensee.Host\Endpoints"
$violations = [System.Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath $endpointRoot -Filter "*Endpoints.cs" | ForEach-Object {
    $count = [regex]::Matches((Get-Content -Raw -LiteralPath $_.FullName), $pattern).Count
    $limit = $limits[$_.Name]
    if ($count -gt 0 -and $null -eq $limit) {
        $violations.Add("$($_.Name) is not in the endpoint-boundary baseline. Move persistence into an application service before adding this endpoint file.")
    } elseif ($null -ne $limit -and $count -gt $limit) {
        $violations.Add("$($_.Name) has $count direct persistence/transaction calls; baseline is $limit. Extract the new command/query behavior into a service instead.")
    }
}

if ($violations.Count -gt 0) {
    throw ($violations -join [Environment]::NewLine)
}

Write-Host "Endpoint-boundary guard passed."

param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$ApiToken = "<PASTE_API_TOKEN_HERE>",
    [int]$RequestDelayMs = 250,
    [switch]$Preview
)

# HTTP equivalent of scripts/seed-lenses-direct-db.ps1.
#
# Edit the CONFIG section before running this file. The IDs are deliberately
# placeholders so this script cannot target an unintended database record.
# Quantities are expressed in packs, matching the current API contracts.

$ErrorActionPreference = "Stop"
$ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")
$jsonContentType = "application/json"

function Assert-ConfiguredValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($Preview) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match "<[^>]+>" -or $Value -like "*REPLACE-ME*") {
        throw "$Name is not configured. Replace the placeholder value before running this script."
    }
}

function Assert-GuidValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    Assert-ConfiguredValue -Name $Name -Value $Value
    if ($Preview) {
        return
    }

    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed)) {
        throw "$Name must be a valid GUID."
    }
}

function Invoke-LenseeApi {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST", "PUT", "PATCH", "DELETE")]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    $uri = "$ApiBaseUrl$Path"
    if ($RequestDelayMs -gt 0) {
        Start-Sleep -Milliseconds $RequestDelayMs
    }

    $parameters = @{
        Method  = $Method
        Uri     = $uri
        Headers = $Headers
    }

    if ($null -ne $Body) {
        $parameters.ContentType = $jsonContentType
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    Write-Host "$Method $uri"
    if ($Preview) {
        if ($null -ne $Body) {
            Write-Host ($parameters.Body)
        }
        return $null
    }

    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $response = $_.Exception.Response
        if ($response) {
            $status = [int]$response.StatusCode
            $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
            $detail = $reader.ReadToEnd()
            throw "$Method $uri failed with HTTP $status. $detail"
        }

        throw
    }
}

function Get-RequiredId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [object]$Response
    )

    if ($Preview) {
        return [Guid]::Empty
    }

    $value = $Response.id
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        throw "$Name response did not contain an id."
    }

    return [Guid]::Parse([string]$value)
}

# ------------------------------ CONFIG -----------------------------------
# Use IDs returned by GET /api/v1/inventory/locations and your catalog SKU
# endpoint. Every target below must already have a stock-balance row because
# the target endpoint updates an existing balance.
$TargetQuantities = @(
    [pscustomobject]@{
        Name       = "Main warehouse target - SKU 1"
        LocationId = "11111111-1111-1111-1111-111111111111"
        SkuId      = "<SKU_GUID_1>"
        TargetPacks = 100
    },
    [pscustomobject]@{
        Name       = "Online warehouse target - SKU 1"
        LocationId = "33333333-3333-3333-3333-333333333333"
        SkuId      = "<SKU_GUID_1>"
        TargetPacks = 25
    }
)


$TargetQuantities = @(
    [pscustomobject]@{
        Name       = "Main warehouse target - SKU 1"
        LocationId = "11111111-1111-1111-1111-111111111111"
        SkuId      = "<SKU_GUID_1>"
        TargetPacks = 100
    },
    [pscustomobject]@{
        Name       = "Online warehouse target - SKU 1"
        LocationId = "22222222-2222-2222-2222-222222222222"
        SkuId      = "<SKU_GUID_1>"
        TargetPacks = 25
    }
)
# A supply shipment is created as Draft and then confirmed. UnitPrice must be
# positive before confirmation; lot and expiry are retained on the receipt.
$SupplyRequests = @(
    [pscustomobject]@{
        Name                 = "Supply shipment 1"
        SupplierName         = "<SUPPLIER_NAME>"
        InvoiceNumber        = "SUP-<INVOICE_NUMBER>"
        DestinationLocationId = "11111111-1111-1111-1111-111111111111"
        ShipmentDate         = (Get-Date).ToUniversalTime().ToString("o")
        Notes                = "API supply seed"
        Lines                = @(
            [pscustomobject]@{
                SkuId      = "<SKU_GUID_1>"
                Quantity   = 100
                UnitPrice  = 125.00
                LotNumber  = "LOT-<LOT_NUMBER_1>"
                ExpiryDate = "2028-12-31"
                Notes      = "Supply target quantity: 100 packs"
            }
        )
        Costs                = @(
            [pscustomobject]@{
                CostType    = "Freight"
                Description = "Inbound freight"
                Amount      = 0
            }
        )
    }
)

# A warehouse transfer is a non-financial operation. Its lifecycle is:
# create Draft, confirm (reserve source stock), ship (move out), receive (move
# into destination). Each line quantity is in packs.
$TransferRequests = @(
    [pscustomobject]@{
        Name                  = "Warehouse transfer 1"
        SourceLocationId      = "11111111-1111-1111-1111-111111111111"
        DestinationLocationId = "33333333-3333-3333-3333-333333333333"
        Notes                 = "API warehouse transfer seed"
        Lines                 = @(
            [pscustomobject]@{
                SkuId       = "<SKU_GUID_1>"
                PackQuantity = 25
                PieceQuantity = $null
                EntryMode   = "Packs"
                Section     = "Standard"
                UnitPrice   = $null
                IsBonus     = $false
                LotNumber   = "LOT-<LOT_NUMBER_1>"
                ExpiryDate  = "2028-12-31"
                Notes       = "Transfer target quantity: 25 packs"
            }
        )
    }
)
# ---------------------------- END CONFIG ---------------------------------

Assert-ConfiguredValue -Name "ApiToken" -Value $ApiToken
if ($ApiBaseUrl -notmatch "^https?://") {
    throw "ApiBaseUrl must start with http:// or https://."
}

$headers = @{
    Authorization = "Bearer $ApiToken"
    Accept        = $jsonContentType
}

foreach ($target in $TargetQuantities) {
    Assert-GuidValue -Name "$($target.Name).LocationId" -Value $target.LocationId
    Assert-GuidValue -Name "$($target.Name).SkuId" -Value $target.SkuId
    if ($target.TargetPacks -lt 0) {
        throw "$($target.Name).TargetPacks must be zero or greater."
    }

    Invoke-LenseeApi `
        -Method PUT `
        -Path "/api/v1/inventory/stock-balances/$($target.LocationId)/$($target.SkuId)/target" `
        -Headers $headers `
        -Body @{ targetPacks = [int]$target.TargetPacks } | Out-Null
}

foreach ($supply in $SupplyRequests) {
    Assert-ConfiguredValue -Name "$($supply.Name).SupplierName" -Value $supply.SupplierName
    Assert-ConfiguredValue -Name "$($supply.Name).InvoiceNumber" -Value $supply.InvoiceNumber
    Assert-GuidValue -Name "$($supply.Name).DestinationLocationId" -Value $supply.DestinationLocationId
    foreach ($line in $supply.Lines) {
        Assert-GuidValue -Name "$($supply.Name).Lines.SkuId" -Value $line.SkuId
        Assert-ConfiguredValue -Name "$($supply.Name).Lines.LotNumber" -Value $line.LotNumber
        Assert-ConfiguredValue -Name "$($supply.Name).Lines.ExpiryDate" -Value $line.ExpiryDate
        if ($line.Quantity -le 0) {
            throw "$($supply.Name) quantity must be greater than zero."
        }
        if ($line.UnitPrice -le 0) {
            throw "$($supply.Name) unit price must be greater than zero before confirmation."
        }
    }

    $shipment = Invoke-LenseeApi -Method POST -Path "/api/v1/supply/shipments" -Headers $headers -Body @{
        supplierName          = $supply.SupplierName
        invoiceNumber         = $supply.InvoiceNumber
        shipmentDate          = $supply.ShipmentDate
        destinationLocationId = $supply.DestinationLocationId
        notes                 = $supply.Notes
        lines                 = @($supply.Lines | ForEach-Object {
            @{
                skuId      = $_.SkuId
                quantity   = [int]$_.Quantity
                unitPrice  = [decimal]$_.UnitPrice
                lotNumber  = $_.LotNumber
                expiryDate = $_.ExpiryDate
                notes      = $_.Notes
            }
        })
        costs                 = @($supply.Costs | ForEach-Object {
            @{
                costType    = $_.CostType
                description = $_.Description
                amount      = [decimal]$_.Amount
            }
        })
    }
    $shipmentId = Get-RequiredId -Name $supply.Name -Response $shipment
    if (-not $Preview) {
        Write-Host "Created shipment $shipmentId; confirming receipt."
    }
    Invoke-LenseeApi -Method POST -Path "/api/v1/supply/shipments/$shipmentId/confirm" -Headers $headers -Body @{} | Out-Null
}

foreach ($transfer in $TransferRequests) {
    Assert-GuidValue -Name "$($transfer.Name).SourceLocationId" -Value $transfer.SourceLocationId
    Assert-GuidValue -Name "$($transfer.Name).DestinationLocationId" -Value $transfer.DestinationLocationId
    foreach ($line in $transfer.Lines) {
        Assert-GuidValue -Name "$($transfer.Name).Lines.SkuId" -Value $line.SkuId
        Assert-ConfiguredValue -Name "$($transfer.Name).Lines.LotNumber" -Value $line.LotNumber
        Assert-ConfiguredValue -Name "$($transfer.Name).Lines.ExpiryDate" -Value $line.ExpiryDate
        if ($line.PackQuantity -le 0) {
            throw "$($transfer.Name) pack quantity must be greater than zero."
        }
    }

    $operation = Invoke-LenseeApi -Method POST -Path "/api/v1/operations" -Headers $headers -Body @{
        operationType         = "WarehouseTransfer"
        sourceLocationId      = $transfer.SourceLocationId
        destinationLocationId = $transfer.DestinationLocationId
        paymentMethod         = $null
        notes                 = $transfer.Notes
        lines                 = @($transfer.Lines | ForEach-Object {
            @{
                skuId        = $_.SkuId
                packQuantity = [int]$_.PackQuantity
                pieceQuantity = $_.PieceQuantity
                entryMode    = $_.EntryMode
                section      = $_.Section
                unitPrice    = $_.UnitPrice
                isBonus      = $_.IsBonus
                lotNumber    = $_.LotNumber
                expiryDate   = $_.ExpiryDate
                notes        = $_.Notes
            }
        })
    }
    $operationId = Get-RequiredId -Name $transfer.Name -Response $operation
    if (-not $Preview) {
        Write-Host "Created operation $operationId; confirming, shipping, and receiving."
    }
    Invoke-LenseeApi -Method POST -Path "/api/v1/operations/$operationId/confirm" -Headers $headers -Body @{} | Out-Null
    Invoke-LenseeApi -Method POST -Path "/api/v1/operations/$operationId/ship" -Headers $headers -Body @{} | Out-Null
    Invoke-LenseeApi -Method POST -Path "/api/v1/operations/$operationId/receive" -Headers $headers -Body @{} | Out-Null
}

if ($Preview) {
    Write-Host "Preview complete; no requests were sent."
}
else {
    Write-Host "Supply, target, and warehouse-transfer requests completed."
}

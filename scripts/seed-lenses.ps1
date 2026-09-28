param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$Username = "admin",
    [string]$Password = "Admin123!",
    [int]$RequestDelayMs = 550
)

$ErrorActionPreference = "Stop"

$ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")
$jsonContentType = "application/json"

function Invoke-LenseeApi {
    param(
        [Parameter(Mandatory = $true)]
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
        Method = $Method
        Uri = $uri
        Headers = $Headers
    }

    if ($null -ne $Body) {
        $parameters.ContentType = $jsonContentType
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20)
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

function Get-OrCreateCategory {
    param(
        [string]$Name,
        [string]$ParentName,
        [hashtable]$Headers
    )

    $categories = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/categories" -Headers $Headers
    $existing = $categories | Where-Object {
        $_.name -eq $Name -and (
            ([string]::IsNullOrWhiteSpace($ParentName) -and $null -eq $_.parentId) -or
            (-not [string]::IsNullOrWhiteSpace($ParentName))
        )
    } | Select-Object -First 1

    if ($existing) {
        return $existing
    }

    $parentId = $null
    if (-not [string]::IsNullOrWhiteSpace($ParentName)) {
        $parent = Get-OrCreateCategory -Name $ParentName -ParentName $null -Headers $Headers
        $parentId = $parent.id
    }

    return Invoke-LenseeApi -Method POST -Path "/api/v1/catalog/categories" -Headers $Headers -Body @{
        name = $Name
        parentId = $parentId
    }
}

function Get-OrCreateNestedCategory {
    param(
        [string[]]$Path,
        [hashtable]$Headers
    )

    $parent = $null
    foreach ($name in $Path) {
        $categories = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/categories" -Headers $Headers
        $existing = $categories | Where-Object {
            $_.name -eq $name -and (
                ($null -eq $parent -and $null -eq $_.parentId) -or
                ($null -ne $parent -and $_.parentId -eq $parent.id)
            )
        } | Select-Object -First 1

        if ($existing) {
            $parent = $existing
            continue
        }

        $parent = Invoke-LenseeApi -Method POST -Path "/api/v1/catalog/categories" -Headers $Headers -Body @{
            name = $name
            parentId = if ($parent) { $parent.id } else { $null }
        }
    }

    return $parent
}

function Get-OrCreateBrand {
    param(
        [string]$Name,
        [hashtable]$Headers
    )

    $brands = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/brands" -Headers $Headers
    $existing = $brands | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if ($existing) {
        return $existing
    }

    return Invoke-LenseeApi -Method POST -Path "/api/v1/catalog/brands" -Headers $Headers -Body @{ name = $Name }
}

function Get-OrCreateProduct {
    param(
        [string]$Name,
        [string]$CategoryId,
        [string]$BrandId,
        [string]$ProductType = "Lens",
        [int]$PiecesPerPack,
        [string]$SellMode,
        [string]$SealedExpiryDuration = $null,
        [string]$OpenedExpiryRate = $null,
        [string]$OpenedExpiryDuration = $null,
        [string]$ClinicalParams,
        [string]$ExtendedAttributes,
        [hashtable]$Headers
    )

    $encodedSearch = [System.Uri]::EscapeDataString($Name)
    $products = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products?includeInactive=true&pageSize=100&search=$encodedSearch" -Headers $Headers
    $existing = $products.items | Where-Object { $_.name -eq $Name } | Select-Object -First 1

    $body = @{
        categoryId = $CategoryId
        brandId = $BrandId
        name = $Name
        productType = $ProductType
        expiryType = "Batch"
        sealedExpiryDuration = $SealedExpiryDuration
        openedExpiryRate = $OpenedExpiryRate
        openedExpiryDuration = $OpenedExpiryDuration
        piecesPerPack = $PiecesPerPack
        sellMode = $SellMode
        clinicalParams = $ClinicalParams
        extendedAttributes = $ExtendedAttributes
    }

    if ($existing) {
        return Invoke-LenseeApi -Method PUT -Path "/api/v1/catalog/products/$($existing.id)" -Headers $Headers -Body $body
    }

    return Invoke-LenseeApi -Method POST -Path "/api/v1/catalog/products" -Headers $Headers -Body $body
}

function New-PowerGrid {
    param(
        [decimal]$Minimum,
        [decimal]$Maximum,
        [switch]$IncludeZero
    )

    function New-AbsolutePowerValues {
        param([decimal]$UpperBound)

        $values = New-Object System.Collections.Generic.List[decimal]
        $p = [decimal]0.50
        while ($p -le $UpperBound) {
            $values.Add($p)
            $p += $(if ($p -lt [decimal]5.00) { [decimal]0.25 } else { [decimal]0.50 })
        }

        return $values
    }

    if ($IncludeZero) {
        $zero = @(@{ sign = "+"; value = [decimal]0.00 })
    }
    else {
        $zero = @()
    }

    $negative = New-AbsolutePowerValues -UpperBound ([Math]::Abs($Minimum)) |
        ForEach-Object { @{ sign = "-"; value = $_ } }

    $positive = New-AbsolutePowerValues -UpperBound $Maximum |
        ForEach-Object { @{ sign = "+"; value = $_ } }

    return @($negative + $zero + $positive)
}

function Add-Sku {
    param(
        [object]$Product,
        [string]$PowerSign,
        [Nullable[decimal]]$PowerValue,
        [string]$ColorName,
        [string]$Size,
        [hashtable]$Headers
    )

    $body = @{
        powerSign = $PowerSign
        powerValue = $PowerValue
        colorName = $ColorName
        size = $Size
        barcode = $null
    }

    try {
        Invoke-LenseeApi -Method POST -Path "/api/v1/catalog/products/$($Product.id)/skus" -Headers $Headers -Body $body | Out-Null
        return "created"
    }
    catch {
        if ($_.Exception.Message -like "*HTTP 409*") {
            return "exists"
        }

        throw
    }
}

function Disable-StaleSkus {
    param(
        [object]$Product,
        [scriptblock]$ShouldKeep,
        [hashtable]$Headers
    )

    $detail = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products/$($Product.id)" -Headers $Headers
    $deactivated = 0
    foreach ($sku in @($detail.skus)) {
        if ($sku.isActive -and -not (& $ShouldKeep $sku)) {
            Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/skus/$($sku.id)/deactivate" -Headers $Headers | Out-Null
            $deactivated++
        }
    }

    return $deactivated
}

function Disable-GlobalSeedSkuConflicts {
    param(
        [object[]]$PlainBoxes,
        [object[]]$PlainVials,
        [object[]]$ColoredPacks,
        [object]$SampleSolution,
        [string[]]$Colors,
        [string[]]$SolutionSizes,
        [hashtable]$Headers
    )

    $page = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products?includeInactive=true&pageSize=1000" -Headers $Headers
    $keepIds = @($PlainBoxes + $PlainVials + $ColoredPacks + @($SampleSolution) | ForEach-Object { $_.id })
    $deactivated = 0
    foreach ($product in @($page.items)) {
        if ($keepIds -contains $product.id -or $product.productType -ne "Lens") {
            continue
        }

        $detail = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products/$($product.id)" -Headers $Headers
        foreach ($sku in @($detail.skus)) {
            if ($sku.isActive) {
                Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/skus/$($sku.id)/deactivate" -Headers $Headers | Out-Null
                $deactivated++
            }
        }

        if ($product.isActive) {
            Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/products/$($product.id)/deactivate" -Headers $Headers | Out-Null
        }
    }

    return $deactivated
}
Write-Host "Logging in to $ApiBaseUrl as $Username..."
$auth = Invoke-LenseeApi -Method POST -Path "/api/v1/auth/login" -Body @{
    username = $Username
    password = $Password
}

$headers = @{ Authorization = "Bearer $($auth.accessToken)" }

Write-Host "Ensuring categories and brands..."
$medicalCategory = Get-OrCreateNestedCategory -Path @("Lenses", "Medical Lenses") -Headers $headers
$coloredCategory = Get-OrCreateNestedCategory -Path @("Lenses", "Colored Lenses") -Headers $headers
$solutionCategory = Get-OrCreateNestedCategory -Path @("Solution") -Headers $headers
$clearVision = Get-OrCreateBrand -Name "Clear Vision" -Headers $headers

function Disable-SupersededLensProducts {
    param(
        [string[]]$KeepProductIds,
        [hashtable]$Headers
    )

    $page = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products?includeInactive=true&pageSize=1000" -Headers $Headers
    $deactivated = 0
    foreach ($product in @($page.items)) {
        $attributes = $product.extendedAttributes
        if ($attributes -is [string]) {
            try { $attributes = $attributes | ConvertFrom-Json } catch { $attributes = $null }
        }
        $seededLens =
            $product.productType -eq "Lens" -and (
                $attributes.seed -in @("medical-lenses-http", "Transparent-lenses-direct-db") -or
                $product.name -like "Plain Medical Lens Box - *" -or
                $product.name -like "Plain Medical Lens Vial - *" -or
                $product.name -like "Plain Transparent Lens Box - *" -or
                $product.name -like "Plain Transparent Lens Vial - *" -or
                $product.name -like "Clear Vision Colored Lens Pack - *"
            )
        if (-not $seededLens -or $KeepProductIds -contains $product.id) {
            continue
        }

        $detail = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products/$($product.id)" -Headers $Headers
        foreach ($sku in @($detail.skus)) {
            if ($sku.isActive) {
                Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/skus/$($sku.id)/deactivate" -Headers $Headers | Out-Null
                $deactivated++
            }
        }

        if ($product.isActive) {
            Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/products/$($product.id)/deactivate" -Headers $Headers | Out-Null
        }
    }

    return $deactivated
}

Write-Host "Ensuring products..."
$plainBoxValidityProducts = @(
    Get-OrCreateProduct `
        -Name "Clear Vision Transparent Lenses BOX3 6 months (monthly)" `
        -CategoryId $medicalCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 3 `
        -SellMode "SealedPackOnly" `
        -SealedExpiryDuration "6 months" `
        -OpenedExpiryRate "Monthly" `
        -OpenedExpiryDuration "6 months" `
        -ClinicalParams '{"powerRange":"plainTransparent","packaging":"Box","duration":"monthly"}' `
        -ExtendedAttributes '{"seed":"medical-lenses-http","packageCode":"BOX3","validity":"6 months"}' `
        -Headers $headers
)

$plainVialValidityProducts = @(
    Get-OrCreateProduct `
        -Name "Clear Vision Transparent Lenses VIAL1 ANNUALLY (1 year)" `
        -CategoryId $medicalCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 1 `
        -SellMode "SealedPackOnly" `
        -SealedExpiryDuration "1 year" `
        -OpenedExpiryRate "Annual" `
        -OpenedExpiryDuration "1 year" `
        -ClinicalParams '{"powerRange":"plainTransparent","packaging":"Vial","duration":"yearly"}' `
        -ExtendedAttributes '{"seed":"medical-lenses-http","packageCode":"VIAL1","validity":"1 year"}' `
        -Headers $headers
)

$coloredValidityProducts = @(
    Get-OrCreateProduct `
        -Name "Clear Vision Colored Lenses BOX2 9 months (monthly)" `
        -CategoryId $coloredCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 2 `
        -SellMode "SinglePiece" `
        -SealedExpiryDuration "9 months" `
        -OpenedExpiryRate "Monthly" `
        -OpenedExpiryDuration "9 months" `
        -ClinicalParams '{"powerRange":"coloredTransparent","duration":"9 months"}' `
        -ExtendedAttributes '{"seed":"medical-lenses-http","packageCode":"BOX2","validity":"9 months"}' `
        -Headers $headers
)
$sampleSolution = Get-OrCreateProduct `
    -Name "Clear Vision Multi-Purpose Solution" `
    -CategoryId $solutionCategory.id `
    -BrandId $clearVision.id `
    -ProductType "Solution" `
    -PiecesPerPack 1 `
    -SellMode "SinglePiece" `
    -ClinicalParams $null `
    -ExtendedAttributes '{"seed":"medical-lenses-http","sample":true}' `
    -Headers $headers

$colors = @(
    "Honey",
    "Sunset",
    "Galaxy Gray",
    "Ocean Gray",
    "Blue",
    "True Sapphire",
    "Green",
    "Hazel",
    "B Hazel",
    "Gray",
    "Green 2T",
    "Gray 2T",
    "Marine",
    "Jewel Brown",
    "Pistachio",
    "Brown",
    "Golden Yellow",
    "Emma Gray",
    "Selena Gray",
    "Rachel Gray",
    "Misty Gray"
)

$created = 0
$existing = 0
$deactivated = 0
$deactivated += Disable-SupersededLensProducts -KeepProductIds @($plainBoxValidityProducts[0].id, $plainVialValidityProducts[0].id, $coloredValidityProducts[0].id) -Headers $headers
$deactivated += Disable-StaleSkus -Product $plainBoxValidityProducts[0] -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Box 3" }
$deactivated += Disable-StaleSkus -Product $plainVialValidityProducts[0] -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Vial 1" }
$deactivated += Disable-StaleSkus -Product $coloredValidityProducts[0] -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-CL-") -and $colors -contains $sku.colorName -and $sku.size -eq "Pack 2" }

Write-Host "Creating transparent lens validity SKUs..."
$plainPowers = New-PowerGrid -Minimum -20.00 -Maximum 10.00
$medicalValiditySkuSpecs = @()
$medicalValiditySkuSpecs += $plainBoxValidityProducts | ForEach-Object { @{ product = $_; size = "Box 3" } }
$medicalValiditySkuSpecs += $plainVialValidityProducts | ForEach-Object { @{ product = $_; size = "Vial 1" } }
foreach ($power in $plainPowers) {
    foreach ($spec in $medicalValiditySkuSpecs) {
        $result = Add-Sku `
            -Product $spec.product `
            -PowerSign $power.sign `
            -PowerValue $power.value `
            -ColorName "Plain" `
            -Size $spec.size `
            -Headers $headers
        if ($result -eq "created") { $created++ } else { $existing++ }
    }
}

Write-Host "Creating Clear Vision colored lens SKUs..."
$coloredPowers = New-PowerGrid -Minimum -10.00 -Maximum 10.00 -IncludeZero
foreach ($color in $colors) {
    foreach ($power in $coloredPowers) {
        $result = Add-Sku `
            -Product $coloredValidityProducts[0] `
            -PowerSign $power.sign `
            -PowerValue $power.value `
            -ColorName $color `
            -Size "Pack 2" `
            -Headers $headers
        if ($result -eq "created") { $created++ } else { $existing++ }
    }
}

Write-Host "Lens HTTP seed completed. Created: $created. Already existed: $existing. Deactivated stale: $deactivated."

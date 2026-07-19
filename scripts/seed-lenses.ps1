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
    $plainBoxIds = @($PlainBoxes | ForEach-Object { $_.id })
    $plainVialIds = @($PlainVials | ForEach-Object { $_.id })
    $coloredPackIds = @($ColoredPacks | ForEach-Object { $_.id })
    $deactivated = 0
    foreach ($product in @($page.items)) {
        $detail = Invoke-LenseeApi -Method GET -Path "/api/v1/catalog/products/$($product.id)" -Headers $Headers
        foreach ($sku in @($detail.skus)) {
            if (-not $sku.isActive) {
                continue
            }

            $seededCode =
                $sku.skuCode.StartsWith("CV-ML-") -or
                $sku.skuCode.StartsWith("CV-CL-") -or
                $sku.skuCode.StartsWith("CV-PCS-") -or
                $sku.skuCode.StartsWith("LAN-PM-")
            $seededShape =
                ($sku.colorName -eq "Plain" -and @("Box 3", "Vial 1") -contains $sku.size) -or
                ($Colors -contains $sku.colorName -and $sku.size -eq "Pack 2") -or
                ($SolutionSizes -contains $sku.size -and $null -eq $sku.colorName)

            if (-not $seededCode -and -not $seededShape) {
                continue
            }

            $keep =
                ($plainBoxIds -contains $detail.id -and $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Box 3") -or
                ($plainVialIds -contains $detail.id -and $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Vial 1") -or
                ($coloredPackIds -contains $detail.id -and $sku.skuCode.StartsWith("CV-CL-") -and $Colors -contains $sku.colorName -and $sku.size -eq "Pack 2") -or
                ($detail.id -eq $SampleSolution.id -and $sku.skuCode.StartsWith("CV-PCS-") -and $SolutionSizes -contains $sku.size)

            if (-not $keep) {
                Invoke-LenseeApi -Method PATCH -Path "/api/v1/catalog/skus/$($sku.id)/deactivate" -Headers $Headers | Out-Null
                $deactivated++
            }
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

Write-Host "Ensuring products..."
$medicalYearlySpecs = @(
    @{ label = "1 Year"; duration = "1 year"; rate = "Annual" },
    @{ label = "3 Years"; duration = "3 years"; rate = "Annual" },
    @{ label = "5 Years"; duration = "5 years"; rate = "Annual" }
)

$plainBoxValidityProducts = foreach ($spec in $medicalYearlySpecs) {
    Get-OrCreateProduct `
        -Name "Plain Medical Lens Box - $($spec.label)" `
        -CategoryId $medicalCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 3 `
        -SellMode "SealedPackOnly" `
        -SealedExpiryDuration $spec.duration `
        -OpenedExpiryRate $spec.rate `
        -OpenedExpiryDuration $spec.duration `
        -ClinicalParams '{"powerRange":"plainMedical","packaging":"Box","duration":"yearly"}' `
        -ExtendedAttributes ('{"seed":"medical-lenses-http","packageCode":"BOX3","validity":"' + $spec.duration + '"}') `
        -Headers $headers
}

$plainVialValidityProducts = foreach ($spec in $medicalYearlySpecs) {
    Get-OrCreateProduct `
        -Name "Plain Medical Lens Vial - $($spec.label)" `
        -CategoryId $medicalCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 1 `
        -SellMode "SealedPackOnly" `
        -SealedExpiryDuration $spec.duration `
        -OpenedExpiryRate $spec.rate `
        -OpenedExpiryDuration $spec.duration `
        -ClinicalParams '{"powerRange":"plainMedical","packaging":"Vial","duration":"yearly"}' `
        -ExtendedAttributes ('{"seed":"medical-lenses-http","packageCode":"VIAL1","validity":"' + $spec.duration + '"}') `
        -Headers $headers
}

$coloredMonthlySpecs = @(
    @{ label = "3 Months"; duration = "3 months"; rate = "Monthly" },
    @{ label = "6 Months"; duration = "6 months"; rate = "Monthly" },
    @{ label = "9 Months"; duration = "9 months"; rate = "Monthly" }
)

$coloredDailySpecs = @(
    @{ label = "1 Day"; duration = "1 day"; rate = "Daily" },
    @{ label = "5 Days"; duration = "5 days"; rate = "Daily" },
    @{ label = "7 Days"; duration = "7 days"; rate = "Daily" }
)

$coloredValidityProducts = foreach ($spec in @($coloredMonthlySpecs + $coloredDailySpecs)) {
    Get-OrCreateProduct `
        -Name "Clear Vision Colored Lens Pack - $($spec.label)" `
        -CategoryId $coloredCategory.id `
        -BrandId $clearVision.id `
        -ProductType "Lens" `
        -PiecesPerPack 2 `
        -SellMode "SinglePiece" `
        -SealedExpiryDuration $spec.duration `
        -OpenedExpiryRate $spec.rate `
        -OpenedExpiryDuration $spec.duration `
        -ClinicalParams ('{"powerRange":"coloredMedical","duration":"' + $spec.duration + '"}') `
        -ExtendedAttributes ('{"seed":"medical-lenses-http","packageCode":"PACK2","validity":"' + $spec.duration + '"}') `
        -Headers $headers
}

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
    "Green",
    "Green 2T",
    "Marine",
    "Blue",
    "True Sapphire",
    "Gray",
    "Galaxy Gray",
    "Selena Gray",
    "Hazel",
    "Pure Hazel",
    "Sunset",
    "Jewel Brown"
)

$created = 0
$existing = 0
$deactivated = 0
$solutionSizes = @("60ml", "120ml", "250ml", "360ml")

$deactivated += Disable-GlobalSeedSkuConflicts -PlainBoxes $plainBoxValidityProducts -PlainVials $plainVialValidityProducts -ColoredPacks $coloredValidityProducts -SampleSolution $sampleSolution -Colors $colors -SolutionSizes $solutionSizes -Headers $headers
foreach ($product in $plainBoxValidityProducts) {
    $deactivated += Disable-StaleSkus -Product $product -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Box 3" }
}
foreach ($product in $plainVialValidityProducts) {
    $deactivated += Disable-StaleSkus -Product $product -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-ML-") -and $sku.colorName -eq "Plain" -and $sku.size -eq "Vial 1" }
}
foreach ($product in $coloredValidityProducts) {
    $deactivated += Disable-StaleSkus -Product $product -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-CL-") -and $colors -contains $sku.colorName -and $sku.size -eq "Pack 2" }
}
$deactivated += Disable-StaleSkus -Product $sampleSolution -Headers $headers -ShouldKeep { param($sku) $sku.skuCode.StartsWith("CV-PCS-") -and $solutionSizes -contains $sku.size }

Write-Host "Creating medical yearly validity SKUs..."
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

Write-Host "Creating Clear Vision colored medical SKUs..."
$coloredPowers = New-PowerGrid -Minimum -10.00 -Maximum 10.00 -IncludeZero
foreach ($product in $coloredValidityProducts) {
    foreach ($color in $colors) {
        foreach ($power in $coloredPowers) {
            $result = Add-Sku `
                -Product $product `
                -PowerSign $power.sign `
                -PowerValue $power.value `
                -ColorName $color `
                -Size "Pack 2" `
                -Headers $headers
            if ($result -eq "created") { $created++ } else { $existing++ }
        }
    }
}

Write-Host "Creating Clear Vision sample solution SKUs..."
foreach ($size in $solutionSizes) {
    $result = Add-Sku `
        -Product $sampleSolution `
        -PowerSign $null `
        -PowerValue $null `
        -ColorName $null `
        -Size $size `
        -Headers $headers
    if ($result -eq "created") { $created++ } else { $existing++ }
}

Write-Host "Lens HTTP seed completed. Created: $created. Already existed: $existing. Deactivated stale: $deactivated."

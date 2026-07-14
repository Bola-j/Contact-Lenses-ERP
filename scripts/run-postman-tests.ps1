param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$AdminPassword = "Admin123!",
    [switch]$PrepareAuthData
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$collectionPath = Join-Path $repoRoot "postman/Lensee_PRD_MVP_Full.postman_collection.json"
$environmentPath = Join-Path $repoRoot "postman/Lensee_Local.postman_environment.json"
$resultsDir = Join-Path $repoRoot "postman/results"

if (-not (Test-Path -LiteralPath $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
}

if ($PrepareAuthData) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql") |
        docker compose exec -T db psql -U lensee_user -d lensee

    & (Join-Path $PSScriptRoot "bootstrap-admin.ps1") -Username admin -Password $AdminPassword
    & (Join-Path $PSScriptRoot "seed-dev-users.ps1")
}

node (Join-Path $PSScriptRoot "generate-postman-collection.mjs")

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$jsonReport = Join-Path $resultsDir "lensee-mvp-postman-$stamp.json"

npx.cmd --yes newman run $collectionPath `
    --environment $environmentPath `
    --env-var "baseUrl=$BaseUrl" `
    --env-var "adminPassword=$AdminPassword" `
    --reporters cli,json `
    --reporter-json-export $jsonReport

Write-Host "Newman JSON report: $jsonReport"

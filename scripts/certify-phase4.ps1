[CmdletBinding()]
param([switch]$SkipDocker, [switch]$SkipBrowser)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root 'artifacts/certification/phase4'
$runRoot = Join-Path $root 'tmp/certify-phase4'
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
# Never leave a previous, narrower run looking like certification while a new
# candidate is being evaluated.  The marker is recreated only after every gate.
Remove-Item -LiteralPath (Join-Path $evidence 'CERTIFIED.txt') -Force -ErrorAction SilentlyContinue
function Invoke-Gate([string]$Name, [scriptblock]$Action) {
  $log = Join-Path $evidence ($Name + '.log')
  Push-Location $root
  $previousErrorAction = $ErrorActionPreference
  try {
    # Docker/BuildKit writes progress to stderr even on success. Capture it without
    # turning diagnostic progress into a PowerShell terminating error, then enforce
    # the native process exit code explicitly.
    $ErrorActionPreference = 'Continue'
    # Keep native test hosts out of a PowerShell output pipeline. In this
    # repository the pipeline capture can terminate vstest/testhost early;
    # direct redirection preserves the complete native exit code and log.
    & $Action *> $log
    Get-Content -LiteralPath $log
    $exitCode = $LASTEXITCODE
    if ($null -ne $exitCode -and $exitCode -ne 0) { throw "$Name failed with exit code $exitCode" }
  }
  finally { $ErrorActionPreference = $previousErrorAction; Pop-Location }
}
Invoke-Gate 'frontend-syntax' { node --check frontend/app.js }
Invoke-Gate 'frontend-localization' { node scripts/check-frontend-localization.mjs }
Invoke-Gate 'backend-build' { dotnet build Lensee.slnx --no-restore --nologo -p:BaseOutputPath="$runRoot/build/" }
Invoke-Gate 'unit-contract-tests' { dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --no-restore --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/unit/" }
$oldPg = $env:LENSEE_RUN_POSTGRES_TESTS; $env:LENSEE_RUN_POSTGRES_TESTS = 'true'
try { Invoke-Gate 'postgres-tests' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/postgres/" } }
finally { $env:LENSEE_RUN_POSTGRES_TESTS = $oldPg }
$oldPg = $env:LENSEE_RUN_POSTGRES_TESTS; $env:LENSEE_RUN_POSTGRES_TESTS = 'true'
try {
  Invoke-Gate 'migration-clean-database' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~PaymentIntegrityPostgresTests.HardeningMigrations_AreAppliedToAFreshDatabase' --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/migration-clean/" }
  Invoke-Gate 'migration-upgrade-database' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~MigrationUpgradePostgresTests.UpgradeFromPriorReleaseSchema_AppliesAllModuleHardeningMigrations' --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/migration-upgrade/" }
}
finally { $env:LENSEE_RUN_POSTGRES_TESTS = $oldPg }
if (-not $SkipDocker) {
  Invoke-Gate 'docker-migrator' { docker compose --profile migrate run --rm --build migrator }
  Invoke-Gate 'docker-build' { docker compose build lensee.host frontend }
  Invoke-Gate 'docker-up' { docker compose up -d lensee.host frontend }
  Invoke-Gate 'health' {
    $healthy = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
      try {
        $health = Invoke-WebRequest http://127.0.0.1:5000/health -UseBasicParsing -TimeoutSec 3
        $ready = Invoke-WebRequest http://127.0.0.1:5000/ready -UseBasicParsing -TimeoutSec 3
        if ($health.StatusCode -eq 200 -and $ready.StatusCode -eq 200) { $healthy = $true; break }
      } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw 'health/readiness did not return 200 within 60 seconds' }
    "health=$($health.StatusCode) ready=$($ready.StatusCode)"
  }
  # Ignore startup-only health probe failures that occurred while the API was
  # waiting for PostgreSQL. The scan must cover the certified, ready window,
  # including schema and browser traffic, rather than historical container log
  # output from prior restarts.
  $apiLogScanSince = [DateTimeOffset]::UtcNow
  Invoke-Gate 'schema-history' {
    $schemaHistorySql = 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";'
    $schemaHistorySql | docker compose exec -T db psql -U lensee_user -d lensee -At
  }
  Invoke-Gate 'api-log-scan' {
    $logs = docker compose logs --no-color --since $apiLogScanSince.ToString('o') lensee.host
    $patterns = '42703', 'column .*FinanceAccountId does not exist', 'migration failed', 'Unhandled exception', '\| crit:', '\| fail:', 'Finance posting failed'
    $hits = $patterns | ForEach-Object { $logs | Select-String -Pattern $_ -CaseSensitive:$false }
    if ($hits) { $hits | Select-Object -Last 30; throw 'forbidden API error pattern found' }
    'api-log-scan=clean'
  }
}
if (-not $SkipBrowser) {
  # Phase 4 certification is intentionally scoped to Finance, Reports, Shopify,
  # payment/treasury integration, and the bilingual/reporting presentation
  # surface. The post-Phase-5 holistic run will cover the complete historical
  # Phase 1-5 browser matrix.
  Invoke-Gate 'e2e-seed' { npm run e2e:setup }
  $phase4BrowserSpecs = @(
    'e2e/payments-accounting.spec.js',
    'e2e/payments-workflow-ui.spec.js',
    'e2e/notifications-reports.spec.js',
    'e2e/localization.spec.js',
    'e2e/localization-mobile.spec.js',
    'e2e/friendly-identifiers.spec.js',
    'e2e/auth-permissions.spec.js'
  )
  Invoke-Gate 'playwright' { npx playwright test @phase4BrowserSpecs --reporter=line }
}
if ($SkipDocker -or $SkipBrowser) {
  Write-Output 'PHASE 4 PARTIAL VALIDATION PASSED: skipped gates prevent certification.'
  exit 0
}
Invoke-Gate 'evidence-manifest' {
  $manifest = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    candidate = (git rev-parse --short HEAD)
    migrationHistory = (docker compose exec -T db psql -U lensee_user -d lensee -At -c 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";' | Where-Object { $_.Trim() })
    scope = 'Phase 4 only; holistic Phase 1-5 certification is deferred until after Phase 5.'
    browserSpecs = $phase4BrowserSpecs
    requiredGates = @('frontend-syntax','frontend-localization','backend-build','unit-contract-tests','postgres-tests','migration-clean-database','migration-upgrade-database','docker-migrator','docker-build','docker-up','health','schema-history','api-log-scan','e2e-seed','playwright')
  }
  $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json')
  'evidence-manifest=written'
}
Set-Content -Path (Join-Path $evidence 'CERTIFIED.txt') -Value "Phase 4 gates passed at $([DateTime]::UtcNow.ToString('o'))"
Write-Output 'PHASE 4 CERTIFIED: all requested gates passed.'

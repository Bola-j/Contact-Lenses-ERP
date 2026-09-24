[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root 'artifacts/certification/phase5'
$runRoot = Join-Path $root 'tmp/certify-phase5'
New-Item -ItemType Directory -Force -Path $evidence, $runRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $evidence 'CERTIFIED.txt') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $evidence 'manifest.json') -Force -ErrorAction SilentlyContinue

$gateNames = [System.Collections.Generic.List[string]]::new()
function Invoke-Gate([string]$Name, [scriptblock]$Action) {
    $gateNames.Add($Name)
    $log = Join-Path $evidence ($Name + '.log')
    Push-Location $root
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Action *> $log
        Get-Content -LiteralPath $log
        $exitCode = $LASTEXITCODE
        if ($null -ne $exitCode -and $exitCode -ne 0) { throw "$Name failed with exit code $exitCode" }
    }
    finally {
        $ErrorActionPreference = $previous
        Pop-Location
    }
}

Invoke-Gate 'frontend-syntax' { node --check frontend/app.js }
Invoke-Gate 'frontend-checks' { npm run check }
Invoke-Gate 'semantic-i18n-architecture' {
    $legacyPatterns = @(
        'translateEnglishText',
        'document\.createTreeWalker',
        'new MutationObserver',
        'queuePresentationApply',
        'translatedTextSources',
        'translatedAttributeSources'
    )
    $hits = $legacyPatterns | ForEach-Object { rg -n --glob 'frontend/app.js' $_ 2>$null }
    if ($hits) {
        $hits | Select-Object -First 80
        throw 'legacy DOM/regex/TreeWalker localization path remains; semantic render-time migration is incomplete.'
    }
    # ripgrep returns 1 when no match is found; that is the passing result for
    # this negative assertion, not a failed certification gate.
    $global:LASTEXITCODE = 0
    'semantic-i18n-architecture=clean'
}
Invoke-Gate 'backend-build' { dotnet build Lensee.slnx --no-restore --nologo -p:BaseOutputPath="$runRoot/build/" }
Invoke-Gate 'unit-contract-tests' { dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --no-restore --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/unit/" }
Invoke-Gate 'phase5-package-contract' { dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --no-restore --filter 'FullyQualifiedName~FinanceEndpointContractTests' --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/phase5-package/" }

$oldPg = $env:LENSEE_RUN_POSTGRES_TESTS
$env:LENSEE_RUN_POSTGRES_TESTS = 'true'
try {
    Invoke-Gate 'postgres-tests' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/postgres/" }
    Invoke-Gate 'migration-clean-database' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~PaymentIntegrityPostgresTests.HardeningMigrations_AreAppliedToAFreshDatabase' --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/migration-clean/" }
    Invoke-Gate 'migration-upgrade-database' { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~MigrationUpgradePostgresTests.UpgradeFromPriorReleaseSchema_AppliesAllModuleHardeningMigrations' --logger 'console;verbosity=minimal' -p:BaseOutputPath="$runRoot/migration-upgrade/" }
}
finally { $env:LENSEE_RUN_POSTGRES_TESTS = $oldPg }

if (-not $SkipDocker) {
    Invoke-Gate 'docker-migrator' { docker compose --profile migrate run --rm --build migrator }
    Invoke-Gate 'docker-build' { docker compose build lensee.host frontend }
    Invoke-Gate 'docker-up' { docker compose up -d lensee.host frontend }
    $apiLogScanSince = [DateTimeOffset]::UtcNow
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
    Invoke-Gate 'schema-history' {
        docker compose exec -T db psql -U lensee_user -d lensee -At -c 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";'
    }
    Invoke-Gate 'api-log-scan' {
        $logs = docker compose logs --no-color --since $apiLogScanSince.ToString('o') lensee.host
        $patterns = '42703', 'migration failed', 'Unhandled exception', '\| crit:', '\| fail:', 'Finance posting failed', 'reconciliation.*failed'
        $hits = $patterns | ForEach-Object { $logs | Select-String -Pattern $_ -CaseSensitive:$false }
        if ($hits) { $hits | Select-Object -Last 50; throw 'forbidden API error pattern found' }
        'api-log-scan=clean'
    }
}

if (-not $SkipBrowser) {
    Invoke-Gate 'e2e-seed' { npm run e2e:setup }
    Invoke-Gate 'playwright-full' { npx playwright test --reporter=line }
}

if ($SkipDocker -or $SkipBrowser) {
    Write-Output 'PHASE 5 PARTIAL VALIDATION PASSED: skipped gates prevent certification.'
    exit 0
}

Invoke-Gate 'evidence-manifest' {
    $manifest = [ordered]@{
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        candidate = (git rev-parse HEAD)
        scope = 'Phase 5 isolated migration/reconciliation and full certification preparation'
        migrationHistory = @(docker compose exec -T db psql -U lensee_user -d lensee -At -c 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";' | Where-Object { $_.Trim() })
        requiredGates = @($gateNames)
        packageSource = 'No production package applied; signed external CSV is required for historical migration.'
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json')
    'evidence-manifest=written'
}
Set-Content -LiteralPath (Join-Path $evidence 'CERTIFIED.txt') -Value "Phase 5 gates passed at $([DateTime]::UtcNow.ToString('o'))"
Write-Output 'PHASE 5 CERTIFIED: all required isolated gates passed.'

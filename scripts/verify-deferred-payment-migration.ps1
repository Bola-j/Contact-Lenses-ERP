param(
    [string]$EnvFile = "D:\LenseeProduction\.env",
    [string]$OutputDirectory = "D:\LenseeProduction\tmp\deferred-payment-migration"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$logPath = Join-Path $OutputDirectory "verification.log"

function Write-Step {
    param([string]$Message)
    $line = "$(Get-Date -Format o) $Message"
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line
}

function Invoke-Logged {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Step "START $Name"
    & $Command *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory "$Name.log")
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
    Write-Step "PASS $Name"
}

Push-Location (Resolve-Path (Join-Path $PSScriptRoot ".."))
try {
    Invoke-Logged "docker-version" { docker version }
    Invoke-Logged "docker-compose-ps-before" { docker compose --env-file $EnvFile ps -a }
    Invoke-Logged "migrator-first-run" { docker compose --env-file $EnvFile --profile migrate run --rm --build migrator }
    Invoke-Logged "migrator-second-run" { docker compose --env-file $EnvFile --profile migrate run --rm migrator }

    $sql = @'
select 'history_count', count(*)::text
from "__EFMigrationsHistory"
where "MigrationId" = '20260922012642_AllowDeferredMerchantPaymentLogs'
union all
select 'payment_method_nullable', is_nullable
from information_schema.columns
where table_schema = 'payments'
  and table_name = 'main_payment_logs'
  and column_name = 'payment_method'
union all
select 'legacy_label_count', count(*)::text
from payments.main_payment_logs
where payment_method in ('MerchantAccount','Installment','Installlaugment')
union all
select 'check_constraint', pg_get_constraintdef(oid)
from pg_constraint
where conrelid = 'payments.main_payment_logs'::regclass
  and conname = 'chk_main_payment_method';
'@

    $sqlPath = Join-Path $OutputDirectory "verify.sql"
    Set-Content -LiteralPath $sqlPath -Value $sql -Encoding ASCII
    Invoke-Logged "schema-data-verification" {
        Get-Content -Raw -LiteralPath $sqlPath | docker compose --env-file $EnvFile exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee
    }
    Invoke-Logged "docker-compose-ps-after" { docker compose --env-file $EnvFile ps }
}
finally {
    Pop-Location
}

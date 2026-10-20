param(
    [ValidateSet('admin', 'erp', 'company')][string]$Instance = 'company',
    [int]$ApiPort = 55003,
    [int]$FrontendPort = 53003
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$container = 'lensee-shopify-qa-20261019'
$database = "lensee_shopify_qa_$Instance"
if ($database -notmatch '^lensee_shopify_qa_(admin|erp|company)$') { throw 'Invalid isolated QA target.' }
$outDir = Join-Path $repo 'artifacts/certification/shopify-20261019'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$hostDll = Join-Path $repo 'backend/Lensee.Host/bin/Debug/net8.0/Lensee.Host.dll'
if (!(Test-Path -LiteralPath $hostDll)) { throw 'Build Lensee.Host before starting QA.' }
$runtimeDir = Join-Path $outDir "$Instance-runtime-v2"
[IO.Directory]::CreateDirectory($runtimeDir) | Out-Null
Get-ChildItem -LiteralPath (Split-Path $hostDll) | Where-Object { $_.Name -ne 'tmp' } |
    Copy-Item -Destination $runtimeDir -Recurse -Force
$hostDll = Join-Path $runtimeDir 'Lensee.Host.dll'

function Invoke-QAProcess([string]$Executable, [string[]]$Arguments, [hashtable]$Variables, [string]$LogPath, [switch]$Wait) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($item in $Arguments) { $start.ArgumentList.Add($item) }
    foreach ($key in $Variables.Keys) { $start.Environment[$key] = [string]$Variables[$key] }
    # Use a log file argument only for process output; never print configured secrets.
    if ($Wait) {
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [IO.File]::WriteAllText($LogPath, $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
        if ($process.ExitCode -ne 0) { throw "QA process failed; inspect $LogPath" }
        return
    }
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -Environment $Variables -WorkingDirectory $repo `
        -WindowStyle Hidden -RedirectStandardOutput $LogPath -RedirectStandardError "$LogPath.err" -PassThru
    return $process.Id
}

$inspect = & docker inspect $container 2>$null
if ($LASTEXITCODE -ne 0) {
    $qaDbPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    & docker run -d --name $container --label 'lensee.shopify.qa=20261019' -p '127.0.0.1:15431:5432' `
        --env POSTGRES_USER=shopify_qa --env "POSTGRES_PASSWORD=$qaDbPassword" --env POSTGRES_DB=lensee_shopify_bootstrap `
        --mount 'type=volume,source=lensee-shopify-qa-20261019-data,target=/var/lib/postgresql/data' postgres:17-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create isolated QA PostgreSQL container.' }
    $inspect = & docker inspect $container
}
$state = ($inspect | ConvertFrom-Json)[0]
if ($state.Config.Labels.'lensee.shopify.qa' -ne '20261019') { throw 'Refusing to use an unrelated container.' }
if (!$state.State.Running) { & docker start $container | Out-Null }
$qaDbPassword = ($state.Config.Env | Where-Object { $_.StartsWith('POSTGRES_PASSWORD=') }) -replace '^POSTGRES_PASSWORD=', ''
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    # pg_isready can succeed during initdb before POSTGRES_DB has been created.
    & docker exec $container psql -U shopify_qa -d lensee_shopify_bootstrap -Atc 'select 1' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep -Seconds 1
}
"SELECT 'CREATE DATABASE $database' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$database')\gexec" |
    & docker exec -i $container psql -v ON_ERROR_STOP=1 -U shopify_qa -d lensee_shopify_bootstrap | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to prepare isolated QA database.' }

$variables = @{
    'ASPNETCORE_ENVIRONMENT' = 'Development'
    'ConnectionStrings__DefaultConnection' = "Host=127.0.0.1;Port=15431;Database=$database;Username=shopify_qa;Password=$qaDbPassword"
    'Database__AutoMigrate' = 'false'
    'Database__BaselineExistingSchema' = 'false'
    'Jwt__Secret' = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    'Jwt__Issuer' = 'Lensee'
    'Jwt__Audience' = 'Lensee.App'
    'Shopify__Enabled' = 'false'
    'Cors__AllowedOrigins__0' = "http://127.0.0.1:$FrontendPort"
    'Cors__AllowedOrigins__1' = "http://localhost:$FrontendPort"
    'Logging__LogLevel__Default' = 'Warning'
}
Invoke-QAProcess 'dotnet' @($hostDll, '--migrate') $variables (Join-Path $outDir "$Instance-migrate.log") -Wait

# Exactly one employee per actual role, isolated in this QA database. Never seed shared DEV.
$fixturePassword = $env:LENSEE_E2E_PASSWORD
if ([string]::IsNullOrWhiteSpace($fixturePassword)) {
    throw 'Set LENSEE_E2E_PASSWORD in the local secret facility before starting isolated Shopify QA.'
}
$salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
$key = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($fixturePassword, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
$hash = 'pbkdf2-sha256.100000.{0}.{1}' -f [Convert]::ToBase64String($salt), [Convert]::ToBase64String($key)
$sql = @"
insert into inventory.locations (id,name,location_type,is_active) values
('11111111-1111-1111-1111-111111111111','TEST Main','MainWarehouse',true),
('22222222-2222-2222-2222-222222222222','TEST SubWarehouse','SubWarehouse',true),
('33333333-3333-3333-3333-333333333333','TEST Online','Online',true) on conflict(id) do nothing;
insert into identity.users(id,username,password_hash,full_name,role,location_id,is_active,is_primary_admin) values
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1','e2e_admin','$hash','TEST Admin','Admin',null,true,true),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2','e2e_clevel','$hash','TEST CLevel','CLevel',null,true,false),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3','e2e_accountant','$hash','TEST Accountant','Accountant',null,true,false),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4','e2e_erp_admin','$hash','TEST ERPAdmin','ERPAdmin',null,true,false),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5','e2e_online_clerk','$hash','TEST Online Clerk','WarehouseClerk','33333333-3333-3333-3333-333333333333',true,false)
on conflict(id) do nothing;
"@
$sql | & docker exec -i $container psql -v ON_ERROR_STOP=1 -U shopify_qa -d $database | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to seed isolated QA employees.' }
$variables['Shopify__Enabled'] = 'true'
$webhookSecret = $env:SHOPIFY_QA_WEBHOOK_SECRET
if ([string]::IsNullOrWhiteSpace($webhookSecret)) {
    throw 'Set SHOPIFY_QA_WEBHOOK_SECRET in the local secret facility before starting isolated Shopify QA.'
}
$variables['Shopify__WebhookSecret'] = $webhookSecret
$variables['Shopify__StoreDomain'] = 'lensee-erp-qa-20261018.myshopify.com'
$variables['Shopify__OnlineLocationId'] = '33333333-3333-3333-3333-333333333333'
$variables['Shopify__ActivationAtUtc'] = '2026-10-19T00:00:00Z'
$variables['Shopify__ReconciliationEnabled'] = 'false'
$apiPid = Invoke-QAProcess 'dotnet' @($hostDll, '--urls', "http://127.0.0.1:$ApiPort") $variables (Join-Path $outDir "$Instance-api.log")
$frontVariables = @{ 'SHOPIFY_QA_API_PORT' = "$ApiPort"; 'SHOPIFY_QA_FRONTEND_PORT' = "$FrontendPort" }
$frontPid = Invoke-QAProcess 'node' @((Join-Path $repo 'scripts/serve-shopify-qa.mjs')) $frontVariables (Join-Path $outDir "$Instance-frontend.log")
@{ instance=$Instance; database=$database; container=$container; apiPid=$apiPid; frontendPid=$frontPid;
    apiUrl="http://127.0.0.1:$ApiPort"; frontendUrl="http://127.0.0.1:$FrontendPort" } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outDir "$Instance-runtime.json")
Write-Host "Isolated Shopify QA $Instance started: API :$ApiPort, frontend :$FrontendPort. No shared DEV data modified."

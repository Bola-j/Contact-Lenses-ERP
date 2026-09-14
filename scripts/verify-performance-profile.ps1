param(
    [string]$ApiContainer = "lensee_api",
    [string]$DatabaseContainer = "lensee_db",
    [string]$FrontendContainer = "lensee_web",
    [string]$ProxyContainer = ""
)

$ErrorActionPreference = "Stop"

$expected = @{
    $ApiContainer = @{ Cpu = 1000000000L; Memory = 1610612736L }
    $DatabaseContainer = @{ Cpu = 800000000L; Memory = 1610612736L }
    $FrontendContainer = @{ Cpu = 50000000L; Memory = 134217728L }
}
if ($ProxyContainer) { $expected[$ProxyContainer] = @{ Cpu = 50000000L; Memory = 67108864L } }

$containers = foreach ($name in $expected.Keys) {
    $inspect = docker inspect $name | ConvertFrom-Json | Select-Object -First 1
    if (-not $inspect) { throw "Container '$name' was not found." }
    $actual = @{ Cpu = [long]$inspect.HostConfig.NanoCpus; Memory = [long]$inspect.HostConfig.Memory }
    [pscustomobject]@{
        Container = $name
        Cpu = $actual.Cpu / 1000000000
        MemoryMiB = $actual.Memory / 1MB
        Running = $inspect.State.Running
        MatchesProfile = $actual.Cpu -eq $expected[$name].Cpu -and $actual.Memory -eq $expected[$name].Memory
    }
}

$postgresSettings = docker exec $DatabaseContainer psql -U lensee_user -d lensee -At -F '|' -c "select current_setting('shared_buffers'),current_setting('work_mem'),current_setting('maintenance_work_mem'),current_setting('max_connections'),current_setting('max_parallel_workers_per_gather');"
$apiInspect = docker inspect $ApiContainer | ConvertFrom-Json | Select-Object -First 1
$poolBounded = @($apiInspect.Config.Env | Where-Object { $_ -like "ConnectionStrings__DefaultConnection=*" }) -match "Maximum Pool Size=20"
$result = [pscustomobject]@{
    VerifiedAt = (Get-Date).ToUniversalTime().ToString("o")
    Containers = $containers
    PostgreSql = $postgresSettings
    ApiPoolMaximum20 = [bool]$poolBounded
    Passed = ($containers.MatchesProfile -notcontains $false) -and ($containers.Running -notcontains $false) -and $postgresSettings -eq "384MB|4MB|64MB|40|1" -and [bool]$poolBounded
}
$result | ConvertTo-Json -Depth 5
if (-not $result.Passed) { exit 1 }

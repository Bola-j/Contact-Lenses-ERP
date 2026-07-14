param(
    [string]$Username = "admin",
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$FullName = "Lansee Admin"
)

$ErrorActionPreference = "Stop"

if ($Password.Length -lt 8) {
    throw "Password must be at least 8 characters."
}

function New-Pbkdf2PasswordHash {
    param([Parameter(Mandatory = $true)][string]$PlainPassword)

    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    $rng.Dispose()

    $pbkdf2 = New-Object Security.Cryptography.Rfc2898DeriveBytes($PlainPassword, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $key = $pbkdf2.GetBytes(32)

    return "pbkdf2-sha256.100000.{0}.{1}" -f [Convert]::ToBase64String($salt), [Convert]::ToBase64String($key)
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

$hash = New-Pbkdf2PasswordHash -PlainPassword $Password
$usernameSql = Escape-SqlLiteral $Username
$fullNameSql = Escape-SqlLiteral $FullName
$hashSql = Escape-SqlLiteral $hash

$sql = @"
insert into identity.users (username, password_hash, full_name, role, location_id, is_active)
values ('$usernameSql', '$hashSql', '$fullNameSql', 'Admin', null, true)
on conflict (username) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = 'Admin',
    location_id = null,
    is_active = true;
"@

$sql | docker compose exec -T db psql -U lensee_user -d lensee

Write-Host "Admin user '$Username' is ready."

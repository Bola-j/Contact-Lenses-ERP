param(
    [string]$Username = "admin",
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$FullName = "Lansee Admin",
    [switch]$Primary
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

$primarySql = if ($Primary) {
@"
begin;
update identity.users set is_primary_admin = false where is_primary_admin;
insert into identity.users (username, password_hash, full_name, role, location_id, is_active, is_primary_admin)
values ('$usernameSql', '$hashSql', '$fullNameSql', 'Admin', null, true, true)
on conflict (upper(btrim(username))) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = 'Admin',
    location_id = null,
    is_active = true,
    is_primary_admin = true;
commit;
"@
} else {
@"
insert into identity.users (username, password_hash, full_name, role, location_id, is_active, is_primary_admin)
values ('$usernameSql', '$hashSql', '$fullNameSql', 'Admin', null, true, false)
on conflict (upper(btrim(username))) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = 'Admin',
    location_id = null,
    is_active = true;
"@
}

$primarySql | docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee

$primaryMessage = if ($Primary) { " as the primary Administrator" } else { "" }
Write-Host "Admin user '$Username' is ready$primaryMessage."

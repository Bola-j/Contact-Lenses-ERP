$ErrorActionPreference = "Stop"

function New-Pbkdf2PasswordHash {
    param([Parameter(Mandatory = $true)][string]$Password)

    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    $rng.Dispose()

    $pbkdf2 = New-Object Security.Cryptography.Rfc2898DeriveBytes($Password, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $key = $pbkdf2.GetBytes(32)

    return "pbkdf2-sha256.100000.{0}.{1}" -f [Convert]::ToBase64String($salt), [Convert]::ToBase64String($key)
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

$users = @(
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"; Username = "clevel"; Password = "12121212"; FullName = "C-Level Executive"; Role = "CLevel"; Location = "null" },
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"; Username = "accountant"; Password = "12121212"; FullName = "Lansee Accountant"; Role = "Accountant"; Location = "null" },
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7"; Username = "erp_admin"; Password = "12121212"; FullName = "ERP Admin"; Role = "ERPAdmin"; Location = "null" },
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"; Username = "roxy_clerk"; Password = "12121212"; FullName = "Roxy Warehouse Clerk"; Role = "WarehouseClerk"; Location = "'11111111-1111-1111-1111-111111111111'" },
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"; Username = "retail_clerk"; Password = "12121212"; FullName = "Retail Warehouse Clerk"; Role = "WarehouseClerk"; Location = "'22222222-2222-2222-2222-222222222222'" },
    @{ Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6"; Username = "online_clerk"; Password = "12121212"; FullName = "Online Warehouse Clerk"; Role = "WarehouseClerk"; Location = "'33333333-3333-3333-3333-333333333333'" }
)

$values = foreach ($user in $users) {
    $hash = Escape-SqlLiteral (New-Pbkdf2PasswordHash -Password $user.Password)
    $username = Escape-SqlLiteral $user.Username
    $fullName = Escape-SqlLiteral $user.FullName
    $role = Escape-SqlLiteral $user.Role
    "('$($user.Id)', '$username', '$hash', '$fullName', '$role', $($user.Location), true)"
}

$sql = @"
insert into identity.users (id, username, password_hash, full_name, role, location_id, is_active)
values
  $($values -join ",`n  ")
on conflict (username) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = excluded.role,
    location_id = excluded.location_id,
    is_active = excluded.is_active;
"@

$sql | docker compose exec -T db psql -U lensee_user -d lensee

Write-Host "Seeded C-Level, Accountant, and warehouse clerk users."

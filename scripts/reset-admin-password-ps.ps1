[CmdletBinding()]
param(
    [Parameter()]
    [string]$Username = "admin",

    [Parameter()]
    [string]$FullName = "Lensee Admin",

    [Parameter()]
    [switch]$NoPrimary
)

#Requires -Version 5.1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Usage {
    @"
Usage:

  .\scripts\reset-admin-password.ps1
  .\scripts\reset-admin-password.ps1 -Username admin
  .\scripts\reset-admin-password.ps1 -Username admin -FullName "Lensee Admin"
  .\scripts\reset-admin-password.ps1 -NoPrimary

Resets or creates an active Admin user in the production Docker Compose database.

The password is read securely from the terminal without echoing it,
so it is not stored in PowerShell history.
"@
}

function ConvertTo-PlainText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.SecureString]$SecureString
    )

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)

    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function New-Pbkdf2PasswordHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    $iterations = 100000
    $saltSize   = 16
    $keySize    = 32

    # Generate a cryptographically secure 16-byte random salt.
    # Uses API compatible with Windows PowerShell 5.1 / .NET Framework.
    $salt = New-Object byte[] $saltSize
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $rng.GetBytes($salt)
    }
    finally {
        $rng.Dispose()
    }

    # Match the original Python implementation exactly:
    #
    # hashlib.pbkdf2_hmac(
    #     "sha256",
    #     password,
    #     salt,
    #     100000,
    #     dklen=32
    # )
    #
    # IMPORTANT:
    # Do not use the older 3-argument Rfc2898DeriveBytes constructor.
    # It defaults to SHA-1 instead of SHA-256.

    try {
        $pbkdf2 = New-Object `
            System.Security.Cryptography.Rfc2898DeriveBytes(
                $Password,
                $salt,
                $iterations,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256
            )
    }
    catch {
        throw @"
Unable to initialize PBKDF2-HMAC-SHA256.

This script requires a Windows .NET Framework version that supports the
Rfc2898DeriveBytes SHA-256 constructor.

Error:
$($_.Exception.Message)
"@
    }

    try {
        $key = $pbkdf2.GetBytes($keySize)

        $saltBase64 = [Convert]::ToBase64String($salt)
        $keyBase64  = [Convert]::ToBase64String($key)

        return "pbkdf2-sha256.$iterations.$saltBase64.$keyBase64"
    }
    finally {
        $pbkdf2.Dispose()
    }
}

# ===========================================================================
# Validate Docker
# ===========================================================================

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error @"
Docker CLI was not found.

Make sure Docker is installed and that 'docker' is available in PATH.
"@
    exit 1
}

try {
    & docker compose version *> $null

    if ($LASTEXITCODE -ne 0) {
        throw "docker compose returned exit code $LASTEXITCODE."
    }
}
catch {
    Write-Error @"
'docker compose' is required but could not be executed.

Make sure Docker Compose is installed and available through:

    docker compose

Error:
$($_.Exception.Message)
"@
    exit 1
}

# ===========================================================================
# Verify expected deployment files
# ===========================================================================

$RequiredFiles = @(
    ".env",
    "docker-compose.yml",
    "docker-compose.prod.yml",
    "docker-compose.deploy.yml"
)

foreach ($File in $RequiredFiles) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
        Write-Error "Required file not found: $File"
        exit 1
    }
}

# ===========================================================================
# Read password securely
# ===========================================================================

$SecurePassword = $null
$SecurePasswordConfirm = $null
$Password = $null
$PasswordConfirm = $null
$PasswordHash = $null

try {
    $SecurePassword = Read-Host `
        "New password for $Username" `
        -AsSecureString

    $SecurePasswordConfirm = Read-Host `
        "Confirm password" `
        -AsSecureString

    $Password = ConvertTo-PlainText $SecurePassword
    $PasswordConfirm = ConvertTo-PlainText $SecurePasswordConfirm

    if ($Password -cne $PasswordConfirm) {
        Write-Error "Passwords do not match."
        exit 1
    }

    if ($Password.Length -lt 8) {
        Write-Error "Password must be at least 8 characters."
        exit 1
    }

    # Generate PBKDF2-HMAC-SHA256 hash before clearing plaintext passwords.
    $PasswordHash = New-Pbkdf2PasswordHash -Password $Password
}
finally {
    # Remove plaintext password references as soon as possible.
    $Password = $null
    $PasswordConfirm = $null

    $SecurePassword = $null
    $SecurePasswordConfirm = $null
}

# ===========================================================================
# Docker Compose configuration
# ===========================================================================

$DockerComposeArgs = @(
    "compose",
    "--project-name", "lenseeproduction",
    "--env-file", ".env",
    "-f", "docker-compose.yml",
    "-f", "docker-compose.prod.yml",
    "-f", "docker-compose.deploy.yml"
)

# ===========================================================================
# Primary administrator configuration
# ===========================================================================

if ($NoPrimary) {

    $PrimarySql = ""

    $PrimaryValue = "false"

    $PrimaryMessage = ""
}
else {

    $PrimarySql = @"
update identity.users
set is_primary_admin = false
where is_primary_admin;
"@

    $PrimaryValue = "true"

    $PrimaryMessage = " as the primary administrator"
}

# ===========================================================================
# SQL
# ===========================================================================

$Sql = @"
begin;

$PrimarySql

with upserted_user as (
    insert into identity.users (
        username,
        password_hash,
        full_name,
        role,
        location_id,
        is_active,
        is_primary_admin
    )
    values (
        :'username',
        :'password_hash',
        :'full_name',
        'Admin',
        null,
        true,
        :'primary_value'::boolean
    )
    on conflict (upper(btrim(username))) do update
    set
        password_hash    = excluded.password_hash,
        full_name        = excluded.full_name,
        role             = 'Admin',
        location_id      = null,
        is_active        = true,
        is_primary_admin = excluded.is_primary_admin
    returning id
)

delete from identity.refresh_tokens
where user_id in (
    select id
    from upserted_user
);

commit;
"@

# ===========================================================================
# psql arguments
# ===========================================================================

$PsqlArgs = @(
    "exec",
    "-T",
    "db",
    "psql",
    "-v", "ON_ERROR_STOP=1",
    "-v", "username=$Username",
    "-v", "full_name=$FullName",
    "-v", "password_hash=$PasswordHash",
    "-v", "primary_value=$PrimaryValue",
    "-U", "lensee_user",
    "-d", "lensee"
)

# ===========================================================================
# Execute password reset
# ===========================================================================

Write-Host ""
Write-Host "Resetting Admin user '$Username'..."

try {

    $Sql | & docker @DockerComposeArgs @PsqlArgs

    if ($LASTEXITCODE -ne 0) {
        throw "psql exited with code $LASTEXITCODE."
    }
}
catch {

    Write-Error @"
Failed to reset the Admin password.

$($_.Exception.Message)
"@

    exit 1
}
finally {

    # The password itself has already been cleared.
    # Clear the generated password hash after use as well.
    $PasswordHash = $null
}

# ===========================================================================
# Success
# ===========================================================================

Write-Host ""
Write-Host "Admin user '$Username' password reset$PrimaryMessage; existing sessions were revoked."
```

param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory = $true)][string]$ApiImage,
    [Parameter(Mandatory = $true)][string]$FrontendImage,
    [string]$ArchivePath = "artifacts/certification/candidate-images.tar",
    [string]$MetadataPath = "artifacts/certification/candidate-images.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$checkedOutSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($checkedOutSha -ne $CandidateSha) {
    throw "Checked-out commit $checkedOutSha does not match candidate image SHA $CandidateSha."
}

$resolvedArchivePath = if ([IO.Path]::IsPathRooted($ArchivePath)) { $ArchivePath } else { Join-Path $repoRoot $ArchivePath }
$resolvedMetadataPath = if ([IO.Path]::IsPathRooted($MetadataPath)) { $MetadataPath } else { Join-Path $repoRoot $MetadataPath }
if (-not (Test-Path -LiteralPath $resolvedArchivePath -PathType Leaf) -or -not (Test-Path -LiteralPath $resolvedMetadataPath -PathType Leaf)) {
    throw "Candidate image archive or metadata is missing."
}

$metadata = Get-Content -Raw -LiteralPath $resolvedMetadataPath | ConvertFrom-Json
if ($metadata.candidate_sha -ne $CandidateSha -or $metadata.api.reference -ne $ApiImage -or $metadata.frontend.reference -ne $FrontendImage) {
    throw "Candidate image metadata does not match the requested commit and references."
}
if ($ApiImage -notmatch (":" + [regex]::Escape($CandidateSha) + "$") -or $FrontendImage -notmatch (":" + [regex]::Escape($CandidateSha) + "$")) {
    throw "Candidate application image tags must both use the exact full commit SHA."
}

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedArchivePath).Hash.ToLowerInvariant()
if ($archiveHash -ne $metadata.archive_sha256) {
    throw "Candidate image archive SHA-256 does not match its build metadata."
}

& docker load --input $resolvedArchivePath | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker could not load the candidate image archive." }
foreach ($target in @(
    [pscustomobject]@{ Name = "api"; Reference = $ApiImage; ExpectedId = $metadata.api.image_id },
    [pscustomobject]@{ Name = "frontend"; Reference = $FrontendImage; ExpectedId = $metadata.frontend.image_id }
)) {
    if ($target.ExpectedId -notmatch '^sha256:[0-9a-f]{64}$') { throw "Invalid image ID in candidate metadata for $($target.Name)." }
    $actualId = (& docker image inspect --format '{{.Id}}' $target.Reference).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualId -ne $target.ExpectedId) {
        throw "Loaded $($target.Name) image ID does not match its build evidence."
    }
}

Write-Output "Candidate image archive verified for $CandidateSha."

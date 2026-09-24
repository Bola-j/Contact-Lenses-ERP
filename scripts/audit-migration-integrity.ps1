[CmdletBinding()]
param(
    [string]$OutputDirectory = "tmp/migration-integrity"
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$projects = Get-ChildItem (Join-Path $repositoryRoot 'backend') -Directory |
    Where-Object { $_.Name -eq 'Lensee.SharedKernel' -or $_.Name -like 'Lensee.Modules.*' } |
    Sort-Object Name

$rows = [System.Collections.Generic.List[object]]::new()
$contexts = [System.Collections.Generic.List[object]]::new()

foreach ($project in $projects) {
    $migrationDirectory = Join-Path $project.FullName 'Migrations'
    $dataDirectory = Join-Path $project.FullName 'Data'
    $contextFiles = if (Test-Path $dataDirectory) { Get-ChildItem $dataDirectory -Filter '*DbContext.cs' -File } else { @() }
    $snapshot = if (Test-Path $migrationDirectory) { Get-ChildItem $migrationDirectory -Filter '*ModelSnapshot.cs' -File | Select-Object -First 1 } else { $null }
    $factory = if (Test-Path $dataDirectory) { Get-ChildItem $dataDirectory -Filter '*DbContextFactory.cs' -File | Select-Object -First 1 } else { $null }

    foreach ($contextFile in $contextFiles) {
        $contextName = [regex]::Match((Get-Content -Raw $contextFile.FullName), 'class\s+(?<name>\w+DbContext)\b').Groups['name'].Value
        $contexts.Add([pscustomobject]@{
            Project = $project.Name
            DbContext = $contextName
            MigrationDirectory = if (Test-Path $migrationDirectory) { $migrationDirectory.Substring($repositoryRoot.Length + 1) } else { '' }
            ModelSnapshot = if ($snapshot) { $snapshot.Name } else { '' }
            DesignTimeFactory = if ($factory) { $factory.Name } else { '' }
        })
    }

    if (-not (Test-Path $migrationDirectory)) { continue }
    $migrationFiles = Get-ChildItem $migrationDirectory -Filter '*.cs' -File |
        Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -notlike '*DbContextModelSnapshot.cs' } |
        Sort-Object Name

    foreach ($migrationFile in $migrationFiles) {
        $source = Get-Content -Raw $migrationFile.FullName
        $baseName = $migrationFile.BaseName
        $designer = Join-Path $migrationDirectory ($baseName + '.Designer.cs')
        $designerSource = if (Test-Path $designer) { Get-Content -Raw $designer } else { '' }
        $combined = $source + "`n" + $designerSource
        $filenameId = if ($baseName -match '^(?<id>\d+_[^\.]+)$') { $Matches.id } else { '' }
        $migrationAttribute = [regex]::Match($combined, '\[Migration\("(?<id>[^"]+)"\)\]').Groups['id'].Value
        $contextAttribute = [regex]::Match($combined, '\[DbContext\(typeof\((?<context>[^\)]+)\)\)\]').Groups['context'].Value
        $className = [regex]::Match($source, 'partial\s+class\s+(?<name>\w+)\s*:\s*Migration').Groups['name'].Value
        $up = [regex]::IsMatch($source, 'protected\s+override\s+void\s+Up\s*\(')
        $down = [regex]::IsMatch($source, 'protected\s+override\s+void\s+Down\s*\(')
        $buildTarget = [regex]::IsMatch($combined, 'BuildTargetModel\s*\(')
        $upBody = [regex]::Match($source, 'Up\s*\([^\)]*\)\s*\{(?<body>[\s\S]*?)\n\s*\}').Groups['body'].Value
        $downBody = [regex]::Match($source, 'Down\s*\([^\)]*\)\s*\{(?<body>[\s\S]*?)\n\s*\}').Groups['body'].Value
        $hasOperations = $upBody -match 'migrationBuilder\.'
        $isNoOp = $up -and -not $hasOperations
        $metadataSource = if (Test-Path $designer) { 'Designer' } elseif ($migrationAttribute -and $contextAttribute) { 'EmbeddedAttributes' } else { 'Missing' }
        $issues = [System.Collections.Generic.List[string]]::new()
        if (-not (Test-Path $designer)) { $issues.Add('MissingDesigner') }
        if (-not $migrationAttribute) { $issues.Add('MissingMigrationAttribute') }
        if (-not $contextAttribute) { $issues.Add('MissingDbContextAttribute') }
        if (-not $buildTarget) { $issues.Add('MissingBuildTargetModel') }
        if ($filenameId -and $migrationAttribute -and $filenameId -ne $migrationAttribute) { $issues.Add('MigrationIdMismatch') }
        if (-not $up) { $issues.Add('MissingUp') }
        if (-not $down) { $issues.Add('MissingDown') }
        if ($isNoOp) { $issues.Add('NoOpUp') }
        $rows.Add([pscustomobject]@{
            Project = $project.Name
            DbContext = ($contexts | Where-Object Project -eq $project.Name | Select-Object -First 1).DbContext
            MigrationId = if ($migrationAttribute) { $migrationAttribute } else { $filenameId }
            MigrationName = $className
            MigrationFile = $migrationFile.Name
            DesignerFile = if (Test-Path $designer) { Split-Path $designer -Leaf } else { '' }
            MetadataSource = $metadataSource
            HasMigrationAttribute = [bool]$migrationAttribute
            HasDbContextAttribute = [bool]$contextAttribute
            HasBuildTargetModel = $buildTarget
            HasUp = $up
            HasMeaningfulUp = $hasOperations
            HasDown = $down
            FilenameMatchesMetadata = (-not $migrationAttribute -or $filenameId -eq $migrationAttribute)
            Classification = if ($metadataSource -eq 'Designer') { 'EFGeneratedOrRestored' } elseif ($metadataSource -eq 'EmbeddedAttributes') { 'ManuallyAuthoredMetadata' } else { 'MetadataDefect' }
            Issues = ($issues -join ';')
        })
    }
}

$duplicates = $rows | Group-Object MigrationId | Where-Object { $_.Count -gt 1 }
$contexts | Export-Csv (Join-Path $outputPath 'dbcontext-inventory.csv') -NoTypeInformation
$rows | Sort-Object Project, MigrationId | Export-Csv (Join-Path $outputPath 'migration-integrity-matrix.csv') -NoTypeInformation

$report = [System.Text.StringBuilder]::new()
[void]$report.AppendLine('# EF Core migration integrity inventory')
[void]$report.AppendLine()
[void]$report.AppendLine("Generated: $(Get-Date -Format o)")
[void]$report.AppendLine()
[void]$report.AppendLine('## DbContexts')
[void]$report.AppendLine()
[void]$report.AppendLine('| Project | DbContext | Migrations | Snapshot | Design-time factory |')
[void]$report.AppendLine('| --- | --- | --- | --- | --- |')
foreach ($context in $contexts) {
    [void]$report.AppendLine("| $($context.Project) | $($context.DbContext) | $($context.MigrationDirectory) | $($context.ModelSnapshot) | $($context.DesignTimeFactory) |")
}
[void]$report.AppendLine()
[void]$report.AppendLine('## Migration matrix')
[void]$report.AppendLine()
[void]$report.AppendLine('| Context | Migration ID | Metadata | Designer | Target model | Up | Down | Classification | Issues |')
[void]$report.AppendLine('| --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in ($rows | Sort-Object Project, MigrationId)) {
    [void]$report.AppendLine("| $($row.DbContext) | $($row.MigrationId) | $($row.MetadataSource) | $([bool]$row.DesignerFile) | $($row.HasBuildTargetModel) | $($row.HasMeaningfulUp) | $($row.HasDown) | $($row.Classification) | $($row.Issues) |")
}
[void]$report.AppendLine()
[void]$report.AppendLine('## Duplicate migration IDs')
[void]$report.AppendLine()
if ($duplicates) {
    foreach ($duplicate in $duplicates) { [void]$report.AppendLine("- $($duplicate.Name): $($duplicate.Count) occurrences") }
} else {
    [void]$report.AppendLine('- None')
}
$report.ToString() | Set-Content (Join-Path $outputPath 'migration-integrity-matrix.md') -NoNewline

Write-Host "Contexts: $($contexts.Count); migrations: $($rows.Count); duplicate IDs: $($duplicates.Count)"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CsvPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateRange(1, 20)]
    [int] $BatchSize = 5
)

$ErrorActionPreference = 'Stop'

function ConvertTo-SqlUnicodeLiteral([AllowNull()] [string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) {
        return 'NULL'
    }

    return "N'$($Value.Replace("'", "''"))'"
}

$rows = @(Import-Csv -LiteralPath $CsvPath)
if ($rows.Count -eq 0) {
    throw 'The CSV contains no records.'
}

$expectedColumns = @(
    'Id', 'Title', 'FilePath', 'Language', 'Category', 'Tags',
    'Content', 'Embedding', 'Description', 'RagLevel', 'PlatformId'
)
if (Compare-Object $expectedColumns @($rows[0].PSObject.Properties.Name)) {
    throw "Unexpected CSV columns. Expected: $($expectedColumns -join ', ')"
}

$duplicateIds = @($rows | Group-Object Id | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) {
    throw "Duplicate Id values found: $($duplicateIds.Name -join ', ')"
}

foreach ($row in $rows) {
    if ([int] $row.Id -le 0 -or [int] $row.PlatformId -ne 2) {
        throw "Only positive PlatformId 2 records may be exported. Invalid row: $($row.Id)"
    }
    if ([string]::IsNullOrWhiteSpace($row.Title) -or
        [string]::IsNullOrWhiteSpace($row.Content) -or
        [string]::IsNullOrWhiteSpace($row.Embedding) -or
        [string]::IsNullOrWhiteSpace($row.RagLevel)) {
        throw "Required data is missing for Id $($row.Id)."
    }

    try {
        $embedding = $row.Embedding | ConvertFrom-Json
    }
    catch {
        throw "Embedding JSON is invalid for Id $($row.Id)."
    }
    if ($embedding -isnot [System.Array] -or $embedding.Length -ne 1536) {
        throw "Embedding for Id $($row.Id) does not contain 1536 values."
    }
}

$builder = New-Object System.Text.StringBuilder
[void] $builder.AppendLine('USE [CodePilot];')
[void] $builder.AppendLine('SET NOCOUNT ON;')
[void] $builder.AppendLine('SET XACT_ABORT ON;')
[void] $builder.AppendLine('')
[void] $builder.AppendLine('BEGIN TRY')
[void] $builder.AppendLine('    BEGIN TRANSACTION;')
[void] $builder.AppendLine('')
[void] $builder.AppendLine('    CREATE TABLE #NinjaTraderRagUpdate')
[void] $builder.AppendLine('    (')
[void] $builder.AppendLine('        Id int NOT NULL PRIMARY KEY,')
[void] $builder.AppendLine('        Title nvarchar(255) NOT NULL,')
[void] $builder.AppendLine('        FilePath nvarchar(500) NULL,')
[void] $builder.AppendLine('        Language nvarchar(50) NULL,')
[void] $builder.AppendLine('        Category nvarchar(50) NULL,')
[void] $builder.AppendLine('        Tags nvarchar(255) NULL,')
[void] $builder.AppendLine('        Content nvarchar(max) NOT NULL,')
[void] $builder.AppendLine('        Embedding nvarchar(max) NOT NULL,')
[void] $builder.AppendLine('        Description nvarchar(500) NULL,')
[void] $builder.AppendLine('        RagLevel nvarchar(20) NOT NULL')
[void] $builder.AppendLine('    );')

for ($start = 0; $start -lt $rows.Count; $start += $BatchSize) {
    $end = [Math]::Min($start + $BatchSize, $rows.Count)
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('    INSERT INTO #NinjaTraderRagUpdate')
    [void] $builder.AppendLine('    (Id, Title, FilePath, Language, Category, Tags, Content, Embedding, Description, RagLevel)')
    [void] $builder.AppendLine('    VALUES')

    for ($index = $start; $index -lt $end; $index++) {
        $row = $rows[$index]
        $values = @(
            $row.Id,
            (ConvertTo-SqlUnicodeLiteral $row.Title),
            (ConvertTo-SqlUnicodeLiteral $row.FilePath),
            (ConvertTo-SqlUnicodeLiteral $row.Language),
            (ConvertTo-SqlUnicodeLiteral $row.Category),
            (ConvertTo-SqlUnicodeLiteral $row.Tags),
            (ConvertTo-SqlUnicodeLiteral $row.Content),
            (ConvertTo-SqlUnicodeLiteral $row.Embedding),
            (ConvertTo-SqlUnicodeLiteral $row.Description),
            (ConvertTo-SqlUnicodeLiteral $row.RagLevel)
        )
        $terminator = if ($index -eq $end - 1) { ';' } else { ',' }
        [void] $builder.AppendLine("    ($($values -join ', '))$terminator")
    }
}

[void] $builder.AppendLine(@"

    DECLARE @SourceCount int = (SELECT COUNT(*) FROM #NinjaTraderRagUpdate);
    DECLARE @TargetCount int = (SELECT COUNT(*) FROM dbo.Code WHERE PlatformId = 2);

    IF @SourceCount <> $($rows.Count)
        THROW 50001, 'Unexpected staging row count. Transaction cancelled.', 1;

    IF @TargetCount <> @SourceCount
        THROW 50002, 'Live PlatformId 2 row count differs from the export. Transaction cancelled.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM #NinjaTraderRagUpdate AS source
        LEFT JOIN dbo.Code AS target
          ON target.Id = source.Id AND target.PlatformId = 2
        WHERE target.Id IS NULL
    )
        THROW 50003, 'At least one exported Id is absent from live PlatformId 2 records. Transaction cancelled.', 1;

    UPDATE target
    SET
        Title = source.Title,
        FilePath = source.FilePath,
        Language = source.Language,
        Category = source.Category,
        Tags = source.Tags,
        Content = source.Content,
        Embedding = source.Embedding,
        Description = source.Description,
        RagLevel = source.RagLevel
    FROM dbo.Code AS target
    INNER JOIN #NinjaTraderRagUpdate AS source
        ON source.Id = target.Id
    WHERE target.PlatformId = 2;

    IF @@ROWCOUNT <> @SourceCount
        THROW 50004, 'Not every PlatformId 2 record was updated. Transaction cancelled.', 1;

    COMMIT TRANSACTION;

    SELECT @SourceCount AS UpdatedRecords;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@)

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [void] (New-Item -ItemType Directory -Path $outputDirectory -Force)
}

$encoding = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), $encoding)
Write-Host "Created $OutputPath with $($rows.Count) PlatformId 2 records."

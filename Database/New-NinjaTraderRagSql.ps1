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

foreach ($row in $rows) {
    if ([int] $row.PlatformId -ne 2) {
        throw "Only PlatformId 2 can be exported. Invalid row: $($row.Title)"
    }
    if ([string]::IsNullOrWhiteSpace($row.Title) -or
        [string]::IsNullOrWhiteSpace($row.Content) -or
        [string]::IsNullOrWhiteSpace($row.Embedding)) {
        throw "Required data is missing for '$($row.Title)'."
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
[void] $builder.AppendLine('    CREATE TABLE #NinjaTraderRagImport')
[void] $builder.AppendLine('    (')
[void] $builder.AppendLine('        Title nvarchar(255) COLLATE DATABASE_DEFAULT NOT NULL,')
[void] $builder.AppendLine('        FilePath nvarchar(500) COLLATE DATABASE_DEFAULT NULL,')
[void] $builder.AppendLine('        Language nvarchar(50) COLLATE DATABASE_DEFAULT NULL,')
[void] $builder.AppendLine('        Category nvarchar(50) COLLATE DATABASE_DEFAULT NULL,')
[void] $builder.AppendLine('        Tags nvarchar(255) COLLATE DATABASE_DEFAULT NULL,')
[void] $builder.AppendLine('        Content nvarchar(max) COLLATE DATABASE_DEFAULT NOT NULL,')
[void] $builder.AppendLine('        Embedding nvarchar(max) COLLATE DATABASE_DEFAULT NOT NULL,')
[void] $builder.AppendLine('        Description nvarchar(500) COLLATE DATABASE_DEFAULT NULL,')
[void] $builder.AppendLine('        RagLevel nvarchar(20) COLLATE DATABASE_DEFAULT NOT NULL,')
[void] $builder.AppendLine('        PlatformId tinyint NOT NULL')
[void] $builder.AppendLine('    );')

for ($start = 0; $start -lt $rows.Count; $start += $BatchSize) {
    $end = [Math]::Min($start + $BatchSize, $rows.Count)
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('    INSERT INTO #NinjaTraderRagImport')
    [void] $builder.AppendLine('    (Title, FilePath, Language, Category, Tags, Content, Embedding, Description, RagLevel, PlatformId)')
    [void] $builder.AppendLine('    VALUES')

    for ($index = $start; $index -lt $end; $index++) {
        $row = $rows[$index]
        $values = @(
            (ConvertTo-SqlUnicodeLiteral $row.Title),
            (ConvertTo-SqlUnicodeLiteral $row.FilePath),
            (ConvertTo-SqlUnicodeLiteral $row.Language),
            (ConvertTo-SqlUnicodeLiteral $row.Category),
            (ConvertTo-SqlUnicodeLiteral $row.Tags),
            (ConvertTo-SqlUnicodeLiteral $row.Content),
            (ConvertTo-SqlUnicodeLiteral $row.Embedding),
            (ConvertTo-SqlUnicodeLiteral $row.Description),
            (ConvertTo-SqlUnicodeLiteral $row.RagLevel),
            '2'
        )
        $terminator = if ($index -eq $end - 1) { ';' } else { ',' }
        [void] $builder.AppendLine("    ($($values -join ', '))$terminator")
    }
}

[void] $builder.AppendLine(@'

    DECLARE @Updated int = 0;
    DECLARE @Inserted int = 0;

    UPDATE target
    SET
        FilePath = source.FilePath,
        Language = source.Language,
        Category = source.Category,
        Tags = source.Tags,
        Content = source.Content,
        Embedding = source.Embedding,
        Description = source.Description,
        RagLevel = source.RagLevel
    FROM dbo.Code AS target
    INNER JOIN #NinjaTraderRagImport AS source
        ON source.PlatformId = target.PlatformId
       AND source.Title = target.Title
    WHERE target.PlatformId = 2;

    SET @Updated = @@ROWCOUNT;

    INSERT INTO dbo.Code
    (
        Title,
        FilePath,
        Language,
        Category,
        Tags,
        Content,
        Embedding,
        Description,
        RagLevel,
        PlatformId
    )
    SELECT
        source.Title,
        source.FilePath,
        source.Language,
        source.Category,
        source.Tags,
        source.Content,
        source.Embedding,
        source.Description,
        source.RagLevel,
        source.PlatformId
    FROM #NinjaTraderRagImport AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Code AS target
        WHERE target.PlatformId = source.PlatformId
          AND target.Title = source.Title
    );

    SET @Inserted = @@ROWCOUNT;

    IF (SELECT COUNT(*) FROM #NinjaTraderRagImport) <> 80
        THROW 50001, 'The staging row count is not 80. The transaction was cancelled.', 1;

    COMMIT TRANSACTION;

    SELECT
        @Updated AS UpdatedRecords,
        @Inserted AS InsertedRecords,
        (SELECT COUNT(*) FROM dbo.Code WHERE PlatformId = 2) AS TotalPlatform2Records;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
'@)

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [void] (New-Item -ItemType Directory -Path $outputDirectory -Force)
}

$encoding = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), $encoding)
Write-Host "Created $OutputPath with $($rows.Count) records."

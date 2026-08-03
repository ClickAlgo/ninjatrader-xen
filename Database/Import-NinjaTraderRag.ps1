[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CsvPath,

    [Parameter(Mandatory = $true)]
    [string] $ConnectionString
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CsvPath -PathType Leaf)) {
    throw "CSV file not found: $CsvPath"
}

$rows = @(Import-Csv -LiteralPath $CsvPath)
if ($rows.Count -eq 0) {
    throw 'The CSV contains no records.'
}

$expectedColumns = @(
    'Id',
    'Title',
    'FilePath',
    'Language',
    'Category',
    'Tags',
    'Content',
    'Embedding',
    'Description',
    'RagLevel',
    'PlatformId'
)
$actualColumns = @($rows[0].PSObject.Properties.Name)
$columnDifference = Compare-Object $expectedColumns $actualColumns
if ($columnDifference) {
    throw "Unexpected CSV columns. Expected: $($expectedColumns -join ', ')"
}

$duplicateTitles = @(
    $rows |
        Group-Object Title |
        Where-Object Count -gt 1
)
if ($duplicateTitles.Count -gt 0) {
    throw "Duplicate titles found: $($duplicateTitles.Name -join ', ')"
}

$allowedCategories = @('Indicator', 'Strategy')
foreach ($row in $rows) {
    if ([string]::IsNullOrWhiteSpace($row.Title)) {
        throw 'Every record must have a title.'
    }
    if ([string]::IsNullOrWhiteSpace($row.Content)) {
        throw "Content is missing for '$($row.Title)'."
    }
    if ([string]::IsNullOrWhiteSpace($row.Embedding)) {
        throw "Embedding is missing for '$($row.Title)'."
    }
    if ([int] $row.PlatformId -ne 2) {
        throw "'$($row.Title)' has PlatformId '$($row.PlatformId)'; only platform 2 is allowed."
    }
    if ($row.Category -notin $allowedCategories) {
        throw "'$($row.Title)' has unsupported category '$($row.Category)'."
    }
    if ($row.Title.Length -gt 255 -or
        $row.FilePath.Length -gt 500 -or
        $row.Language.Length -gt 50 -or
        $row.Category.Length -gt 50 -or
        $row.Tags.Length -gt 255 -or
        $row.Description.Length -gt 500 -or
        $row.RagLevel.Length -gt 20) {
        throw "'$($row.Title)' exceeds one or more database column limits."
    }

    try {
        $embedding = @($row.Embedding | ConvertFrom-Json)
    }
    catch {
        throw "'$($row.Title)' contains invalid embedding JSON."
    }
    if ($embedding.Count -ne 1536) {
        throw "'$($row.Title)' has $($embedding.Count) embedding values; expected 1536."
    }
}

$table = New-Object System.Data.DataTable
[void] $table.Columns.Add('Title', [string])
[void] $table.Columns.Add('FilePath', [string])
[void] $table.Columns.Add('Language', [string])
[void] $table.Columns.Add('Category', [string])
[void] $table.Columns.Add('Tags', [string])
[void] $table.Columns.Add('Content', [string])
[void] $table.Columns.Add('Embedding', [string])
[void] $table.Columns.Add('Description', [string])
[void] $table.Columns.Add('RagLevel', [string])
[void] $table.Columns.Add('PlatformId', [byte])

foreach ($source in $rows) {
    $target = $table.NewRow()
    foreach ($name in @(
        'Title',
        'FilePath',
        'Language',
        'Category',
        'Tags',
        'Content',
        'Embedding',
        'Description',
        'RagLevel')) {
        $value = [string] $source.$name
        $target[$name] = if ([string]::IsNullOrWhiteSpace($value)) {
            [DBNull]::Value
        }
        else {
            $value
        }
    }
    $target['PlatformId'] = [byte] 2
    [void] $table.Rows.Add($target)
}

$connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$connection.Open()
$transaction = $connection.BeginTransaction()

try {
    $createStage = $connection.CreateCommand()
    $createStage.Transaction = $transaction
    $createStage.CommandText = @'
CREATE TABLE #NinjaTraderRagImport
(
    Title nvarchar(255) NOT NULL,
    FilePath nvarchar(500) NULL,
    Language nvarchar(50) NULL,
    Category nvarchar(50) NULL,
    Tags nvarchar(255) NULL,
    Content nvarchar(max) NOT NULL,
    Embedding nvarchar(max) NOT NULL,
    Description nvarchar(500) NULL,
    RagLevel nvarchar(20) NOT NULL,
    PlatformId tinyint NOT NULL
);
'@
    [void] $createStage.ExecuteNonQuery()

    $bulkCopy = New-Object System.Data.SqlClient.SqlBulkCopy(
        $connection,
        [System.Data.SqlClient.SqlBulkCopyOptions]::Default,
        $transaction)
    try {
        $bulkCopy.DestinationTableName = '#NinjaTraderRagImport'
        $bulkCopy.BatchSize = 100
        $bulkCopy.BulkCopyTimeout = 120
        foreach ($column in $table.Columns) {
            [void] $bulkCopy.ColumnMappings.Add(
                $column.ColumnName,
                $column.ColumnName)
        }
        $bulkCopy.WriteToServer($table)
    }
    finally {
        $bulkCopy.Dispose()
    }

    $upsert = $connection.CreateCommand()
    $upsert.Transaction = $transaction
    $upsert.CommandTimeout = 120
    $upsert.CommandText = @'
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

SELECT @Updated AS Updated, @Inserted AS Inserted;
'@

    $reader = $upsert.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The import did not return a result.'
    }
    $updated = $reader.GetInt32(0)
    $inserted = $reader.GetInt32(1)
    $reader.Close()

    $transaction.Commit()
    Write-Host "NinjaTrader RAG import complete. Updated: $updated; Inserted: $inserted; CSV rows: $($rows.Count)."
}
catch {
    $transaction.Rollback()
    throw
}
finally {
    $transaction.Dispose()
    $connection.Dispose()
}

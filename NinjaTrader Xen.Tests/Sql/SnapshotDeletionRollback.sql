SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

CREATE TABLE #ProjectRevisions
(
    Id int NOT NULL PRIMARY KEY,
    ConversationId uniqueidentifier NOT NULL,
    SubscriberId int NOT NULL,
    Notes nvarchar(500) NULL,
    CreatedUtc datetime2 NOT NULL
);

DECLARE @ProjectId uniqueidentifier = '11111111-1111-1111-1111-111111111111';
DECLARE @SubscriberId int = 42;

INSERT INTO #ProjectRevisions (Id, ConversationId, SubscriberId, Notes, CreatedUtc)
VALUES
    (1, @ProjectId, @SubscriberId, NULL, '2026-01-01T00:00:00'),
    (2, @ProjectId, @SubscriberId, N'PINNED|Build Plan milestone', '2026-01-02T00:00:00'),
    (3, @ProjectId, @SubscriberId, N'Restored from revision 1', '2026-01-03T00:00:00'),
    (4, @ProjectId, @SubscriberId, NULL, '2026-01-04T00:00:00'),
    (5, NEWID(), @SubscriberId, NULL, '2026-01-05T00:00:00'),
    (6, @ProjectId, 99, NULL, '2026-01-06T00:00:00');

DELETE pr
FROM #ProjectRevisions AS pr
WHERE pr.ConversationId = @ProjectId
  AND pr.SubscriberId = @SubscriberId
  AND ISNULL(pr.Notes, N'') NOT LIKE N'PINNED|%'
  AND pr.Id <> (
      SELECT TOP(1) r2.Id
      FROM #ProjectRevisions AS r2
      WHERE r2.ConversationId = @ProjectId
        AND r2.SubscriberId = @SubscriberId
      ORDER BY r2.CreatedUtc DESC, r2.Id DESC
  );

IF @@ROWCOUNT <> 2
    THROW 51000, 'Bulk deletion did not delete exactly the ordinary history rows.', 1;

IF NOT EXISTS (SELECT 1 FROM #ProjectRevisions WHERE Id = 2)
    THROW 51000, 'Bulk deletion removed a pinned snapshot.', 1;

IF NOT EXISTS (SELECT 1 FROM #ProjectRevisions WHERE Id = 4)
    THROW 51000, 'Bulk deletion removed the current snapshot.', 1;

IF NOT EXISTS (SELECT 1 FROM #ProjectRevisions WHERE Id = 5)
    THROW 51000, 'Bulk deletion crossed the project scope.', 1;

IF NOT EXISTS (SELECT 1 FROM #ProjectRevisions WHERE Id = 6)
    THROW 51000, 'Bulk deletion crossed the subscriber scope.', 1;

ROLLBACK TRANSACTION;
PRINT 'Snapshot deletion rollback test passed.';

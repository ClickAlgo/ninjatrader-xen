/*
Run this only if dbo.Subscribers currently has an email-only unique constraint
or index. Review the discovered index name before applying changes.

The application identity is (Email, PlatformId), allowing the same customer
email to own both cTrader and NinjaTrader subscriptions.
*/

SELECT
    i.name AS UniqueIndexName,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes AS i
INNER JOIN sys.index_columns AS ic
    ON ic.object_id = i.object_id
   AND ic.index_id = i.index_id
INNER JOIN sys.columns AS c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID('dbo.Subscribers')
  AND i.is_unique = 1
  AND ic.is_included_column = 0
GROUP BY i.name;

/*
After dropping any obsolete UNIQUE index/constraint covering Email alone,
create this composite unique index:

CREATE UNIQUE INDEX UX_Subscribers_Email_PlatformId
    ON dbo.Subscribers (Email, PlatformId);
*/

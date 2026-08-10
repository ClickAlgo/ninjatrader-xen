IF OBJECT_ID(N'dbo.ExistingCodeProjectStates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExistingCodeProjectStates
    (
        ConversationId uniqueidentifier NOT NULL,
        SubscriberId int NOT NULL,
        Task nvarchar(80) NOT NULL,
        StateJson nvarchar(max) NOT NULL,
        UpdatedUtc datetime2 NOT NULL,
        CONSTRAINT PK_ExistingCodeProjectStates PRIMARY KEY (SubscriberId, ConversationId),
        CONSTRAINT CK_ExistingCodeProjectStates_Task
            CHECK (Task IN (N'existing-strategy', N'existing-indicator'))
    );
END;

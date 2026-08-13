IF OBJECT_ID(N'dbo.ExistingCodeProjectStates', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.CK_ExistingCodeProjectStates_Task', N'C') IS NOT NULL
        ALTER TABLE dbo.ExistingCodeProjectStates
            DROP CONSTRAINT CK_ExistingCodeProjectStates_Task;

    ALTER TABLE dbo.ExistingCodeProjectStates WITH CHECK
        ADD CONSTRAINT CK_ExistingCodeProjectStates_Task
        CHECK (Task IN (
            N'existing-strategy', N'existing-indicator',
            N'convert-strategy', N'convert-indicator'));
END;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Subscribers', 'RegistrationNetworkRisk') IS NULL
BEGIN
    ALTER TABLE dbo.Subscribers
        ADD RegistrationNetworkRisk decimal(6,2) NULL;
END;

IF COL_LENGTH('dbo.Subscribers', 'RegistrationNetworkCheckedUtc') IS NULL
BEGIN
    ALTER TABLE dbo.Subscribers
        ADD RegistrationNetworkCheckedUtc datetime2(7) NULL;
END;

IF COL_LENGTH('dbo.Subscribers', 'TrialEvaluatedUtc') IS NULL
BEGIN
    ALTER TABLE dbo.Subscribers
        ADD TrialEvaluatedUtc datetime2(7) NULL;
END;

IF COL_LENGTH('dbo.Subscribers', 'TrialDenialReason') IS NULL
BEGIN
    ALTER TABLE dbo.Subscribers
        ADD TrialDenialReason nvarchar(64) NULL;
END;

-- Preserve the already-final state of subscribers who previously received a trial.
UPDATE dbo.Subscribers
SET TrialEvaluatedUtc = COALESCE(TrialEvaluatedUtc, UpdatedUtc, CreatedUtc),
    TrialDenialReason = NULL
WHERE PlatformId = 2
  AND TrialGranted = 1
  AND TrialEvaluatedUtc IS NULL;

COMMIT TRANSACTION;

/*
    Optional, explicit Version 2 backfill.
    Default @Apply = 0 is read-only and prints the rows that can be classified with proof.
    It never changes unmatched identity failures and never requeues any row.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Apply bit = 0;
DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
DECLARE @Candidates TABLE
(
    InboxId bigint NOT NULL PRIMARY KEY,
    ResolutionCode nvarchar(100) NOT NULL
);

INSERT @Candidates (InboxId, ResolutionCode)
SELECT Inbox.InboxId,
       CASE
           WHEN Candidate.IsActive = 0 THEN N'WorkerInactive'
           WHEN Candidate.EmploymentEndDate IS NOT NULL
                AND CONVERT(date, Inbox.SourceCheckTimeLocal) > CONVERT(date, Candidate.EmploymentEndDate)
               THEN N'AttendanceAfterEmploymentEnd'
       END
FROM dbo.ZkAttendanceSyncInbox AS Inbox
CROSS APPLY
(
    SELECT COUNT_BIG(*) AS MatchCount,
           MAX(CONVERT(int, Worker.IsActive)) AS IsActive,
           MAX(Worker.EmploymentEndDate) AS EmploymentEndDate
    FROM dbo.Workers AS Worker
    WHERE Worker.AttendanceUserId = CONVERT(nvarchar(120), Inbox.SourceUserId)
       OR (Inbox.BadgeNumber IS NOT NULL AND Worker.BadgeNumber = LTRIM(RTRIM(Inbox.BadgeNumber)))
) AS Candidate
WHERE Inbox.ProcessingStatus = 'Failed'
  AND Inbox.LastError = N'WorkerIdentityNotResolved'
  AND Candidate.MatchCount = 1
  AND (Candidate.IsActive = 0 OR
       (Candidate.EmploymentEndDate IS NOT NULL AND CONVERT(date, Inbox.SourceCheckTimeLocal) > CONVERT(date, Candidate.EmploymentEndDate)));

SELECT ResolutionCode, COUNT_BIG(*) AS PreviewCount
FROM @Candidates
GROUP BY ResolutionCode
ORDER BY ResolutionCode;

IF @Apply = 1
BEGIN
    BEGIN TRANSACTION;
    UPDATE Inbox
    SET ProcessingStatus = 'Skipped',
        LastError = NULL,
        ResolutionCode = Candidates.ResolutionCode,
        ResolutionDetails = N'Classified from a prior WorkerIdentityNotResolved failure during the Version 2 upgrade.',
        ResolvedAtUtc = @NowUtc,
        ProcessingLeaseId = NULL,
        ProcessingStartedAtUtc = NULL,
        ProcessedAtUtc = NULL,
        UpdatedAtUtc = @NowUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    INNER JOIN @Candidates AS Candidates ON Candidates.InboxId = Inbox.InboxId
    WHERE Inbox.ProcessingStatus = 'Failed'
      AND Inbox.LastError = N'WorkerIdentityNotResolved';

    SELECT @@ROWCOUNT AS ClassifiedCount;
    COMMIT TRANSACTION;
END
ELSE
BEGIN
    SELECT N'Preview only. Set @Apply = 1 after review to classify exactly these rows; no rows were changed.' AS Result;
END;
GO

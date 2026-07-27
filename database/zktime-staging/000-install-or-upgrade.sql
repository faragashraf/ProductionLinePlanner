:On Error exit

PRINT N'Phase 1/3: validating target, source, permissions, object compatibility, and SQL Agent request.';
:r database/zktime-staging/000-preflight.sql

PRINT N'Phase 2/3: installing or upgrading durable staging objects. Existing inbox data and sync state are preserved.';
:r database/zktime-staging/001-create-staging-schema.sql
:r database/zktime-staging/002-create-run-procedures.sql
:r database/zktime-staging/003-create-ingestion-procedures.sql
:r database/zktime-staging/004-create-processing-procedures.sql
:r database/zktime-staging/005-record-schema-version.sql

PRINT N'Phase 3/3: installing or updating the SQL Agent job when requested. This phase is not part of the staging schema transaction.';
:r database/zktime-staging/010-create-sql-agent-job.sql

PRINT N'ZKTime staging install/upgrade completed. A new Agent job is disabled; an existing job keeps its previous enabled state.';

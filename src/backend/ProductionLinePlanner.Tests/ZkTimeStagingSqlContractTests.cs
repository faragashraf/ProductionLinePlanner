using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace ProductionLinePlanner.Tests;

public sealed class ZkTimeStagingSqlContractTests
{
    [Fact]
    public void Collector_writes_only_staging_and_never_domain_tables()
    {
        var sql = ReadSqlFiles();

        Assert.DoesNotContain("INSERT dbo.Workers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.Workers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.Workers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.AttendanceRecords", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.AttendanceRecords", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.AttendanceRecords", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Punch_identity_and_rolling_window_are_explicit_and_idempotent()
    {
        var schema = Read("database/zktime-staging/001-create-staging-schema.sql");
        var ingestion = Read("database/zktime-staging/003-create-ingestion-procedures.sql");
        var job = Read("database/zktime-staging/010-create-sql-agent-job.sql");

        Assert.Contains("UNIQUE (SourceUserId, SourceCheckTimeLocal, SourceCheckType)", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HASHBYTES(''SHA2_256''", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@RollingWindowDays int = 3", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@RollingWindowDays = 3", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE NOT EXISTS", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MAX(USERID)", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("READUNCOMMITTED", ingestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Processing_claims_only_new_rows_but_returns_complete_day_context()
    {
        var processing = Read("database/zktime-staging/004-create-processing-procedures.sql");

        Assert.Contains("ProcessingLeaseId", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProcessingStartedAtUtc < DATEADD(MINUTE, -@LeaseMinutes", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN @Claimed AS Claimed", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claimed.InboxId", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("usp_ZkInboxRequeueFailed", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("usp_ZkInboxRequeueSkipped", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolutionStatus", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WorkerIdentityNotResolved", Read("database/zktime-staging/README.md"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agent_job_has_separate_steps_failure_path_and_five_minute_schedule()
    {
        var job = Read("database/zktime-staging/010-create-sql-agent-job.sql");

        Assert.Contains("N'Start run'", job, StringComparison.Ordinal);
        Assert.Contains("N'Stage workers'", job, StringComparison.Ordinal);
        Assert.Contains("N'Stage attendance punches'", job, StringComparison.Ordinal);
        Assert.Contains("N'Complete successful run'", job, StringComparison.Ordinal);
        Assert.Contains("N'Record failed run'", job, StringComparison.Ordinal);
        Assert.Contains("@freq_subday_interval = 5", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@on_fail_step_id = 5", job, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unified_installer_is_ordered_versioned_and_keeps_agent_outside_the_schema_phase()
    {
        var install = Read("database/zktime-staging/000-install-or-upgrade.sql");

        var expectedIncludes = new[]
        {
            "000-preflight.sql", "001-create-staging-schema.sql", "002-create-run-procedures.sql",
            "003-create-ingestion-procedures.sql", "004-create-processing-procedures.sql",
            "005-record-schema-version.sql", "010-create-sql-agent-job.sql"
        };
        var previousIndex = -1;
        foreach (var include in expectedIncludes)
        {
            var index = install.IndexOf(include, StringComparison.OrdinalIgnoreCase);
            Assert.True(index > previousIndex, $"Installer include is missing or out of order: {include}");
            previousIndex = index;
        }

        Assert.Contains(":On Error exit", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ZkSyncSchemaVersions", Read("database/zktime-staging/001-create-staging-schema.sql"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF NOT EXISTS", Read("database/zktime-staging/005-record-schema-version.sql"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Version int = 2", Read("database/zktime-staging/005-record-schema-version.sql"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upgrade_contract_preserves_inbox_state_and_agent_identity()
    {
        var schema = Read("database/zktime-staging/001-create-staging-schema.sql");
        var version = Read("database/zktime-staging/005-record-schema-version.sql");
        var job = Read("database/zktime-staging/010-create-sql-agent-job.sql");

        Assert.DoesNotContain("DELETE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", version, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessingStatus = 'Pending'", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_delete_job", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_delete_schedule", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sp_update_jobstep", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF NOT EXISTS", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@enabled = 0", job, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Version_two_upgrade_is_non_destructive_and_adds_explicit_resolution_fields()
    {
        var schema = Read("database/zktime-staging/001-create-staging-schema.sql");
        var preflight = Read("database/zktime-staging/000-preflight.sql");
        var verification = Read("database/zktime-staging/020-verify-read-only.sql");

        Assert.Contains("ZkInboxResolutionResult", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolutionCode') IS NULL", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolvedAtUtc') IS NULL", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'Skipped'", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@TargetSchemaVersion int = 2", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SkippedCount", verification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Completed with skipped records", verification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolution_requeue_contract_keeps_skipped_manual_and_failed_distinct()
    {
        var processing = Read("database/zktime-staging/004-create-processing-procedures.sql");
        var optionalClassification = Read("database/zktime-staging/006-preview-or-classify-known-skipped.sql");

        Assert.Contains("ProcessingStatus = 'Failed'", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProcessingStatus = 'Skipped'", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ReasonCode nvarchar(100) = NULL", processing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DECLARE @Apply bit = 0", optionalClassification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WorkerInactive", optionalClassification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttendanceAfterEmploymentEnd", optionalClassification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operational_scripts_are_separate_and_verification_is_read_only()
    {
        Assert.True(File.Exists(FindRepoFile("database/zktime-staging/011-disable-sql-agent-job.sql")));
        Assert.True(File.Exists(FindRepoFile("database/zktime-staging/012-run-manual.sql")));
        Assert.True(File.Exists(FindRepoFile("database/zktime-staging/013-enable-sql-agent-job.sql")));
        Assert.True(File.Exists(FindRepoFile("database/zktime-staging/014-remove-sql-agent-job.sql")));
        Assert.True(File.Exists(FindRepoFile("database/zktime-staging/015-full-uninstall.sql")));
        var verify = Read("database/zktime-staging/020-verify-read-only.sql");
        Assert.DoesNotContain("UPDATE dbo.", verify, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", verify, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.", verify, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BackendProcessorObservation", verify, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_sql_batch_parses_with_the_sql_server_2016_grammar()
    {
        var parser = new TSql130Parser(initialQuotedIdentifiers: true);
        foreach (var path in Directory.GetFiles(FindRepoDirectory("database/zktime-staging"), "*.sql").OrderBy(path => path))
        {
            var sql = Regex.Replace(File.ReadAllText(path), @"(?im)^\s*:(?:setvar|on\s+error|r)\b[^\r\n]*(?:\r?\n)?", string.Empty);
            sql = Regex.Replace(sql, @"\$\([^)]+\)", "SqlCmdValue");
            var batches = Regex.Split(sql, @"(?im)^\s*GO\s*(?:--[^\r\n]*)?$");
            for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                if (string.IsNullOrWhiteSpace(batches[batchIndex])) continue;
                parser.Parse(new StringReader(batches[batchIndex]), out var errors);
                Assert.True(
                    errors.Count == 0,
                    $"{Path.GetFileName(path)} batch {batchIndex + 1}: {string.Join(" | ", errors.Select(error => $"L{error.Line},C{error.Column}: {error.Message}"))}");
            }
        }
    }

    private static string ReadSqlFiles() => string.Join(
        Environment.NewLine,
        Directory.GetFiles(FindRepoDirectory("database/zktime-staging"), "*.sql")
            .OrderBy(path => path)
            .Select(File.ReadAllText));

    private static string Read(string relativePath) => File.ReadAllText(FindRepoFile(relativePath));

    private static string FindRepoDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate repository directory: {relativePath}");
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {relativePath}");
    }
}

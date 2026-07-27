# ZKTime staging synchronization

This capability copies raw `USERINFO` identities and `CHECKINOUT` punches into durable inboxes in
the Dayoub application database. The SQL collector never writes `Workers` or `AttendanceRecords`;
the existing backend sync engines remain the only owner of matching, UPSERT, time conversion, and
daily attendance aggregation.

```text
ZKTime (read only) -> SQL Agent/manual collector -> Dayoub staging inboxes
                                                    -> backend worker sync
                                                    -> backend attendance sync
                                                    -> Workers / AttendanceRecords
                                                    -> batched SignalR invalidation
```

`CHECKTIME` remains the unmodified Egypt-local source value in staging. The backend converts it once
with `TimeZoneInfo` and the Egypt time-zone rules. Repeated collection uses a rolling three-day
window plus the unique logical punch key `(USERID, CHECKTIME, CHECKTYPE)`.

## Files and responsibilities

- `000-preflight.sql`: read-only target/source/permissions/object/Agent checks.
- `000-install-or-upgrade.sql`: the one-command SQLCMD installer; includes schema, types, procedures,
  version registration, and optionally the Agent job.
- `010-create-sql-agent-job.sql`: non-destructive idempotent Agent job upgrade. A newly created job is
  disabled; an existing job's enabled state is preserved.
- `012-run-manual.sql`: one collector run; it does not run the backend processor.
- `013-enable-sql-agent-job.sql` / `011-disable-sql-agent-job.sql`: explicit scheduling controls.
- `014-remove-sql-agent-job.sql`: removes only the named job and its unused schedule.
- `015-full-uninstall.sql`: optional destructive removal, protected by an exact confirmation value.
- `020-verify-read-only.sql`: read-only version, object, Agent, run, inbox, and processor evidence.

The scripts use the SQL Server 2016-compatible create-stub/`ALTER PROCEDURE` pattern rather than
assuming `CREATE OR ALTER` is available on every SQL Server 2016 patch level. No installer statement
resets a watermark, processing status, `Pending` row, or `Failed` row.

## Inbox processing states (schema version 2)

- `Pending`: eligible for backend processing, including an explicitly retried transient identity miss.
- `Processing`: leased by one backend batch.
- `Processed`: applied idempotently to `Workers` or the one daily `AttendanceRecord` summary. A replayed
  punch that changes no domain row is still `Processed` because its idempotency was proven.
- `Skipped`: technically valid but intentionally not applied by a documented business rule, such as
  `WorkerInactive` or `AttendanceAfterEmploymentEnd`.
- `Failed`: malformed/ambiguous source data or a technical persistence failure requiring review.

`ResolutionCode`, `ResolutionDetails`, and `ResolvedAtUtc` carry the outcome independently of
`LastError`. `LastError` remains reserved for actual failures. A missing worker produces the transient
`WorkerIdentityNotResolved` resolution while attempts remain; it becomes `Failed` only when the configured
maximum is exhausted. `Skipped` rows are never replayed automatically.

## Required SQLCMD variables

| Variable | Meaning |
|---|---|
| `TargetDatabase` | Exact Dayoub application database selected by `-d`. |
| `SourceServer` | Read-only linked-server name, or an empty string when ZKTime is local. |
| `SourceDatabase` | Exact ZKTime database name; no placeholder/default is accepted. |
| `InstallAgentJob` | `1` to install/update Agent, `0` for Express/external scheduling. |

Run all commands from the repository root because the master script resolves `:r` includes relative
to that location. Use Windows/integrated authentication or the environment's protected credential
facility; do not put usernames/passwords in these scripts or shell history.

### Read-only preflight command

```bash
cd /path/to/ProductionLinePlanner && sqlcmd -S "<dayoub-sql-server>" -d "<dayoub-database>" -E -b -v TargetDatabase="<dayoub-database>" SourceServer="<read-only-linked-server-or-empty>" SourceDatabase="<zktime-database>" InstallAgentJob="1" -i database/zktime-staging/000-preflight.sql
```

### Shell install/upgrade command

```bash
cd /path/to/ProductionLinePlanner && sqlcmd -S "<dayoub-sql-server>" -d "<dayoub-database>" -E -b -v TargetDatabase="<dayoub-database>" SourceServer="<read-only-linked-server-or-empty>" SourceDatabase="<zktime-database>" InstallAgentJob="1" -i database/zktime-staging/000-install-or-upgrade.sql
```

### PowerShell install/upgrade command

```powershell
Set-Location C:\path\to\ProductionLinePlanner; sqlcmd -S "<dayoub-sql-server>" -d "<dayoub-database>" -E -b -v TargetDatabase="<dayoub-database>" SourceServer="<read-only-linked-server-or-empty>" SourceDatabase="<zktime-database>" InstallAgentJob="1" -i database\zktime-staging\000-install-or-upgrade.sql
```

If Agent is absent or the approved scheduler is external, use `InstallAgentJob="0"`. The source must
be local to the Dayoub SQL instance or exposed through an already configured read-only linked server;
the installer never creates linked servers and never embeds source credentials.

## Test runbook

1. Back up the Test Dayoub database.
2. Run the read-only preflight command with the Test values.
3. Run the unified install/upgrade command. Keep the newly created Agent job disabled.
4. Run verification:

   ```bash
   cd /path/to/ProductionLinePlanner && sqlcmd -S "<test-server>" -d "<test-dayoub-database>" -E -b -i database/zktime-staging/020-verify-read-only.sql
   ```

5. Run one manual collector cycle:

   ```bash
   cd /path/to/ProductionLinePlanner && sqlcmd -S "<test-server>" -d "<test-dayoub-database>" -E -b -v TargetDatabase="<test-dayoub-database>" SourceServer="<linked-server-or-empty>" SourceDatabase="<zktime-database>" -i database/zktime-staging/012-run-manual.sql
   ```

6. Verify that inbox counts/source timestamps are correct and no duplicate logical keys exist.
7. Deploy the backend while it remains in Direct mode.
8. Set the Staging settings below.
9. Restart the API; schema validation must succeed before the processor starts.
10. Run verification again and confirm `Pending` drains to `Processed`, explicit business `Skipped`, or
    per-row `Failed`. Review reason-code summaries before requeueing anything.
11. Verify one batched Worker event and/or AttendanceRecord event only when that batch changed domain data.
12. Enable the periodic job only after the manual run and processor validation:

    ```bash
    cd /path/to/ProductionLinePlanner && sqlcmd -S "<test-server>" -d "<test-dayoub-database>" -E -b -i database/zktime-staging/013-enable-sql-agent-job.sql
    ```

## Production runbook

1. Take and verify a restorable Dayoub backup; record current Direct-mode settings.
2. Run the strict preflight and stop on any failure.
3. Run install/upgrade before changing backend configuration. Existing inbox/history remains intact.
4. Run the read-only verification script and archive its result.
5. Keep the job disabled and run exactly one manual collector cycle.
6. Verify worker/punch inbox uniqueness, timestamps, run status, and expected volumes.
7. Deploy the backend with Direct mode still active.
8. Switch to Staging mode and restart the API.
9. Verify worker UPSERT, daily attendance summaries, and batched SignalR refresh.
10. Only then enable the five-minute Agent job with `013-enable-sql-agent-job.sql`.

If the Agent phase fails after schema installation, the schema/version/history remains installed and
usable for manual collection. Correct Agent permissions/configuration and rerun script `010` or the
master installer; no staging rollback is required.

## Backend settings

Direct mode is the default and performs no staging schema access:

```text
AttendanceSource__Mode=Direct
```

Enable staging only after installation and manual verification:

```text
AttendanceSource__Mode=Staging
AttendanceSource__StagingProcessorEnabled=true
AttendanceSource__StagingProcessorIntervalSeconds=60
AttendanceSource__StagingBatchSize=2000
AttendanceSource__ProcessingLeaseMinutes=15
AttendanceSource__MaxProcessingAttempts=5
AttendanceSource__MaxPendingProductionDates=3
```

Selecting Staging when the required schema/version is absent fails startup with an explicit
configuration error; it never silently falls back to Direct. `StagingProcessorEnabled=false` keeps
the installed collector/inboxes but pauses backend processing.

## Recovery and rollback

Fastest safe rollback:

1. Set `AttendanceSource__Mode=Direct` and restart the API.
2. Disable collection without deleting history:

   ```bash
   cd /path/to/ProductionLinePlanner && sqlcmd -S "<server>" -d "<dayoub-database>" -E -b -i database/zktime-staging/011-disable-sql-agent-job.sql
   ```

3. Diagnose or requeue individual failed rows only after correcting their cause:

   ```sql
   EXEC dbo.usp_ZkInboxRequeueFailed @InboxType = 'Worker', @SourceUserId = 17252;
   EXEC dbo.usp_ZkInboxRequeueFailed @InboxType = 'Attendance', @SourceUserId = 17252;
   ```

   Requeue a reviewed business skip only after the underlying business condition changes (for example,
   a worker is reactivated). This is intentional and does not happen automatically:

   ```sql
   EXEC dbo.usp_ZkInboxRequeueSkipped
       @InboxType = 'Attendance',
       @SourceUserId = 3864,
       @ReasonCode = N'WorkerInactive';
   ```

### Optional, explicit Version 1 classification preview

Version 2 never silently rewrites prior failures. To preview only the old `WorkerIdentityNotResolved`
rows that can be proved to belong to an inactive or ended worker, run:

```bash
cd /path/to/ProductionLinePlanner && sqlcmd -S "<server>" -d "<dayoub-database>" -E -b -i database/zktime-staging/006-preview-or-classify-known-skipped.sql
```

The script defaults to preview. Set its local `@Apply` variable to `1` only after reviewing the counts;
it classifies only those proven rows as `Skipped` and never requeues them.

To remove only the job, run `014-remove-sql-agent-job.sql`. Full uninstall destroys inbox/history and
is intentionally separate:

```bash
cd /path/to/ProductionLinePlanner && sqlcmd -S "<server>" -d "<dayoub-database>" -E -b -v TargetDatabase="<dayoub-database>" ConfirmFullUninstall="DROP-ZKTIME-STAGING" -i database/zktime-staging/015-full-uninstall.sql
```

Full uninstall requires an approved backup and maintenance window. Normal rollback never deletes
staging data.

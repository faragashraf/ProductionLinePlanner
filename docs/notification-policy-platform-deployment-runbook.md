# Notification Policy Platform — staged production deployment runbook

This runbook covers migration `20260719213301_AddNotificationPolicyPlatform` and the platform-wide, opt-in EF Core startup migration runner. This repository task did not connect to, inspect, or modify a production database.

The notification migration is additive: it creates `NotificationPolicies` and `NotificationPolicyRecipientRules`, adds nullable `Notifications.EventKey`, and adds nullable/defaulted `Notifications.Severity`. It does not send notifications, and catalog reconciliation creates only disabled policies.

## Startup migration configuration

| Configuration key | Default in committed configuration | Purpose |
| --- | --- | --- |
| `Database:ApplyMigrationsOnStartup` | `false` | Enables the explicitly controlled startup path. |
| `Database:MigrationCommandTimeoutSeconds` | `120` | SQL command timeout for the migration operations; valid range is 1–3600. |

Equivalent environment variables are `Database__ApplyMigrationsOnStartup` and `Database__MigrationCommandTimeoutSeconds`. Production configuration commits set `ApplyMigrationsOnStartup` to `false`. Do not put connection strings or other secrets in this document, source control, or deployment logs.

The startup runner checks pending migrations before calling `Database.MigrateAsync`. When disabled it logs that automatic execution is disabled and continues normally. When enabled with no pending migration it logs that result and continues. If inspecting or applying migrations fails, it logs a non-sensitive failure message and rethrows so the backend does not start against a partially migrated schema.

Auto-apply is only for migrations that have been reviewed and approved before deployment. Do not publish a future destructive migration with `Database:ApplyMigrationsOnStartup=true`; this is a deployment policy, not a general SQL safety parser.

## Path A — recommended controlled deployment

1. Have the database owner create a named, time-stamped production backup according to the production backup policy.
2. Restore that backup to an isolated non-production instance and verify application tables and `__EFMigrationsHistory` can be read. Record the backup ID, restore verification time, SQL Server version, compatibility level, free space, and change approver in the deployment ticket.
3. Use an anonymized clone or staging database with the same migration history. Generate the reviewed SQL without a production connection string:

   ```bash
   cd /Users/ashraffarag/Repo/ProductionLinePlanner-notification-policy
   ASPNETCORE_ENVIRONMENT=Production \
   ConnectionStrings__AppDatabase='Server=(localdb)\\MSSQLLocalDB;Database=NotificationPolicyDesignTime;Trusted_Connection=True;TrustServerCertificate=True' \
   dotnet tool run dotnet-ef migrations script 20260716015121_AllowWorkerMultiStageParticipation 20260719213301_AddNotificationPolicyPlatform --idempotent \
     --context AppDbContext \
     --project src/backend/ProductionLinePlanner.Infrastructure/ProductionLinePlanner.Infrastructure.csproj \
     --startup-project src/backend/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj \
     --output database/sql2016/018-add-notification-policy-platform.sql
   ```

4. Review the script and dry run it on staging. Stop for review if it contains `DROP`, `TRUNCATE`, unqualified `DELETE`, a destructive `ALTER`, a table rebuild, or an unexpected migration range. This reviewed script should contain only the additive notification changes, constraints, indexes, and migration-history insert.
5. Have the DBA apply the approved script manually through the production change process. Do not use a developer workstation or an application instance for this recommended path. Creating indexes and constraints can take short schema-modification locks, so use a low-traffic window.
6. Deploy the backend with `Database:ApplyMigrationsOnStartup=false` (the committed default). Verify the old application can still read/write its existing inbox, then smoke test the new policy APIs: known events appear disabled; unknown events and unknown tokens are rejected; stale row versions return HTTP 409.
7. Deploy the matching frontend. Confirm the Arabic RTL Studio is visible only to `notifications.policies.manage` and users without that permission cannot reach the route.
8. Keep every policy disabled. Enable a low-risk test policy only after a separately approved business-event delivery integration exists. That delivery integration is not part of this platform.

## Path B — controlled automatic startup migration

Use this path only when the migration has received the same backup, restore, staging-clone, SQL-review, and change approval as Path A.

1. Create and verify the backup and restore as in Path A.
2. Drain or stop all but one backend instance. **Never start multiple backend instances with automatic migration enabled at the same time.** `Database.MigrateAsync` alone is not a distributed deployment lock.
3. On the one deployment instance only, set the temporary environment variables:

   ```text
   Database__ApplyMigrationsOnStartup=true
   Database__MigrationCommandTimeoutSeconds=120
   ```

4. Start that backend instance. It logs the count and reviewed IDs of pending migrations, then logs success after the migration completes. If it fails, startup fails; do not start additional instances until the cause is resolved through the approved change process.
5. Confirm success from the backend logs and `__EFMigrationsHistory`, and verify the expected notification tables/columns. Do not treat an application log alone as the database verification.
6. Set `Database__ApplyMigrationsOnStartup=false` again, or remove the temporary override so the committed `false` default applies. Restart the deployment instance with automatic migration disabled.
7. Start the remaining backend instances, then run the API smoke checks from Path A.
8. Deploy the frontend and keep notification policies disabled until explicit activation is approved.

## Monitoring and recovery

Monitor backend startup errors, database lock duration, API 4xx/409 rates, audit records, and inbox/SignalR error dashboards. Set rollback criteria before the window: failed schema verification, old-application incompatibility, excessive locking/error rates, or unexpected notification dispatch.

- There is no automatic backup and no automatic migration rollback.
- Before new-policy data is used, a DBA may roll back migration `20260719213301_AddNotificationPolicyPlatform` only after confirming no policy/rule data exists and manually reviewing its destructive `Down` operations.
- After policy/rule data exists, the normal safe recovery is to roll back the application while retaining the additive schema. Restore the verified database backup only when required by the approved incident process.
- Do not use automatic table drops as the primary recovery plan.

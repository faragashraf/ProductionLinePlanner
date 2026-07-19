# Notification Policy Platform — staged production deployment runbook

This runbook is for migration `20260719213301_AddNotificationPolicyPlatform`. It is deliberately schema-first: do **not** enable the new backend feature until the additive schema has been applied and verified. This task did not connect to, inspect, or modify a production database.

## 1. Pre-deployment backup and restore check

1. Have the database owner create a named, time-stamped production backup according to the production backup policy.
2. Restore that backup to an isolated non-production instance and verify application tables and `__EFMigrationsHistory` can be read. Do not test a restore against the production database.
3. Record the backup identifier, restore test time, SQL Server version, database compatibility level, free space, and change approver in the deployment ticket.

## 2. Clone/staging dry run

1. Use an anonymized clone or staging database with the same migration history as production.
2. Generate/review the SQL without any production connection string:

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

3. Review `database/sql2016/018-add-notification-policy-platform.sql`: it must contain only the two new tables, nullable `Notifications.EventKey`, nullable/defaulted `Notifications.Severity`, indexes, constraints, foreign keys, and migration-history insert. Stop if a reviewed script contains `DROP`, `TRUNCATE`, unqualified `DELETE`, destructive `ALTER`, table rebuild, or an unexpected migration range.
4. Apply the reviewed script to staging using the approved DBA mechanism, then run the existing deployed application against it. It must continue to read/write its current inbox because it ignores the new nullable fields and tables.

## 3. Production schema application

1. Schedule a low-traffic window: creating indexes/constraints can take short schema-modification locks even though this migration is additive.
2. The DBA must run the already reviewed, idempotent script through the approved production change process. Do not use `dotnet ef database update`, application startup, a developer workstation, or this repository task to apply it to production.
3. Verify the new migration history row, `NotificationPolicies`, `NotificationPolicyRecipientRules`, the nullable `Notifications.EventKey`, and the nullable/defaulted `Notifications.Severity`. Verify existing inbox rows still load through the old application.

## 4. Backend and API smoke test

1. Deploy the backend that contains this feature only after the schema verification succeeds. Startup reconciliation creates any missing static catalog rows as disabled; it does not dispatch notifications.
2. With an account granted only `notifications.policies.manage`, call `GET /api/admin/notification-policies` and verify the code-catalog events appear disabled.
3. Call `GET /api/admin/notification-policies/{eventKey}` for one known event; verify templates, allowed tokens, and a nonempty row version.
4. Verify an unknown event key is rejected, a bad token is rejected, and a stale row version returns HTTP 409. Do not activate any production policy during the initial API smoke test.

## 5. Frontend deployment and gradual activation

1. Deploy the matching frontend after backend smoke tests pass.
2. Verify the Arabic RTL Notification Policy Studio is visible only to `notifications.policies.manage`; confirm users without it do not see or reach the route.
3. Keep every policy disabled. Enable one low-risk test policy only after a separate business-event delivery integration is approved and released; that integration is not part of this change.
4. Monitor backend errors, database lock duration, API 4xx/409 rates, audit records, and inbox/SignalR error dashboards. Set rollback criteria before the window: unexpected old-app incompatibility, failed schema validation, elevated database lock/error rate, or unexpected notification dispatch.

## Rollback principles

- Before any new-policy data is used, a DBA may roll back migration `20260719213301_AddNotificationPolicyPlatform` only after confirming no policy/rule data exists and the Down script has been reviewed. Its `Down` drops the new tables and new inbox columns, so it is destructive by definition.
- After policy/rule data exists, roll back the application first and leave the additive tables/columns in place. This is the normal safe recovery path.
- Restore the verified database backup only when required by the approved incident process. Dropping the new tables is not the primary recovery plan.

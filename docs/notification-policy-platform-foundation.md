# Notification Policy Platform Foundation

## Scope and decision

Notification event definitions are product-controlled and static in code. The administration surface can configure only known events; it cannot create, rename, or delete event definitions. This foundation deliberately does not publish business events, refresh screens, implement chat or messaging, or add Web Push.

The foundation separates these responsibilities:

- `NotificationEventCatalog`: known event keys, allowed tokens, and safe disabled defaults.
- `NotificationPolicyEngine`: validates and evaluates one known event policy.
- `NotificationTemplateResolver`: accepts `{TokenName}` placeholders only and rejects malformed, unknown, or missing tokens.
- `NotificationRecipientResolver`: unions active recipients selected by user, active role, effective permission, capability group, or creator, then applies actor exclusion.
- `NotificationSoundPolicy`, `NotificationToastPolicy`, and `NotificationInboxPolicy`: independent channel decisions.
- Admin foundation contract: exposes the fixed catalog and supported options under a dedicated permission, without persistence or mutation endpoints.

All catalog defaults are disabled. Existing inbox and SignalR transport remain unchanged and are not called by the policy engine in this phase.

## Discovery summary

The repository already contains a durable `Notification` inbox entity, owner-scoped inbox/read APIs, unread counts, an idempotent persist-before-dispatch publisher, authenticated SignalR user delivery, ephemeral permission capability groups, frontend inbox state, badge/toast presentation, and a default Web Audio sound. The notification page itself still contains mock rows and is not connected to the durable inbox.

Before this foundation, there was no notification event catalog, persisted policy model, policy engine, token template resolver, recipient-rule resolver, shared backend severity model, or Notification Policy Studio settings API. Existing sound settings are in-memory frontend presentation extension points and include a volume value; they are not an administration policy or persisted user preference.

## Evaluation flow

1. A future business integration submits a code-known event key and token values.
2. A future policy repository loads the corresponding persisted policy.
3. The policy engine rejects unknown events and invalid severity/channel definitions.
4. Disabled events short-circuit without resolving templates or recipients.
5. The template resolver validates placeholders against that event's code-owned allowed-token list.
6. The recipient resolver selects active users and applies effective permission grants/denies and actor exclusion.
7. The engine returns rendered content, severity, recipients, and independent sound/toast/inbox decisions.
8. A future orchestrator will persist inbox deliveries before dispatching live presentation. That integration is intentionally out of scope here.

## Persistence implementation — NOTIFY-002

The approved V1 schema remains intentionally small. Arabic templates live directly on the policy because every current event has one title/message pair and one live presentation policy. A separate template table would add joins, lifecycle rules, and migration complexity before English, per-channel content, or reusable templates are actually shipped. When either language or channel-specific content becomes real, a new additive `NotificationPolicyTemplates` table can be introduced and backfilled from these two columns.

`Toast`, `Inbox`, and `Sound` are explicit policy flags. A general channels table is deferred: it has no current consumer and would make a simple Studio edit unnecessarily relational. `SoundKey` is nullable when disabled and is constrained to the single V1 value `default` when enabled. Future channels, multiple sounds, and user preferences can be added through new tables/columns without changing the static event catalog or current API contracts.

There is no policy version table in V1. `RowVersion` provides optimistic edit concurrency and the audit log records every update. Version history should be added only if rollback/draft workflows become a product requirement. Factory/line scope is also deliberately deferred; future scope can be modeled by an additive scope table, rather than encoding a conditional rule engine now.

### `NotificationPolicies`

| Column | Type | Rules |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Application-generated primary key |
| `EventKey` | `nvarchar(100)` | Unique; must resolve in the static code catalog |
| `IsEnabled`, `IsToastEnabled`, `IsInboxEnabled`, `IsSoundEnabled` | `bit` | Required policy flags; catalog reconciliation always creates disabled policies |
| `Severity` | `int` | Required `Information`, `Success`, `Warning`, or `Critical` |
| `SoundKey` | `nvarchar(50) NULL` | `NULL` when sound is disabled; otherwise only `default` |
| `TitleTemplateAr`, `MessageTemplateAr` | `nvarchar(200)`, `nvarchar(2000)` | Required token-only Arabic templates |
| `CreatedByUserId`, `UpdatedByUserId` | `uniqueidentifier NULL` | Restricted foreign keys to `AppUsers` |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | Required audit timestamps |
| `RowVersion` | `rowversion` | Optimistic concurrency token |

`EventKey` has a unique index and `UpdatedAtUtc` has a lookup index. SQL constrains the sound shape, while catalog membership and template tokens are validated in the application because the catalog is static code, not database data.

### `NotificationPolicyRecipientRules`

Rules are attached by `NotificationPolicyId`, use an application-generated GUID primary key, and support `User`, `Role`, `Permission`, `CapabilityGroup`, `Creator`, and `ExcludeActor`. A SQL check constraint permits only the target fields applicable to the selected kind. User and role foreign keys are restricted; the policy-to-rule relationship cascades only when an administrator deliberately removes a policy record outside this API. A unique `(NotificationPolicyId, SortOrder)` index preserves Studio order, and individual user/role indexes support target validation/diagnostics. Each rule has required created/updated timestamps; policy-level actor metadata and the audit log identify who changed the complete rule set.

### Existing `Notifications` additions and seed strategy

The migration adds `Notifications.EventKey nvarchar(100) NULL` and an index, plus `Notifications.Severity int NULL DEFAULT 0`. Both are additive and old rows remain valid: a null event key is treated as legacy and null severity is presented as `Information`. Toast/sound decisions are not recorded in historical inbox rows.

There is no `HasData` seed and no database-owned event catalogue. On a new backend startup after schema application, idempotent catalog reconciliation inserts exactly one disabled policy for every static code event that is missing. It never treats an unknown database key as an executable product event, and it never sends a notification.

### Administration and security

- `GET /api/admin/notification-policies`, `GET /{eventKey}`, and `GET /recipient-options` read only catalog-backed policies and selectable recipients.
- `PUT /{eventKey}` updates the policy and its rule set atomically; `PUT /{eventKey}/recipient-rules` is provided for the project’s separate-rule editing convention.
- No route creates or deletes an event; every route requires `notifications.policies.manage`.
- Updates validate event keys, severity, the default-only sound key, templates/tokens, recipient shape/existence, maximum rule count, and row-version concurrency. Audit records include configuration metadata and rule count, never template contents or rendered token values.

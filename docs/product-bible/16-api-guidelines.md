# 16 - API Guidelines

## Why

- API غير متوافق على نمط permission-driven واضح.
- **Approved**: تعريف عقود ثابتة قابلة لاختبار صلاحية وdrift.

## Scope

- Contract rules لـ REST APIs عبر domain contexts.
- سياسة authz، errors، idempotency، pagination، و versioning.

## Ownership

- Backend architecture owners + API guild.
- Reviews قبل merge لأي endpoint جديد.

## Source of Truth

- **Confirmed**: `Program.cs` يحتوي معظم endpoints في minimal APIs.
- **Confirmed**: `/api/identity/placeholder` موجود ويشير legacy behavior.
- **Approved**: نقل جميع المجموعات إلى permission-based policies.

## Entities

- لا تخصيصات كود جديدة.
- API DTOs to follow new models:
  - `WorkerDto`
  - `WorkerSalaryHistoryDto`
  - `ProductModelDto`
  - `ProductionOutputDto`
  - `PermissionAuditDto`

## Business Rules

- كل endpoint تعديل:
  - authorization explicit.
  - actor validation.
  - error mapping via `MapFailureStatusCode`.
- endpoints للقراءة:
  - response shape موحد `ApiResponse<T>`.
- write endpoints:
  - `POST`/`PATCH` لا تنفذ duplicate create.
- avoid hidden side effects في GET.

## Validation

- IDs must be guid.
- pagination: page >=1 and pageSize <= 200.
- dates UTC only for historical records.
- DTO required fields strict.

## Permissions

- كل endpoint document permission in route metadata.
- مثال:
  - `GET /api/workers` => `workers.view`
  - `PATCH /api/workers/{id}` => `workers.manage`
  - `POST /api/compensation/import` => `compensation.import`
  - `GET /api/auth/me` => `Authenticated` + returns effective permissions

## Audit

- جميع write endpoints يجب إرفاق audit.
- audit metadata:
  - actor, before, after, route, version.

## API Direction

- Add extension methods:
  - `RequirePermission("...")` wrapper.
  - `RequirePermissionsAny(...)` for OR semantics.
  - `RequirePermissionsAll(...)`.
- Use dynamic policy provider when needed:
  - avoids generating dozens of hard-coded policies.
- Keep login/refresh unchanged in interface:
 - login returns access/refresh and base permissions claims.
 - `/api/auth/me` returns effective permissions + roles + meta.

## UI direction

- Frontend uses permission metadata from me endpoint for local guard decisions only.

## Failure Handling

- 401 for unauthorized token missing/invalid.
- 403 for valid token lacking permission.
- 409 for conflict (salary periods / unique constraints).
- 422 أو 400 validation.

## Deferred Work

- GraphQL/multi-version APIs (Deferred).
- webhook push endpoints للـattendance.

## Risks

- كثرة policies without naming policy could lead to typos. حل: policy-name generator + tests.

## Acceptance Criteria

- وجود خريطة endpoint->permission داخل `16-api-guidelines` مع أمثلة.
- 100% sensitive endpoints محمية.
- error contract متسق.

## Alternatives Considered

- **Rejected**: role-gated endpoints فقط.
- **Rejected**: hardcoded permission strings داخل controllers دون enum constants.

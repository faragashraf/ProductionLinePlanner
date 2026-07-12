# 13 - Identity and Access Management

## Why

- المنصة تحتاج إدارة صلاحيات منضبطة على مستوى capability بدل role strings.
- **Approved**: تحويل الأذونات من role-based جزئي إلى permission-based مع deny override.

## Scope

- نمذجة Users/Roles.
- Catalog للصلاحيات + assignments.
- User overrides (Grant/Deny).
- Effective permissions flow عبر backend + frontend.
- حماية endpoint/route/component.

## Ownership

- Backend: IAM/Authorization Capability.
- Frontend: `PermissionService`, `PermissionGuard`, directives.
- Security/AppDb: `AppUser`, `AppRole`, Permission tables.

## Source of Truth

- **Approved**:
  - `PermissionCatalog` ثابت في الكود (enum-like) مع optional seed.
  - `RolePermission` و`UserPermissionOverride` من DB.
  - `AppUser`/`AppRole` موجودة حاليا.
- **Planned**:
  - `SecurityStamp`/`PermissionsVersion` في `AppUser`.
  - Dynamic policy provider + caching.

## Entities

- Approved:
  - `AppUser` (`src/backend/ProductionLinePlanner.Domain/Entities/AppUser.cs`).
  - `AppRole` (`src/backend/ProductionLinePlanner.Domain/Entities/AppRole.cs`).
  - `Permission` (Planned).
  - `RolePermission` (Planned).
  - `UserPermissionOverride` (Planned, with Grant/Deny enum).
  - `PermissionAuditEntry` (Planned).
- Confirmed:
  - `UserRole` enum.

## Business Rules

- كل مستخدم له roles متعددة.
- لكل دور صلاحيات من `RolePermission`.
- يمكن grant/deny مباشر لمستخدم.
- User Deny أعلى أولوية من role grants.
- SuperAdmin يمكنه إدارة صلاحيات `permissions.assign` و`users.manage`:
  - لا يمكن حذف آخر superadmin.
- لا يسمح بإنشاء permission ad-hoc من UI.

## Validation

- رفض أي permission name غير موجود في catalog.
- منع تكرار grant/deny متضاد لمستخدم + role.
- منع حذف دور مستخدم active إذا يترك النظام بلا صلاحية `users.manage` أو `permissions.assign`.
- token refresh flow يتحقق من `PermissionsVersion` (Planned).

## Permissions Catalog (Proposed)

مقترح مرجعي مطابق:

- `workers.view`
- `workers.manage`
- `workers.export`
- `departments.view`
- `departments.manage`
- `attendance.view`
- `attendance.sync`
- `factory-structure.view`
- `factory-structure.manage`
- `compensation.view`
- `compensation.manage`
- `compensation.import`
- `compensation.export`
- `stages.view`
- `stages.manage`
- `stages.import`
- `stages.export`
- `models.view`
- `models.manage`
- `production.view`
- `production.record`
- `production.approve`
- `users.view`
- `users.manage`
- `roles.view`
- `roles.manage`
- `permissions.assign`
- `audit.view`

`factory-structure.*` تخص المصانع وخطوط الإنتاج وبنية المصنع، وهي مختلفة عن `production.*` الخاصة بالتنفيذ اليومي. قراءة factories/production lines تستخدم `factory-structure.view`، بينما POST/PUT/PATCH/DELETE تستخدم `factory-structure.manage`.

## Audit

- كل تعديل role/permission/user override => `AuditLog` + event metadata.
- Login/logout/refresh tokens already audited partially عبر `AuditActionType`.

## API Direction

- **Current (Confirmed)**:
  - `/api/auth/login`
  - `/api/auth/refresh`
  - `/api/auth/me` (يرجع roles + permissions حاليا من mapping ثابت)
- **Approved**:
  - `GET /api/permissions/catalog`
  - `GET /api/roles` / `POST /api/roles`
- `PATCH /api/users/{id}/roles`
- `PATCH /api/users/{id}/permissions`
- `POST /api/users/{id}/permissions/test` (for dry-run impact)
- جميع هذه endpoints تحتاج permission: `permissions.assign` أو `roles.manage`.

## UI Direction

- صفحة Administration:
  - Users: عرض roles + overrides + effective permissions.
- Roles: create/edit + assignment matrix.
- Permissions:
  - عرض catalog ثابت + descriptions ثنائية اللغة.
- Sidebar وroute filtering عبر same PermissionService.

## Failure Handling

- إذا فقد المستخدم صلاحية أثناء session:
  - `401` عند token غير صالح.
  - `403` عند نفاد صلاحية مسار محدد حسب cache state.
- deny user override يغطي أي grant role conflict تلقائيًا.

## Deferred Work

- Admin audit approval workflow before permission changes.
- Delegated ownership by department/line scopes (ABAC extension).

## Risks

- token الكبير إذا حُمّلت كل صلاحيات المستخدم بدل versioning.
- race condition عند تحديث roles أثناء جلسة طويلة.
- إزالة آخر superadmin بدون guard مناسب.

## Acceptance Criteria

- `users.manage` لا يساوي فقط UI.
- يمكن إظهار "effective permission" مع source (`Role`/`User`/`Deny`).
- `permissions.assign` يغيّر حقولًا auditable فقط.
- Deny override يعمل عمليًا.

## Alternatives Considered

- **Rejected**: أذونات role-by-role منفصلة داخل كل Component بدون Policy.
- **Rejected**: hard-coded permission strings خارج catalog.

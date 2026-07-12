# 17 - Frontend Authorization Guidelines

## Why

- واجهة frontend يجب أن تدعم navigation وUX على أساس نفس الـcapabilities.
- **Approved**: منع تحميل routes غير مسموح بها (canMatch) واستخدام canActivate + directive.

## Scope

- Route guard architecture.
- Permission-aware navigation.
- reusable components لا تعتمد على `user.role === 'Admin'`.
- loading states و403 UX.

## Ownership

- Frontend architecture team.
- Shared auth module.

## Source of Truth

- **Confirmed**:
  - `AuthService` يخزّن `permissions` في الذاكرة/localStorage.
  - `app-routing.module.ts` يستخدم `RoleGuard` فقط.
  - `app-shell` navigation static array.
- **Approved**:
  - Permission framework shared.

## Loading Standard (Skeleton / Shimmer)

- **Approved**: استخدام skeleton بدل الشاشة الفارغة أو spinner عام عند انتظار قراءة أي payload من الـBackend.
- **Cross-cutting**: يطبق على كل صفحات/عناصر القراءة في الواجهات التالية لن يكون فيه exception.
- Skeleton primitives الموصى بها:
  - `PlpSkeletonComponent` + `PlpSkeletonTableComponent` + `PlpSkeletonCardComponent` + `PlpSkeletonListItemComponent`.
  - `PlpSkeletonPlaceholderService` لإعادة استخدام التكوينات حسب نوع الصفحة (table/card/detail/form).
- قواعد تنفيذ:
  - الجدول: placeholder columns بنفس أبعاد الأعمدة والنسب النهائية لتقليل CLS.
  - الكرت: placeholder للعناوين، الصفوف الداخلية، والـactions.
  - صفحة التفاصيل: skeleton لكل sections (header, meta, history, related sections).
  - النماذج: skeleton للحقول حتى يصل الـmodel بالكامل ثم تبديل إلى النموذج الحقيقي.
  - shimmer animation فقط إذا لم يوجد `prefers-reduced-motion: reduce`.
- فصل حالات UI بوضوح:
  - Initial loading: إظهار skeleton لكل الـviewport الأولي.
  - Refreshing: skeleton/overlay خفيف مع إبقاء layout الحالي.
  - Empty: رسالة فارغة + action hints (بدون skeleton).
  - Error: حالة خطأ قراءة + retry action.
  - Unauthorized: route/page state مخصص مع زر login / request access.
- منع تكرار markup/CSS:
  - لا تكرر هياكل skeleton بين الصفحات، بل استخدم shared primitives + tokens للـspacing/spacing.
- RTL + responsive:
  - التوجيه في الاتجاهات يتبع tokens layout.
  - placeholders يجب أن تتكيف mobile/tablet/desktop.
- accessibility:
  - إضافة `aria-busy="true"` على الكونتينر.
  - `role=\"status\"`/نص مساعد مختصر في الحالات المهمة.
- **مهم**: لا تستبدل inline button loading داخل زر الحفظ بــ skeleton. استخدم spinner icon/inline state داخل الـbutton.

## Entities (Frontend Data)

- `PermissionState` (Planned): map permission->bool.
- `PermissionService` (Planned)
- `PermissionGuard` (Planned)
- `PermissionCanMatchGuard` (Planned)
- `PermissionDirective` e.g. `*plpCan="'workers.manage'"` (Planned)

## Business Rules

- Backend remains source of truth.
- UI checks are UI ergonomics:
  - show/hide button، hide menu.
  - never authorize critical action.
- Route load behavior:
  - canMatch first.
  - canActivate second.
- support `requireAny` and `requireAll`.
- avoid flicker by rendering skeleton during permission hydration.

## Validation

- route metadata standardized: `data.permissions` as string[].
- directive fallback:
  - `disabled` UI بدل إزالة DOM.
- 401/403 screens separate.

## Permissions

- frontend consumes same catalog:
  - `workers.view`, `workers.manage`, ...

## Audit

- No business audit in frontend; فقط logging UX events (non-PII).

## API Direction

- login/refresh -> `GET /api/auth/me`.
- recommended payload:
  - `user`, `roles`, `effectivePermissions`, `permissionVersion`.
- permissionVersion used for cache stale detection.

## UI Direction

- `app-routing.module.ts` target model:
  - data: `{ permissions: { requireAny: ['workers.view'], requireAll: [] } }`.
- `PermissionGuard` on child routes.
- `canMatch` guard on parent shell routes to avoid module loading.
- Sidebar built from same registry:
  - only render allowed entries.
- `PermissionDirective`:
  - `*plpCan="'workers.manage'"`.
  - `*plpCan="['workers.view','workers.manage'], requireAny=true"`.
- 403 page route `'/403'`.
- Preloading strategy filters unknown permission-based routes.

## Failure Handling

- إذا `AuthService.getCurrentUser()` فشل بشكل مؤقت -> block sensitive routes حتى تأكيد الحالة.
  - approved fallback: block sensitive routes حتى تأكيد الحالة.
- Missing permissions data => fallback to deny.

## Deferred Work

- lazy-loaded module boundary for each bounded context.
- route-level permission prefetch.

## Risks

- عرض محتوى مشوش قبل hydration.
- اختلاف casing permission strings بين backend/frontend.

## Acceptance Criteria

- لا وجود لـ `role` checks داخل components (مُمنوع في المراجعة).
- كل route يمكن حماية متعددة الأذونات.
- لا يظهر زر action ممنوع (أو يظهر مع tooltip disabled).

## Alternatives Considered

- **Rejected**: إخفاء route فقط في Sidebar دون canMatch.
- **Rejected**: `canActivate` فقط دون canMatch.

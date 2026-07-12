# 10 - Production Engineering

## Why

- **Approved**: تحتاج منصة الإنتاج محركًا منفصلاً لتنظيم التسلسل، السعة، والتصميم قبل مرحلة التنفيذ.
- **Confirmed**: منطق التعيين موجود حالياً عبر `AssignmentEngine`.

## Scope

- تخطيط التوزيع (assignment) وفق تاريخ سريان وتداخلات.
- إعدادات جاهزية خطوط التشغيل وتقارير جاهزية المرحلة/الخط (ReadinessEngine).
- لا يشمل تسجيل الإنتاج الكمي النهائي (انتقل إلى `11-production-execution.md`).

## Ownership

- Backend Capability Engine: `AssignmentEngine`, `ReadinessEngine`.
- Domain: `WorkerDefaultAssignment`, `WorkerTemporaryAssignment`, `AssignmentTimelineEntry`.
- UI: `factory-map-page`, `assignments-page`, `dashboard-page`.

## Source of Truth

- **Confirmed**:
  - `WorkerDefaultAssignment` و`WorkerTemporaryAssignment` تمثل الواقع التشغيلي للتعيين.
  - `AssignmentTimelineEntry` تسجل transitions وتاريخية.
- **Planned**:
  - `ProductionModelRouteConfig` (ربطه بين خطة خط الإنتاج ونموذج المنتج) في مرحلة 5/6.

## Entities

- `WorkerDefaultAssignment` (Confirmed)
- `WorkerTemporaryAssignment` (Confirmed)
- `AssignmentTimelineEntry` (Confirmed)
- `ReadinessSnapshot` (Planned)
- `ProductionPlanConstraint` (Deferred)

## Business Rules

1. التعيين الافتراضي دائم.
2. التعيين المؤقت لديه start/end.
3. عدم وجود تداخل غير مقبول لـ temporary بنفس العامل.
4. التابع "resolve current" يتبع قواعد انتهاء/بدء التاريخ.
5. **Approved**: التعيين لا يضيف إنتاجًا فعليًا، فقط يحدد `scope` و`capacity`.
6. **Architecture Conflict**: لا يوجد فصل كامل بين Planning وExecution في واجهات API.

## Validation

- **Confirmed**:
  - فحوصات `request` وvalidation في endpoints.
  - Engine يمنع تغييرات غير منطقية للفترات.
- **Planned**:
  - validation لحدود capacity قبل أي simulation.
  - policy للتحقق من scope/ownership قبل تخصيص worker لخط غير مصرح له.

## Permissions

- `assignments.view`
- `assignments.manage`
- `production.approve`

## Audit

- **Confirmed**: إنشاء وتحديث assignments يُسجل عبر `AuditLog`.
- **Planned**: إضافة audit على عمليات `assignment conflict resolution` و`bulk replan`.

## API Direction

- `GET /api/assignments`, `GET /api/assignments/{id}` (Confirmed موجودة)
- `POST /api/assignments/default`, `POST /api/assignments/temporary`, `POST /api/assignments/replacement` (Confirmed).
- **Approved**:
  - ربط permissions لكل endpoint:
    - `assignments.view` للقراءات
    - `assignments.manage` للتعديلات
    - `production.approve` للحسم/إغلاق خطة.

## UI Direction

- صفحة map وassignments تعرض حالات readiness والتعارض بصريًا.
- لا يحق للواجهة تعديل القواعد دون مرشح صلاحيات واضح.

## Failure Handling

- fallback للـsnapshot القديم إذا فشلت policy أو انتهت التهيئة.
- تعارض assignment يرجع برسالة conflict + alternative.

## Deferred Work

- Planning templates، auto-optimizers، simulation engine (Planned).
- قيود الإنتاج المتقدمة (shift-level constraints) (Deferred).

## Risks

- تداخل بين default وtemporary assignment يسبب سلوك غير متوقع دون إظهار سبب واضح.
- تحديثات assignment كثيرة قد تخلق race conditions عند عدم وجود optimistic concurrency.

## Acceptance Criteria

- كل assignment محجوز ضمن منطق تاريخي واضح.
- لا يظهر إنتاج إضافي عبر assignments page.
- وجود API guard بالـpermission + UI guard مماثل.

## Alternatives Considered

- **Rejected**: التعامل مع التعيين مباشرة داخل Frontend دون engine service.
- **Rejected**: إهمال timeline واستخدام حالة أخيرة فقط بدون سجل تغييري.

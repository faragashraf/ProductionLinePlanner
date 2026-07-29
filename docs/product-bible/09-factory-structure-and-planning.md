# 09 - Factory Structure & Planning

## Why

- **Approved**: إنتاج مخطط للخطوط والمراحل يحتاج سياقًا واضحًا لتفادي ربط عشوائي بين العمالات والتكلفة/الزمن.
- **Confirmed**: الكيانات الأساسية موجودة فعليًا في الكود:
  - `Factory`
  - `ProductionLine`
  - `MainStage`
  - `SubStage`
  - `WorkerDefaultAssignment`
  - `WorkerTemporaryAssignment`

## Scope

- الإدارة المجمّعة للبنية الهيكلية (مصنع/خط/مرحلة) + روابط التعيين الحالية.
- لا يتضمن التسعير أو زمن التشغيل النهائي للمنتج (انتقال للمراحل الإنتاجية أدناه).

## Ownership

- Backend: `ProductionLinePlanner.Domain`, `ProductionLinePlanner.Infrastructure`, `ProductionLinePlanner.Api`.
- Frontend: `src/frontend/src/app/pages/factory-map-page` و`pages/manufacturing-workspace` لإدارة البنية والمراحل.
- Data platform: `AppDbContext` في `src/backend/ProductionLinePlanner.Infrastructure/Data`.

## Source of Truth

- **Approved**:
  - `Factory`, `ProductionLine`, `MainStage`, `SubStage` من `AppDbContext`.
  - `WorkerDefaultAssignment` و`WorkerTemporaryAssignment` تحدد مواقع العمل الحالية للمجند.
- **Planned**:
  - `Department` داخل `AppDbContext` كـ master data منفصل لإسناد العامل دون خلطه مع `USERINFO`.
- **Rejected**: استبدال هذا التصنيف بهيكلات منفصلة لكل شاشة.

## Entities

- `Factory` (Confirmed)
- `ProductionLine` (Confirmed)
- `MainStage` (Confirmed)
- `SubStage` (Confirmed)
- `WorkerDefaultAssignment` (Confirmed)
- `WorkerTemporaryAssignment` (Confirmed)
- `Department` (Planned, محلي)
- `FactoryPlanSnapshot` (Deferred)

## Business Rules

1. كل `ProductionLine` ينتمي لمصنع واحد.
2. كل `MainStage` ينتمي لخط إنتاج واحد.
3. كل `SubStage` ينتمي لمرحلة رئيسية.
4. لا يجب أن يتجاوز التعديل التسلسلي ترتيب الأنابيب (SequenceOrder) قيمًا سلبية.
5. **Approved**: `SubStage` لا تحمل Pricing/Compensation مباشرة.
6. **Rejected**: استخدام `SubStage` كسلطه لحفظ أسعار المنتج؛ التقييم يكون في `ProductModelStage`.

## Validation

- **Confirmed**: validation على الـrequests في endpoints وdomain constructors موجود.
- **Planned**: قيود إضافية على:
  - عدم حذف فعاليات مفردة (soft delete via `IsActive=false`).
  - فحص تناسق العلاقات أثناء حذف/تعطيل الهيكل.

## Permissions

- `factories.view`
- `factories.manage`
- `productionLines.view` (بديل مقترح/مستقبلي داخل مجموعة factories)
- `productionLines.manage`
- `stages.view`
- `stages.manage`
- `stages.import`
- `stages.export`

## Audit

- **Confirmed**: جميع write operations الحالية على factories/lines/stages تضيف `AuditLog` عبر `AssignmentHelpers.AddAuditLog(...)` داخل API endpoints.
- **Architecture Conflict**: لا يوجد سجل تغييرات صريح على صلاحيات التعيين ضمن نفس scope (مؤجل إلى Phase 1 IAM / phase 1.2).

## API Direction

- `GET /api/factories`, `POST /api/factories` currently موجودة بـ admin policy.
- `GET /api/production-lines`, `POST /api/production-lines`.
- `GET /api/main-stages`, `GET /api/sub-stages`.
- **Approved**: future endpoints تتطلب permission-based، مثل:
  - `factories.view`/`factories.manage`
  - `stages.view`/`stages.manage`
  - `departments.manage` (عند إدخال Department محليًا).

## UI Direction

- صفحة إدارة المخطط (Factory/Line/Stage) تُعرض بناءً على route data ثابت بـ permission IDs.
- لا يختبر UI أي business rule من نفس الكيانات (لا تتحكم UI بقواعد الإنتاج/الأجور).

## Failure Handling

- عند تعارض اسم/كود خط داخل نفس المصنع => 409.
- عند حذف/تعطيل هيكل فيه تعيينات فعّالة => رفض مع توجيه إعادة ربط.
- **Architecture Conflict**: الحالية تسمح بإدارة هيكل بدون فحص تأثيرات cross-scope واضح.

## Deferred Work

- `Department` local master (Planned).
- Versioning للهيكل (snapshot) (Deferred).
- ربط التخطيط بموديلات الإنتاج (Deferred إلى Production Engineering).

## Risks

- تغيّر ترتيب `SequenceOrder` ينعكس على UI غير متزامن.
- حذف `SubStage` بدون إزالة تخصيصات العمال قد يسبب orphan assignment logic (مخفّض محمي بتفعيل `IsActive=false` حاليا).

## Acceptance Criteria

- كل read/write للهيكل مصحوب بمستند صلاحيات واضح.
- لا يتكرر منطق "pricing" داخل `SubStage`.
- API وUI يعتمدان على permission IDs موحدة.
- أي تعديل هيكلي يُسجل في Audit مع actor + request snapshot.

## Alternatives Considered

- **Rejected**: نموذج واحد "FactoryConfig" مرن بخصائص pricing/import/export مدموجة.
- **Rejected**: الاعتماد المباشر على Department من Attendance Source كـ production identity master.

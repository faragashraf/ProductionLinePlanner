# 11 - Production Execution

## Why

- تنفيذ الإنتاج هو مصدر المترنات المالية وسلوك كمية الإنتاج.
- **Approved**: يجب فصل "الكمية الفعلية" عن "مساهمة العامل" منعًا لتضخيم الإنتاج.

## Scope

- تسجيل الناتج الفعلي (Physical Output).
- إدارة مساهمات العمال أثناء تنفيذ كل مرحلة/صفقة إنتاج.
- تجميع النتائج النهائية للتعويض.
- لا يدخل فيه Pricing نفسه (يأتي من `ProductModelStage`).

## Ownership

- Backend: Domain Engine "ProductionExecutionEngine" (مقترح جديدة)، endpoint in API.
- UI: صفحة production execution.
- Audit: `AuditEngine`.

## Source of Truth

- **Planned (أساسي للـV1)**:
  - `ProductionStageOutput` (كمية إنتاج حقيقية لكل مرحلة/وقت/Batch).
  - `WorkerAllocation` (نسب/مقادير تخصيصية لا تُعدي الإنتاج).
- **Deferred**:
  - Snapshot تفصيلي على مستوى الآلة/التبديل اللحظي.
- **Rejected**: اعتبار كل سجل تخصيص عامل كصفقة إنتاج جديدة.

## Entities

- Planned:
  - `ProductionStageOutput`
  - `WorkerAllocation`
  - `ProductionBatch` (مستقبلي إذا لزم)
- Confirmed:
  - `Worker` (للربط مع العامل).
  - `SubStage` (للحالة المكانية).

## Business Rules

1. `ProductionStageOutput.Quantity` هو الكمية الأساسية الفعلية وليست محسوبة من عدد العاملين.
2. `WorkerAllocation` ليس له دور في زيادة `ProductionStageOutput`.
3. لكل Output:
   - حالة: draft/submitted/approved.
   - Snapshot of pricing configuration عند التسجيل (Approved).
4. **Approved**: لا يسمح بمضاعفة كمية الإنتاج بسبب عدة عمال.

### Example

- Output physical quantity = 500 pairs.
- Worker A = 50%.
- Worker B = 50%.
- Production total must remain 500، وليس 1000.

## Validation

- Non-negative quantity.
- Timestamp window within shift scope.
- One output per stage per batch/day per status rules (Planned unique constraints).
- Allocation percentages sum business checks حسب mode:
  - `SharedPercentage` sum must = 100%.
  - `FixedRatePerWorker` no percentage sum restriction.

## Permissions

- `production.record`
- `production.approve`
- `production.view`

## Audit

- **Planned**: write audit لكل output وallocations.
- **Architecture Conflict**: حاليًا لا يوجد نماذج execution، وبالتالي لا يوجد audit مفصل لهذه العمليات.

## API Direction

- Proposed:
  - `POST /api/production/stage-outputs`
  - `GET /api/production/stage-outputs`
  - `POST /api/production/stage-outputs/{id}/approve`
  - `GET /api/production/allocations`
- Permission mapping:
  - view: `production.view`
  - create: `production.record`
  - approve: `production.approve`

## UI Direction

- شاشة تسجيل الإنتاج تستقبل output مرة واحدة لكل مرحلة/وحدة عمل.
- شاشة توزيع الأجور تعرض `WorkerAllocation` داخل نفس output بدون تعديل `Quantity`.

## Failure Handling

- إذا تعذر حساب payroll نتيجة missing `ProductModelStage` => رفض `production.record` مع رسالة واضحة.
- if duplicate output found for same key => 409 conflict.

## Deferred Work

- Device-level timestamps/IoT confirmation (Deferred).
- Real-time locking per sub-stage (Deferred).

## Risks

- تضارب التوقيت بين Attendance وExecution.
- إعادة فتح output معتمدة قد تؤدي recalculation risk (must version snapshots).

## Acceptance Criteria

- مجموع `WorkerAllocation` لا يغير `ProductionStageOutput.Quantity`.
- payroll engine يحسب من snapshot وليس من قيم مرحلة/منتج حية.
- route-level permissions منع unauthorized updates.

## Alternatives Considered

- **Rejected**: model يحسب payroll من output lines دون snapshot.
- **Rejected**: ربط كل WorkerAllocation بجدول منفصل للرواتب مباشرة بدون Stage output.

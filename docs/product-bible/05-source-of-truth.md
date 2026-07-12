# 05 - Source of Truth

## SoT per domain (Approved)

- **Attendance source identifiers**: `Attendance source tables USERINFO/CHECKINOUT` as external identity+time source.
- **Worker operational identity**: `AppDomain.Worker` + external keys (`AttendanceUserId`, `BadgeNumber`) mapping table is same entity (not separate mirror).
- **Factory/line/stage structure**: `AppDb` tables (`Factories`, `ProductionLines`, `MainStages`, `SubStages`).
- **Assignments & history**: `WorkerDefaultAssignment`, `WorkerTemporaryAssignment`, `AssignmentTimelineEntry`.
- **Audit**: `AuditLogs`.

## Not SoT yet (Planned)

- **Salary history**: No dedicated source => create `WorkerSalaryHistory`.
- **Product model pricing/time**: No source => create `ProductModel`, `ProductModelStage`.
- **Production output**: No source => create `ProductionStageOutput` + `WorkerAllocation`.

## Confirmed conflict

- `Worker.BadgeNumber` موجود ضمن Worker ولم توجد سياسة ثابتة لمنع تغييره أو تعارُضه مع ZK source.

## Source-of-truth rules

1. أي قيمة تشغيلية تأتي من مصادر خارجية تُحمل كمصدر ثابت في Planner فقط بعد validation.
2. أي إدخال متكرر من التكامل لا يُعتبر الحقيقة الوحيدة؛ يحتفظ بمدة/تاريخ وraw id.

## Failure handling

- عند تعارض worker-id mapping: لا ينسخ sync ورفع خطأ `unmatchedSourceRows` في نتيجة Attendance sync.

## Deferred

- Global SOT policy service مع reconciliation queue.

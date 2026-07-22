# Manufacturing command center data dictionary

## النطاق والثوابت

- تعرض `/dashboard` و`/factory-map` الاستجابة نفسها من `GET /api/manufacturing-command-center`. لا توجد مصفوفات تجريبية أو تحويل لفشل API إلى أرقام.
- كل استجابة مرتبطة صراحةً بـ`productionDate` ونطاق المصنع/القسم/الخط وحالة التشغيل و`calculatedAtUtc`. جميع الفلاتر تطبّق في الخادم قبل إنشاء الملخص أو الخريطة.
- الحضور قراءة فقط عبر `IAttendanceEngine`. هو المرجع الوحيد لحدود يوم القاهرة ولتحويل حالات المصدر المعتمدة إلى `Present` أو`Late`; لا يكتب هذا المسار في ZKTime أو في سجل الحضور.
- العمال والتسكينات لا يدخلون إلا عندما يكون العامل `IsActive` و`EmploymentStatus=Active`، والتسكين `WorkerDefaultAssignment` نشطًا، والمرحلة الفرعية والرئيسية نشطتين. التسكينات المؤقتة/النقل/الاستبدال مستبعدة.
- التشغيل هو `ProductionOrder` في التاريخ المحدد، على خط داخل النطاق، ومن مصدر يومي أو استيراد (`SourceReference` أو`SourceImportBatchId`). الـPreview غير المحفوظ مستبعد.
- `ProductionOrder.PlannedQuantity` اسم الحقل البرمجي لـ«كمية التشغيل المحفوظة». هي قيمة أدخلت وحُفظت في أمر التشغيل، وليست إثبات إنتاج فعلي أو معتمد، ولا تُستنتج من سجلات المراحل.
- `StageProductionRecord.TotalWorkerEarnings` «قيمة سجلات المراحل»، لا كمية إنتاج ولا اعتماد مالي.
- النسب لا تُرسل بلا البسط والمقام. إذا كان المقام صفرًا فالنسبة `null` و`zeroBehavior=NoData`؛ وإذا كان نطاقًا جزئيًا لا يمكن فيه إسناد غير المسكنين فالنسبة `null` و`zeroBehavior=NotAttributable`.

## قاموس المؤشرات

| الاسم العربي | الاسم البرمجي | التعريف والمعادلة | الجداول/المصادر | الفلاتر والاستبعادات | لا بيانات / قسمة صفر |
|---|---|---|---|---|---|
| العمال النشطون | `workforce.activeWorkers` | عدد مميز للعمال النشطين ذوي خدمة عمل نشطة؛ ليس عدد الحاضرين ولا المسكنين | `Workers` | متاح فقط في نطاق يمكن إسناده لكل العمال: كل المصانع/كل الحالات، أو مصنع وحيد في النظام. غير ذلك `null` حتى لا يُنسب العامل غير المسكن تخمينًا | `null` عندما لا يمكن الإسناد؛ `0` صحيح إن لم يوجد عامل مؤهل |
| الحاضرون في النطاق | `workforce.presentWorkers` | عدد مميز لحالة `Present` أو`Late` في يوم القاهرة. في النطاق الكامل: كل العمال المؤهلين؛ في النطاق الجزئي: تقاطع الحاضرين مع التسكين الدائم على خطوط النطاق | `Workers` + `IAttendanceEngine` + `WorkerDefaultAssignments` | يستبعد العامل/التسكين/المراحل غير النشطة. العامل متعدد التسكين يحسب مرة واحدة | `0` |
| حاضرون ومسكنون دائمًا | `workforce.presentPermanentlyAssignedWorkers` | عدد مميز لـ`الحاضرون ∩ المسكنون الدائمون في النطاق` | الحضور + التسكينات | نفس الاستبعادات؛ لا يتضاعف العامل عند تعدد سجلات التسكين | `0` |
| حاضرون غير مسكنين | `workforce.presentUnassignedWorkers` | الحاضرون المؤهلون بلا أي تسكين دائم نشط على خطوط النظام | الحضور + كل التسكينات النشطة | لا يحسب في قسم/خط أو نطاق غير كامل لأنه لا توجد علاقة مصدر تنسب غير المسكن للمكان | `null` عند عدم قابلية الإسناد، وإلا `0` |
| مسكنون دائمًا غير حاضرين | `workforce.permanentlyAssignedNotPresentWorkers` | عدد مميز للمسكنين في النطاق ممن ليست حالتهم `Present` أو`Late`؛ التفاصيل تفرق بين `Absent` و`NoRecord` | التسكين + الحضور | نفس شروط نشاط العامل والعلاقة؛ لا تكرار للموظف | `0` |
| تغطية تسكين الحاضرين | `workforce.assignmentCoverage` | البسط `presentPermanentlyAssignedWorkers`؛ المقام الحاضرون في نطاق كامل قابل للإسناد | المؤشرين السابقين | لا تُحسب لنطاق لا يمكن نسبة غير المسكنين إليه | `null/NoData` عند المقام 0؛ `null/NotAttributable` للنطاق الجزئي |
| الخطوط النشطة | `lineSummary.activeLines` | عدد صفوف الخط النشطة بعد المصنع/القسم/الخط/حالة التشغيل | `ProductionLines` | الخط غير النشط مستبعد؛ فلتر الحالة يبقي الخط الذي يطابق حالته فقط | `0` وحالة فارغة صريحة |
| الخطوط الجاهزة | `lineSummary.readyLines` | تشغيل مسجل + رحلة مراحل نشطة مطلوبة + سعر وزمن مكتملان + عدد حاضر مسكن لكل مرحلة ≥ `SubStage.Capacity` | أوامر التشغيل، رحلة الموديل، التسكين، الحضور | لا نسبة كفاءة مصطنعة؛ كل شروط النشاط السابقة مطبقة | `0` |
| خطوط نقص العمالة | `lineSummary.staffingShortageLines` | يوجد في رحلة التشغيل مرحلة واحدة على الأقل بعدد حاضر مسكن أقل من السعة | نفس مصدر الجاهزية | لا يعد العامل مرتين داخل المرحلة | `0` |
| خطوط بلا رحلة مهيأة | `lineSummary.journeyNotConfiguredLines` | تشغيل بلا مراحل مطلوبة نشطة على خطه، أو خط بلا تشغيل ولا رحلة موديل نشطة قابلة للتشغيل | `ProductModelStages → SubStages → MainStages → ProductionLines` | العلاقات غير النشطة مستبعدة | `0` |
| خطوط بياناتها غير مكتملة | `lineSummary.dataIncompleteLines` | تشغيل له مرحلة مطلوبة سعرها ≤0 أو زمنها المعياري فارغ/≤0 | رحلة تشغيل الأمر | لا يخفي نقص البيانات بكون الخط غير جاهز لسبب آخر | `0` |
| الخطوط ذات المشكلة | `lineSummary.problemLines` | خط غير `Ready`، أو بلا تشغيل، أو له تشغيل Draft/ApprovalCancelled/Cancelled | ملخص الخطوط والتشغيل | وصف تشغيلي وليس نسبة | `0` |
| مراحل التشغيل بلا عامل حاضر | `lineSummary.stagesWithoutPresentWorker` و`dataQuality.activeJourneyStagesWithoutPresentWorker` | عدد أزواج العملية/مرحلة موديل المطلوبة التي ليس لها عامل حاضر مسكن؛ كل عملية تحتفظ بهويتها | رحلة الأمر + التسكين + الحضور | لا يخلط بين مراحل موديلات أو عمليات مختلفة | `0` |
| خطوط بها تشغيل / بلا تشغيل | `operations.linesWithOperation` / `operations.linesWithoutOperation` | الأول: عدد مميز لخطوط بنود التشغيل المعروضة؛ الثاني: خطوط الخريطة بلا بند تشغيل معروض | `ProductionOrders` + خطوط النطاق | بعد فلتر الحالة؛ لا يعد صف سجل مرحلة كتشغيل | `0` |
| مسودات / معتمد / ملغي الاعتماد / ملغى | `operations.draftOperations` / `approvedOperations` / `approvalCancelledOperations` / `cancelledOperations` | عدد أوامر التشغيل بعد تصنيف الحالة: `Completed→Approved`؛ `Cancelled→Cancelled`؛ Draft مع `CancelledAtUtc` في سجل مرحلة→`ApprovalCancelled`؛ الباقي Draft | `ProductionOrders` + `StageProductionRecords` | التاريخ والنطاق والمصدر اليومي/المستورد وحالة التشغيل | `0` |
| قيمة سجلات التشغيل المعتمدة | `operations.approvedRecordedValue` | مجموع `recordedStageValue` لعمليات `Approved` فقط | سجلات المراحل غير الملغاة | قيمة مسجلة وليست كمية أو موافقة مالية | `0` |
| مراحل موديل بلا سعر/زمن | `dataQuality.modelStagesWithoutPrice` / `modelStagesWithoutStandardTime` | عدد مراحل الرحلات النشطة المطلوبة المهيأة على خطوط النطاق، حيث السعر ≤0 أو الزمن فارغ/≤0 | رحلة التهيئة النشطة | يشمل خطًا بلا تشغيل كي لا يخفي خلل التهيئة؛ العلاقات غير النشطة مستبعدة | `0` |
| موديلات نشطة بلا رحلة | `dataQuality.activeModelsWithoutJourney` | موديل نشط لا توجد له مرحلة مطلوبة نشطة (`NOT EXISTS`) | `ProductModels`, `ProductModelStages` | متاح فقط مع كل المصانع وكل حالات التشغيل؛ لا ينسب مدلولًا يتيمًا لمصنع | `null` خارج النطاق الكامل، وإلا `0` |
| كمية التشغيل المحفوظة | `operation.finalLineQuantity` | قيمة `ProductionOrder.PlannedQuantity` للأمر نفسه؛ ليست إنتاجًا فعليًا مقاسًا | `ProductionOrders` | تظهر فقط مع أمر محفوظ؛ لا تجمع كميات المراحل | لا توجد عند غياب التشغيل |
| قيمة سجلات المراحل | `operation.recordedStageValue` | مجموع `TotalWorkerEarnings` للسجلات غير الملغاة في أمر واحد | `StageProductionRecords` | لا تستخدم ككمية أو نسبة إنتاج | `0` |
| اكتمال تسجيل المراحل | `operation.stageRegistrationCoverage` | البسط: معرفات `ProductModelStage` المسجلة غير الملغاة والمتقاطعة مع رحلة الأمر؛ المقام: مراحل رحلة الأمر النشطة. يمنع التقاطع نسبة فوق 100% | رحلة الأمر + السجلات | لا تعد سجلات مرحلة خارجة عن الرحلة الحالية | `null/NoData` عندما المقام 0؛ ليس كفاءة إنتاج |

## Operational statuses

| Status | Rule |
|---|---|
| `Ready` | operation exists, journey exists, price/time complete, and every required stage meets present permanent staffing capacity |
| `StaffingShortage` | journey exists and at least one required stage is below present permanent staffing capacity |
| `JourneyNotConfigured` | no required active model journey can be resolved on the line |
| `NoOperation` | line has a runnable journey but no persisted operation for the selected date |
| `DataIncomplete` | current journey has missing price or standard time |
| `Draft` | persisted daily operation is a draft |
| `Approved` | persisted daily operation is completed/approved |
| `ApprovalCancelled` | approval cancellation evidence exists and the daily order is reopened as draft |
| `Cancelled` | persisted order is cancelled |

## SQL verification queries

These queries target the application read model restored in development. They are read-only. Set the parameters to the exact API scope. `@StartUtc` and `@EndUtc` must be the Cairo day bounds used by the application. Stored attendance enum values are `Present=0`, `Late=2` after the approved ZKTime mapping; never query or write ZKTime directly from these scripts.

```sql
DECLARE @ProductionDate date = '2026-07-22';
DECLARE @StartUtc datetime2 = '2026-07-21T21:00:00Z'; -- derive using current Cairo offset
DECLARE @EndUtc datetime2 = '2026-07-22T21:00:00Z';
DECLARE @FactoryId uniqueidentifier = NULL;
DECLARE @DepartmentId uniqueidentifier = NULL;
DECLARE @LineId uniqueidentifier = NULL;

WITH LatestAttendance AS (
  SELECT ar.WorkerId, ar.AttendanceStatus,
         ROW_NUMBER() OVER (PARTITION BY ar.WorkerId ORDER BY ar.AttendanceTimeUtc DESC) AS rn
  FROM AttendanceRecords ar
  WHERE ar.AttendanceTimeUtc >= @StartUtc AND ar.AttendanceTimeUtc < @EndUtc
), Present AS (
  SELECT DISTINCT WorkerId FROM LatestAttendance WHERE rn = 1 AND AttendanceStatus IN (0, 2)
), ScopedAssignments AS (
  SELECT DISTINCT a.WorkerId, a.SubStageId, ms.ProductionLineId
  FROM WorkerDefaultAssignments a
  JOIN Workers w ON w.Id = a.WorkerId AND w.IsActive = 1 AND w.EmploymentStatus = 1 -- Active
  JOIN SubStages ss ON ss.Id = a.SubStageId AND ss.IsActive = 1
  JOIN MainStages ms ON ms.Id = ss.MainStageId AND ms.IsActive = 1
  JOIN ProductionLines pl ON pl.Id = ms.ProductionLineId AND pl.IsActive = 1
  WHERE a.IsActive = 1
    AND (@FactoryId IS NULL OR pl.FactoryId = @FactoryId)
    AND (@DepartmentId IS NULL OR pl.DepartmentId = @DepartmentId)
    AND (@LineId IS NULL OR pl.Id = @LineId)
)
SELECT (SELECT COUNT(*) FROM Present) AS PresentWorkers,
       (SELECT COUNT(DISTINCT a.WorkerId) FROM ScopedAssignments a JOIN Present p ON p.WorkerId = a.WorkerId) AS PresentAssignedWorkers,
       (SELECT COUNT(*) FROM Present p WHERE NOT EXISTS (SELECT 1 FROM WorkerDefaultAssignments a WHERE a.WorkerId=p.WorkerId AND a.IsActive=1)) AS PresentUnassignedWorkers,
       (SELECT COUNT(DISTINCT a.WorkerId) FROM ScopedAssignments a WHERE NOT EXISTS (SELECT 1 FROM Present p WHERE p.WorkerId=a.WorkerId)) AS AssignedNotPresentWorkers;
```

```sql
-- Active lines and operation states. UI/API line counts use DISTINCT ProductionLineId.
SELECT COUNT(*) AS ActiveLines
FROM ProductionLines pl
WHERE pl.IsActive=1
  AND EXISTS (SELECT 1 FROM Factories f WHERE f.Id=pl.FactoryId AND f.IsActive=1)
  AND (pl.DepartmentId IS NULL OR EXISTS (SELECT 1 FROM Departments d WHERE d.Id=pl.DepartmentId AND d.IsActive=1))
  AND (@FactoryId IS NULL OR pl.FactoryId=@FactoryId)
  AND (@DepartmentId IS NULL OR pl.DepartmentId=@DepartmentId)
  AND (@LineId IS NULL OR pl.Id=@LineId);

-- This is the raw order status. The endpoint additionally classifies a Draft with
-- StageProductionRecord.CancelledAtUtc evidence as ApprovalCancelled.
SELECT po.Status, COUNT(*) AS OperationCount, COUNT(DISTINCT po.ProductionLineId) AS LineCount
FROM ProductionOrders po
WHERE po.ProductionDate=@ProductionDate AND po.ProductionLineId IS NOT NULL
  AND (po.SourceReference IS NOT NULL OR po.SourceImportBatchId IS NOT NULL)
GROUP BY po.Status;
```

```sql
-- Model journey on one line, preserving ProductModelStage price/time/order.
SELECT pms.Id, pms.ProductModelId, pms.StageOrder, pms.PiecePrice, pms.StandardSeconds,
       ss.Id AS SubStageId, ss.Code, ss.Name, ss.Capacity, ms.Name AS MainStageName, ms.ProductionLineId
FROM ProductModelStages pms
JOIN SubStages ss ON ss.Id=pms.SubStageId
JOIN MainStages ms ON ms.Id=ss.MainStageId
WHERE pms.ProductModelId=@ProductModelId AND ms.ProductionLineId=@LineId
  AND pms.IsActive=1 AND pms.IsRequired=1 AND ss.IsActive=1 AND ms.IsActive=1
ORDER BY pms.StageOrder;
```

```sql
-- Required stages with no present permanently assigned worker.
WITH LatestAttendance AS (
  SELECT ar.WorkerId, ar.AttendanceStatus, ROW_NUMBER() OVER (PARTITION BY ar.WorkerId ORDER BY ar.AttendanceTimeUtc DESC) rn
  FROM AttendanceRecords ar WHERE ar.AttendanceTimeUtc>=@StartUtc AND ar.AttendanceTimeUtc<@EndUtc
), Present AS (SELECT WorkerId FROM LatestAttendance WHERE rn=1 AND AttendanceStatus IN (0,2))
SELECT pms.Id, ss.Code, ss.Name
FROM ProductModelStages pms
JOIN SubStages ss ON ss.Id=pms.SubStageId
JOIN MainStages ms ON ms.Id=ss.MainStageId
WHERE pms.ProductModelId=@ProductModelId AND ms.ProductionLineId=@LineId
  AND pms.IsActive=1 AND pms.IsRequired=1 AND ss.IsActive=1 AND ms.IsActive=1
  AND NOT EXISTS (
    SELECT 1 FROM WorkerDefaultAssignments a
    JOIN Workers w ON w.Id=a.WorkerId AND w.IsActive=1 AND w.EmploymentStatus=1
    JOIN Present p ON p.WorkerId=a.WorkerId
    WHERE a.SubStageId=ss.Id AND a.IsActive=1
  );
```

```sql
-- One operation: stored operation quantity (not actual measured output), recorded value,
-- and registration numerator/denominator.
SELECT po.Id, po.PlannedQuantity AS FinalLineQuantity,
       COALESCE(SUM(CASE WHEN spr.Status<>2 THEN spr.TotalWorkerEarnings ELSE 0 END),0) AS RecordedStageValue,
       COUNT(DISTINCT CASE WHEN spr.Status<>2 THEN spr.ProductModelStageId END) AS RegisteredStages
FROM ProductionOrders po
LEFT JOIN StageProductionRecords spr ON spr.ProductionOrderId=po.Id
WHERE po.Id=@ProductionOrderId
GROUP BY po.Id, po.PlannedQuantity;

SELECT COUNT(*) AS JourneyStages
FROM ProductModelStages pms
JOIN SubStages ss ON ss.Id=pms.SubStageId
JOIN MainStages ms ON ms.Id=ss.MainStageId
JOIN ProductionOrders po ON po.ProductModelId=pms.ProductModelId AND po.ProductionLineId=ms.ProductionLineId
WHERE po.Id=@ProductionOrderId AND pms.IsActive=1 AND pms.IsRequired=1 AND ss.IsActive=1 AND ms.IsActive=1;
```

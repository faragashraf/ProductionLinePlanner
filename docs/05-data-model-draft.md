# 05 - مسودة نموذج البيانات

> ملاحظة: هذا نموذج مفاهيمي أولي لمرحلة التوثيق فقط.

## الكيانات الأساسية

### User
- `Id`
- `FullName`
- `Email`
- `PasswordHash`
- `Role` (`SuperAdmin` / `Admin`)
- `IsActive`

### FactoryLine
- `Id`
- `Name`
- `Description`
- `IsActive`

### StageGroup (المراحل الرئيسية)
- `Id`
- `FactoryLineId`
- `Name`
- `OrderIndex`

### Stage (المراحل الفرعية)
- `Id`
- `StageGroupId`
- `Name`
- `Capacity` (اختياري)
- `OrderIndex`

### Worker
- `Id`
- `EmployeeCode`
- `FullName`
- `ZkEmployeeCode` (مطابق ZKTime)
- `DefaultStageId` (nullable)
- `IsActive`

### WorkerAttendanceSnapshot
- `Id`
- `WorkerId`
- `AttendanceState` (`Present`, `Absent`, `Late`, `Partial`)
- `SourceTimestamp`
- `RawData` (اختياري JSON مختصر)

### PermanentAssignment
- `Id`
- `WorkerId`
- `StageId`
- `AssignedByUserId`
- `AssignedAt`
- `IsActive`

### TemporaryAssignment
- `Id`
- `WorkerId`
- `FromStageId`
- `ToStageId`
- `StartAt`
- `EndAt`
- `AssignedByUserId`
- `Reason`
- `IsActive`

### Notification
- `Id`
- `UserId` (المستلم)
- `Title`
- `Body`
- `Type` (`Attendance`, `Assignment`, `Alert`, `System`)
- `IsRead`
- `CreatedAt`

## العلاقات
- `FactoryLine` 1 -> * `StageGroup`.
- `StageGroup` 1 -> * `Stage`.
- `Worker` * -> 1 `DefaultStage` (اختياري).
- `Worker` * -> * `PermanentAssignment` (منظور التفعيل الأحدث/الفعال).
- `Worker` * -> * `TemporaryAssignment` ضمن فترات زمنية.
- `User` ينشئ تعيينات/تنبيهات.

## منطق الحسابات
- `ReadyState` الحالي للمرحلة:
  - يجمع بين `WorkerAttendanceSnapshot` ووجود تعيين فعّال.
- `ReadinessPercent` للمراحل:
  - عدد العمال الجاهزين / العدد المطلوب (أو العدد المتاح بحسب السعة).
- `LineReadinessPercent`:
  - متوسط مرجح أو معدل بسيط حسب الحاجة الداخلية (أفضل في البداية متوسط بسيط).

## قواعد الاسترجاع بعد تعيين مؤقت
- عند انتهاء `TemporaryAssignment.EndAt`:
  - إذا Worker له `DefaultStageId` → يعاد إليه.
  - إذا لا يوجد → حالة Unassigned.

## أسئلة مفتوحة
- هل ستكون السعة في المرحلة إجبارية أو اختيارية؟
- هل يلزم حفظ سجل تفصيلي لأسباب الغياب في نفس الكيان أو منفصل؟
- هل نحتاج جدول مستقل لورديات العمل؟

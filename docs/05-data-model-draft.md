# 05 - مسودة نموذج البيانات (محدثة)

> الغرض من هذا الملف: توثيق بنية البيانات من جهة **الدومين** (بدون أكواد تنفيذية) قبل بدء التطوير.

## 1) الكيانات والجداول

### Factory
- `FactoryId` (PK)
- `Name` (نص)
- `Code` (فريد)
- `Location` (اختياري)
- `IsActive` (bool)
- `CreatedAt` , `CreatedByUserId`
- `UpdatedAt` , `UpdatedByUserId`

### ProductionLine
- `ProductionLineId` (PK)
- `FactoryId` (FK → Factory)
- `Name` (نص)
- `LineCode` (اختياري، فريد داخل المصنع)
- `SequenceOrder` (int)
- `IsActive` (bool)
- `CreatedAt`, `UpdatedAt`

### MainStage
- `MainStageId` (PK)
- `ProductionLineId` (FK → ProductionLine)
- `Name` (نص)
- `SequenceOrder` (int)
- `IsCritical` (bool، افتراضي = false)
- `IsActive` (bool)
- `CreatedAt`, `UpdatedAt`

### SubStage
- `SubStageId` (PK)
- `MainStageId` (FK → MainStage)
- `Name` (نص)
- `Capacity` (int, > = 0)  // عدد العمال المستهدف لتشغيلها
- `SequenceOrder` (int)
- `IsActive` (bool)
- `CreatedAt`, `UpdatedAt`

### Worker
- `WorkerId` (PK)
- `EmployeeCode` (فريد)
- `FullName`
- `ZkEmployeeCode` (اختياري)
- `Phone` (اختياري)
- `IsActive` (bool)
- `CreatedAt`, `UpdatedAt`

### WorkerDefaultAssignment
- `WorkerDefaultAssignmentId` (PK)
- `WorkerId` (FK → Worker, فريد)
- `SubStageId` (FK → SubStage)
- `AssignedAt` (datetime)
- `AssignedByUserId` (FK → User)
- `IsActive` (bool)
- `Reason` (اختياري: transfer, restructure, init)
- `CreatedAt`, `UpdatedAt`

### WorkerTemporaryAssignment
- `WorkerTemporaryAssignmentId` (PK)
- `WorkerId` (FK → Worker)
- `FromSubStageId` (FK → SubStage)
- `ToSubStageId` (FK → SubStage)
- `StartAtUtc` (datetime)
- `EndAtUtc` (datetime)
- `ReplacementForWorkerId` (FK → Worker, nullable)
- `AssignedByUserId` (FK → User)
- `Reason` (enum: CoverAbsent, Rebalance, PeakSupport, UrgentDemand)
- `Status` (enum: Scheduled, Active, Expired, Cancelled, Completed)
- `CreatedAt`, `UpdatedAt`

### AttendanceRecord
- `AttendanceRecordId` (PK)
- `WorkerId` (FK → Worker)
- `AttendanceTimeUtc` (datetime)
- `AttendanceState` (enum: Present, Absent, Late, Unknown)
- `Source` (enum: ZkTime, Manual)
- `SourceRawId` (nullable)
- `SourcePayload` (JSON اختياري)

### StageReadinessSnapshot
- `StageReadinessSnapshotId` (PK)
- `ScopeType` (enum: SubStage, MainStage, ProductionLine, Factory)
- `ScopeEntityId` (guid/int)
- `CalculatedAtUtc` (datetime)
- `TotalRequiredWorkers` (int)
- `PresentWorkers` (int)
- `LateWorkers` (int)
- `AbsentWorkers` (int)
- `UnassignedWorkers` (int)
- `ReadinessPercent` (decimal(5,2))

### Notification
- `NotificationId` (PK)
- `RecipientUserId` (FK → User)
- `SenderUserId` (FK → User, nullable)
- `Title`
- `Message`
- `NotificationType` (enum: Attendance, Assignment, Readiness, System, Audit)
- `Severity` (enum: Info, Warning, Critical)
- `RelatedWorkerId` (FK → Worker, nullable)
- `RelatedEntityType` (اختياري)
- `RelatedEntityId` (optional)
- `IsRead` (bool)
- `ReadAt` (nullable)
- `CreatedAt`

### User
- `UserId` (PK)
- `FullName`
- `Email` (فريد)
- `PasswordHash`
- `IsActive` (bool)
- `PreferredLanguage` (enum: ar, en)
- `CreatedAt`, `UpdatedAt`

### Role
- `RoleId` (PK)
- `Code` (فريد: SuperAdmin, Admin)
- `Name`
- `Description`
- `IsSystemRole` (bool)

### AuditLog
- `AuditLogId` (PK)
- `ActorUserId` (FK → User)
- `Action` (النص/الـverb: Create, Update, Cancel, Resolve...)
- `EntityType`
- `EntityId`
- `EntityBeforeJson` (nullable)
- `EntityAfterJson` (nullable)
- `RequestMeta` (JSON اختياري: IP, endpoint, correlationId)
- `CreatedAt`

### UserRole (جدول ربط)
- `UserId` (FK → User)
- `RoleId` (FK → Role)
- `AssignedAt`
- `AssignedByUserId` (FK → User)
- **المفتاح الأساسي المركب: (UserId, RoleId)**

## 2) العلاقات (ER)
- `Factory` 1 → N `ProductionLine`.
- `ProductionLine` 1 → N `MainStage`.
- `MainStage` 1 → N `SubStage`.
- `SubStage` 1 → N `WorkerDefaultAssignment`.
- `Worker` 1 → 1 `WorkerDefaultAssignment` فعّال (عند حد أقصى `IsActive = true` واحد).
- `Worker` 1 → N `WorkerTemporaryAssignment`.
- `Worker` 1 → N `AttendanceRecord`.
- `User` 1 → N `WorkerDefaultAssignment` (من قام بالتعيين).
- `User` 1 → N `WorkerTemporaryAssignment`.
- `User` 1 → N `AuditLog` (كمصدر).
- `User` N ↔ M `Role` (عبر `UserRole`).
- `SubStage`/`MainStage`/`ProductionLine`/`Factory` → N `Notification` (عبر حقول `Related*`).

## 3) قواعد التكامل الأساسية
- `WorkerTemporaryAssignment`:
  - `StartAtUtc < EndAtUtc`.
  - لا يسمح بفترتين فعّاليتين متداخلتين للعامل نفسه (`Status` في (Scheduled/Active)).
  - `ToSubStageId` و`FromSubStageId` لا يلزمهما أن يختلفا، لكن يفضّل اختلافهما لتجنب سجلات بلا أثر.
- `WorkerDefaultAssignment`:
  - لكل عامل تعيين ثابت واحد فعّال فقط.
- `WorkerTemporaryAssignment` لا يلغي سجل التسكين الثابت.
  - التسكين الثابت يبقى مرجع الرجوع عند انتهاء المؤقت.
- `SubStage.Capacity` = 0 يسمح بالتعريف التشغيلي للمرحلة دون حد أدنى.
- `AttendanceRecord` يُرتّب حسب `AttendanceTimeUtc` لاستخراج أحدث حالة.

## 4) قواعد التراجع بعد انتهاء التسكين المؤقت
- عند انتهاء الفاصل الزمني (`Status` = Expired/Completed أو التاريخ انتهى):
  - إذا worker لديه `WorkerDefaultAssignment` فعّال: العودة التلقائية إلى المرحلة الثابتة.
  - إذا لا يوجد تسكين ثابت: تبقى الحالة `Unassigned` إلى أن يُضاف تسكين ثابت جديد.

## 5) قواعد منع التعارض (مبدئية ومفيدة للتنفيذ)
- عامل واحد لا يمكن أن يكون فعّالًا في أكثر من مرحلة واحدة في نفس الزمن.
- بدل العامل المؤقت `ReplacementForWorkerId` لا يغيّر توقيت انتهاء/فعالية التسكين الثابت للعامل الأصلي.
- إذا كان العامل "بديلًا" ضمن فترة مؤقتة، يقيَّد ذلك بفترة `StartAtUtc` إلى `EndAtUtc` فقط.
- أي تعديل على التسكين خلال فترة حالية يُفحص أولًا ضد حالات وجود تعيين فعال أخرى.

## 6) احتساب نسب الجاهزية (مرجع مفاهيمي)
- **SubStage**:
  - `ReadinessPercent = PresentWorkers / Max(RequiredWorkers, 1) * 100`
  - إذا `Capacity = 0` يمكن حسابها كـ 100% (حسب سياسة الإظهار/التجربة).
- **MainStage**:
  - متوسط مرجّح على `SubStage` في المرحلة الرئيسية.
- **ProductionLine**:
  - متوسط مرجّح على `MainStage` داخل الخط.
- **Factory**:
  - متوسط مرجّح على خطوط المصنع.

## 7) أسئلة مفتوحة (Open Questions)
- هل نحتاج تخزين `StageReadinessSnapshot` كـ snapshot كل 1..5 دقائق أم بناء لحظي فقط؟
- هل يظل `AttendanceState=Late` محسوبًا كـ `Present` أم له تأثير جزئي على الجاهزية؟
- هل نحتاج قيودًا إضافية لتغطية تغييرات الطوارئ (مثلاً حد أقصى للتسكين المؤقت لكل stage)?

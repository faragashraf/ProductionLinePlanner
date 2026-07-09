# 08 - عقود Backend APIs (MVP - توثيق فقط)

## النطاق
- هذا الملف يصف API contracts المطلوبة لتنفيذ الـ Backend فقط (بدون Controllers أو DTO أو أي كود تطبيقي).
- كل العقود هنا قابلة للتنفيذ مباشرة على ASP.NET Core ضمن هذا المشروع.

## قواعد عامة
- Base route: `/api`
- مصادقة: `Authorization: Bearer <access_token>`
- تنسيق التاريخ: UTC ISO-8601 (`2026-07-09T12:00:00Z`)
- هيكل الاستجابة (مستحسن):
  - نجاح: `{"success": true, "data": ..., "message": "OK"}`
  - فشل: `{"success": false, "error": {"code": "ValidationError", "message": "...", "details": [...]}}`

## 1) Authentication

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `POST` | `/api/auth/login` | تسجيل الدخول وإصدار access token | عامة (بدون token) | `{ email, password }` | `{ "accessToken", "refreshToken", "expiresAt", "userId", "roles" }` | التحقق من صحة البريد + الباسورد، معدل محاولات تسجيل الدخول، إرجاع 401 عند فشل. |
| `POST` | `/api/auth/refresh` | تحديث access token | عامة ( refresh token صالح ) | `{ refreshToken }` | `{ accessToken, refreshToken?, expiresAt }` | Rotate token: إعادة إصدار token جديد وإبطال القديم. |
| `GET` | `/api/auth/me` | جلب المستخدم الحالي + الصلاحيات | `SuperAdmin`, `Admin` | - | `{ id, fullName, email, roles, permissions[] }` | إذا token غير صالح يرجع 401. |
| `POST` | `/api/auth/logout` | إبطال جلسة المستخدم | `SuperAdmin`, `Admin` | `{ refreshToken? }` | `{ success: true }` | إن توفر Refresh token يتم حفظه في blacklist/إبطال التخزين. |

## 2) Users & Roles

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/users` | جلب قائمة المستخدمين | `SuperAdmin`, `Admin` | `?page=1&pageSize=20&active=true&search=` | `{ items: [{ id, fullName, email, isActive, roles[] }], totalCount }` | يدعم Pagination + Search. |
| `POST` | `/api/users` | إضافة مستخدم | `SuperAdmin` | `{ fullName, email, password, roleIds[] }` | `{ id, fullName, email, isActive }` | email فريد، password قوية (8+ + حرف/رقم/رمز). |
| `PATCH` | `/api/users/{userId}` | تعديل بيانات المستخدم | `SuperAdmin` | `{ fullName?, email?, isActive? }` | `{ id, fullName, email, isActive }` | لا يمكن تعطيل حسابك أنت خلال جلسة نشطة بدون تأكيد. |
| `POST` | `/api/users/{userId}/roles` | ربط/تحديث Roles للمستخدم | `SuperAdmin` | `{ roleIds: ["SuperAdmin","Admin"] }` | `{ userId, roles: [] }` | التحقق أن الـ roles موجودة ومسموحة. |
| `POST` | `/api/users/{userId}/activate` | تفعيل المستخدم | `SuperAdmin` | - | `{ id, isActive: true }` | تسجيل فعلية في AuditLog. |
| `POST` | `/api/users/{userId}/deactivate` | إيقاف المستخدم | `SuperAdmin` | `{ reason? }` | `{ id, isActive: false }` | منع الدخول للـ system بعد التعطيل. |
| `GET` | `/api/roles` | جلب أدوار النظام | `SuperAdmin`, `Admin` | - | `{ items: [{ id, code, name }] }` | تستخدم في واجهات الإسناد. |

## 3) Factory Structure

### 3.1 Factories

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/factories` | عرض المصانع | `SuperAdmin`, `Admin` | `?active=true` | `{ items: [{ id, name, code, isActive }] }` | ترتيب أبجدي أو حسب `code`. |
| `POST` | `/api/factories` | إنشاء مصنع | `SuperAdmin` | `{ name, code, location?, isActive=true }` | `{ id, name, code }` | `code` فريد، `name` مطلوب. |
| `PATCH` | `/api/factories/{factoryId}` | تعديل مصنع | `SuperAdmin` | `{ name?, location?, isActive? }` | `{ id, name, location, isActive }` | حفظ سجل التعديل. |
| `DELETE` | `/api/factories/{factoryId}` | حذف/أرشفة مصنع | `SuperAdmin` | - | `{ success: true }` | يفضل Soft delete + `isActive=false`. |

### 3.2 Production Lines

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/factories/{factoryId}/production-lines` | خطوط المصنع | `SuperAdmin`, `Admin` | `?includeInactive=false` | `{ items: [{ id, factoryId, name, lineCode, sequenceOrder, isActive }] }` | يتحقق `factoryId` موجود. |
| `POST` | `/api/production-lines` | إنشاء خط إنتاج | `SuperAdmin`, `Admin` | `{ factoryId, name, lineCode?, sequenceOrder, isActive=true }` | `{ id, factoryId, name, lineCode }` | `lineCode` فريد داخل نفس المصنع، ترتيب صحيح. |
| `PATCH` | `/api/production-lines/{lineId}` | تعديل خط إنتاج | `SuperAdmin`, `Admin` | `{ name?, lineCode?, sequenceOrder?, isActive? }` | `{ id, factoryId, name, lineCode, sequenceOrder, isActive }` | تحديث التسلسل لا يؤثر على جاهزية مباشرة. |

### 3.3 Main Stages

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/production-lines/{lineId}/main-stages` | جلب المراحل الرئيسية | `SuperAdmin`, `Admin` | `?includeInactive=false` | `{ items: [{ id, lineId, name, isCritical, sequenceOrder, isActive }] }` | ترتيب حسب `sequenceOrder`. |
| `POST` | `/api/main-stages` | إنشاء مرحلة رئيسية | `SuperAdmin`, `Admin` | `{ productionLineId, name, isCritical=false, sequenceOrder, isActive=true }` | `{ id, productionLineId, name, isCritical }` | `isCritical` يرفع أولوية التنبيهات. |
| `PATCH` | `/api/main-stages/{mainStageId}` | تعديل مرحلة رئيسية | `SuperAdmin`, `Admin` | `{ name?, isCritical?, sequenceOrder?, isActive? }` | `{ id, name, isCritical, sequenceOrder, isActive }` | لا حذف إذا كانت مرتبطة بمرحلة فرعية فعالة. |

### 3.4 Sub Stages

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/main-stages/{mainStageId}/sub-stages` | جلب المراحل الفرعية | `SuperAdmin`, `Admin` | `?includeInactive=false` | `{ items: [{ id, mainStageId, name, capacity, sequenceOrder, isActive }] }` | ترتيب حسب التسلسل داخل المرحلة الرئيسية. |
| `POST` | `/api/sub-stages` | إنشاء مرحلة فرعية | `SuperAdmin`, `Admin` | `{ mainStageId, name, capacity, sequenceOrder, isActive=true }` | `{ id, mainStageId, name, capacity }` | `capacity` عدد صحيح ≥ 0. |
| `PATCH` | `/api/sub-stages/{subStageId}` | تعديل مرحلة فرعية | `SuperAdmin`, `Admin` | `{ name?, capacity?, sequenceOrder?, isActive? }` | `{ id, name, capacity, sequenceOrder, isActive }` | إذا انخفضت السعة وأصبحت أقل من العاملين الحاليين، readiness تحت التحذير. |
| `DELETE` | `/api/sub-stages/{subStageId}` | حذف/أرشفة مرحلة فرعية | `SuperAdmin`, `Admin` | - | `{ success: true }` | Soft delete مفضل للحفاظ على السجلات التاريخية. |

### 3.5 Required Workers per Stage

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/sub-stages/{subStageId}/requirements` | جلب شرط العدد المطلوب | `SuperAdmin`, `Admin` | - | `{ subStageId, requiredWorkers, updatedAt, updatedByUserId }` | derived from `capacity` افتراضياً. |
| `PATCH` | `/api/sub-stages/{subStageId}/requirements` | تعديل العدد المطلوب | `SuperAdmin`, `Admin` | `{ requiredWorkers }` | `{ subStageId, requiredWorkers, previousRequiredWorkers }` | `requiredWorkers` ≥ 0. |

## 4) Workers

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/workers` | قائمة العمال | `SuperAdmin`, `Admin` | `?isActive=true&search=&page=1&pageSize=20` | `{ items: [{ id, employeeCode, fullName, phone, isActive, defaultSubStageId? }], totalCount }` | يدعم فلترة بالحالة/النص. |
| `POST` | `/api/workers` | إنشاء عامل | `SuperAdmin`, `Admin` | `{ employeeCode, fullName, phone?, isActive=true }` | `{ id, employeeCode, fullName }` | `employeeCode` فريد، يمكن إنشائه قبل ربطه بالـ badge. |
| `GET` | `/api/workers/{workerId}` | تفاصيل عامل | `SuperAdmin`, `Admin` | - | `{ id, employeeCode, fullName, phone, isActive, attendanceBadgeCode?, defaultAssignment, activeAssignment }` | تضمن `currentAssignment` محسوبة حالياً. |
| `PATCH` | `/api/workers/{workerId}` | تعديل عامل | `SuperAdmin`, `Admin` | `{ fullName?, phone?, isActive? }` | `{ id, fullName, phone, isActive }` | منع تكرار `employeeCode` إن تغير. |
| `POST` | `/api/workers/{workerId}/attendance-badge` | ربط بطاقة/كود الحضور | `SuperAdmin`, `Admin` | `{ attendanceCode }` | `{ workerId, attendanceCode, linkedAt }` | attendanceCode فريد عبر النظام، يرفض التكرار. |
| `DELETE` | `/api/workers/{workerId}/attendance-badge` | فك ربط البطاقة | `SuperAdmin`, `Admin` | - | `{ workerId, attendanceCode: null }` | لا تمس التسكين أو الحضور القديم. |
| `GET` | `/api/workers/{workerId}/current-assignment` | جلب التعيين النشط (ثابت/مؤقت) | `SuperAdmin`, `Admin` | - | `{ workerId, effectiveSubStageId, assignmentType, startedAt, endsAt, replacementForWorkerId? }` | يطبق precedence: المؤقت الفعّال ثم الثابت ثم Unassigned. |
| `PATCH` | `/api/workers/{workerId}/default-assignment` | تعيين/تعديل التسكين الثابت | `SuperAdmin`, `Admin` | `{ subStageId }` | `{ workerId, subStageId, assignedAt }` | إذا يوجد تعيين ثابت فعّال سابق: إلغاء وتبديل القديم ضمن transactional logic. |

## 5) Worker Assignments

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `POST` | `/api/assignments/default` | إنشاء أو تحديث التسكين الثابت | `SuperAdmin`, `Admin` | `{ workerId, subStageId, reason? }` | `{ assignmentId, workerId, subStageId, startedAt, status: "Active" }` | يحافظ على قاعدة "تسكين ثابت واحد فعّال لكل عامل". |
| `POST` | `/api/assignments/temporary` | إنشاء تعيين مؤقت | `SuperAdmin`, `Admin` | `{ workerId, fromSubStageId, toSubStageId, startAtUtc, endAtUtc, reason }` | `{ assignmentId, workerId, fromSubStageId, toSubStageId, startAtUtc, endAtUtc, status }` | يتحقق عدم تداخل الفترات لنفس العامل. |
| `POST` | `/api/assignments/temporary/move` | drag & drop move endpoint | `SuperAdmin`, `Admin` | `{ workerId, toSubStageId, startAtUtc, endAtUtc?, isReassignFromCurrent=true }` | `{ assignmentId, workerId, fromSubStageId, toSubStageId, startAtUtc, endAtUtc }` | إن كان `endAtUtc` فارغ يعتبر "مؤقت فوري مفتوح" بحد أقصى policy. |
| `POST` | `/api/assignments/replacement` | تعيين عامل بديل لغياب آخر | `SuperAdmin`, `Admin` | `{ replacementWorkerId, replacedWorkerId, subStageId, startAtUtc, endAtUtc, reason }` | `{ assignmentId, replacementWorkerId, replacedWorkerId, subStageId, status }` | يطلب `replacementWorkerId` له صلاحية Stage availability ضمن الفترة. |
| `DELETE` | `/api/assignments/temporary/{assignmentId}` | إلغاء تعيين مؤقت | `SuperAdmin`, `Admin` | - | `{ assignmentId, cancelledAt, status: "Cancelled" }` | يلغي فقط المؤقت الفعّال/المجدول، ثم إعادة worker للتسكين الثابت إن وجد. |
| `GET` | `/api/assignments/history` | عرض تاريخ التعيينات | `SuperAdmin`, `Admin` | `?workerId=&subStageId=&fromDate=&toDate=&page=&pageSize=` | `{ items:[{ assignmentId, workerId, fromSubStageId, toSubStageId, type, startAtUtc, endAtUtc, status }] , totalCount }` | ترتيب من الأحدث للأقدم، يدعم التصفية. |

## 6) Attendance Integration

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `POST` | `/api/attendance/sync/today` | تشغيل مزامنة الحضور لليوم الحالي | `Admin` | - | `{ syncDateUtc, evaluatedWorkers, syncedWorkers, insertedRecords, updatedRecords, unchangedRecords, unmatchedSourceRows, workersWithMissingMapping }` | يقرأ من Attendance DB (read-only) ويكتب/يحدث `AttendanceRecord` ضمن FactoryPlannerDB. |
| `GET` | `/api/attendance/today` | حالة الحضور لليوم الحالي | `Auth` | `?dateUtc=&factoryId=&lineId=` | `{ date, items:[{ workerId, employeeCode, fullName, attendanceStatus, attendanceTimeUtc, source, attendanceUserId, badgeNumber }] }` | يجلب snapshot لآخر حالة حضور لكل عامل بتاريخ اليوم. |
| `GET` | `/api/attendance/workers/{workerId}` | سجل حضور عامل | `Auth` | `?fromDateUtc=&toDateUtc=` | `{ workerId, items:[{ id, attendanceTimeUtc, attendanceStatus, source, attendanceUserId, badgeNumber, sourceRawId }] }` | يدعم نطاق تاريخ UTC اختياري بدون تقطيع pagination في M3. |
| `GET` | `/api/attendance/stages/{subStageId}` | حالة حضور مرحلة فرعية | `Auth` | `?dateUtc=` | `{ subStageId, subStageName, dateUtc, capacity, assignedWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers, workers:[...] }` | يعتمد على التسكين الفعّال لنفس التاريخ/الوقت. |

## 7) Readiness Dashboard

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/readiness/factories/{factoryId}` | جاهزية المصنع كاملًا | `SuperAdmin`, `Admin` | `?asOfUtc=` | `{ scopeType:"Factory", scopeId, readinessPercent, requiredWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers, updatedAt }` | يحسب من snapshots أو لحظياً حسب policy. |
| `GET` | `/api/readiness/lines/{lineId}` | جاهزية خط إنتاج | `SuperAdmin`, `Admin` | `?asOfUtc=` | `{ scopeType:"ProductionLine", scopeId, readinessPercent, mainStages:[...] }` | يضم breakdown كل MainStage. |
| `GET` | `/api/readiness/stages/{subStageId}` | جاهزية مرحلة فرعية | `SuperAdmin`, `Admin` | `?asOfUtc=` | `{ scopeType:"SubStage", subStageId, readinessPercent, requiredWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers }` | يعتمد على `requiredWorkers` الحالية في القسم 3.5. |
| `GET` | `/api/readiness/stages/{subStageId}/workers` | حالة العمال المعينين بالمرحلة | `SuperAdmin`, `Admin` | `?asOfUtc=` | `{ subStageId, workers:[{ workerId, fullName, assignmentType, attendanceState, isLate, isActive }] }` | يعرض العاملين غير المسكنين إن طلب `includeUnassignedWorkers=true`. |
| `GET` | `/api/readiness/snapshots` | جلب لقطات جاهزية تاريخية | `SuperAdmin`, `Admin` | `?factoryId=&fromDate=&toDate=&scopeType=` | `{ items:[{ scopeType, scopeId, calculatedAtUtc, readinessPercent, requiredWorkers, presentWorkers }] }` | يستخدم لعرض trend dashboard. |

## 8) Notifications

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/notifications` | قائمة الإشعارات | `SuperAdmin`, `Admin` | `?isRead=false&severity=&type=&page=` | `{ items:[{ id, title, message, severity, type, isRead, createdAt, relatedEntityType, relatedEntityId }], totalCount }` | ترتيب تنازلي من الأحدث. |
| `GET` | `/api/notifications/unread-count` | عدد غير المقروء | `SuperAdmin`, `Admin` | - | `{ unreadCount }` | سريع جدًا، cacheable قصيرة المدة. |
| `PATCH` | `/api/notifications/{notificationId}/read` | تعليم إشعار كمقروء | `SuperAdmin`, `Admin` | - | `{ id, isRead: true, readAt }` | لا يحتاج body. |
| `PATCH` | `/api/notifications/mark-all-read` | تعليم كل الإشعارات كمقروءة | `SuperAdmin`, `Admin` | `{ beforeDateUtc? }` | `{ updatedCount }` | إذا أُرسل `beforeDateUtc` يحد فقط الأقدم من التاريخ. |

## 9) SignalR Events

| Method | Route | الغرض | الدور المطلوب | Request body مختصر | Response shape مختصر | Notes / validation |
|---|---|---|---|---|---|---|
| `Event` | `/hubs/production` + `attendance.updated` | إشعار بتغيير حالة حضور عامل | `SuperAdmin`, `Admin` | `{ workerId, oldState, newState, stateTimeUtc, source }` | push message للعميل | يدعم إعادة اتصال + إعادة إرسال events غير المستلمة في أول 30 ثانية. |
| `Event` | `/hubs/production` + `assignment.changed` | تغيير تعيين عامل | `SuperAdmin`, `Admin` | `{ workerId, previousSubStageId, newSubStageId, assignmentType, startAtUtc, endAtUtc }` | push message للعميل | مهم جدًا لواجهة drag/drop اللحظية. |
| `Event` | `/hubs/production` + `readiness.recalculated` | إعادة احتساب جاهزية | `SuperAdmin`, `Admin` | `{ scopeType, scopeId, readinessPercent, calculatedAtUtc }` | push message للعميل | يرسل بعد كل كتابة على readiness المتأثرة. |
| `Event` | `/hubs/production` + `notification.created` | إشعار جديد تم إنشاؤه | `SuperAdmin`, `Admin` | `{ notificationId, title, severity, createdAt }` | push message للعميل | العميل يلتقط مع عدد غير المقروء من endpoint. |

## 10) Audit Log

| الطريقة | المسار | الغرض | الدور المطلوب | Request body مختصر | شكل الاستجابة | ملاحظات / قواعد تحقق |
|---|---|---|---|---|---|---|
| `GET` | `/api/audit-logs` | جلب سجلات المراجعة | `SuperAdmin` | `?userId=&action=&entityType=&entityId=&fromDate=&toDate=&page=&pageSize=` | `{ items:[{ id, actorUserId, action, entityType, entityId, createdAt, requestMeta }], totalCount }` | قراءة فقط، لا تعديل. |

## ملاحظات التنفيذ المقترحة بعد التوثيق
- تنفيذ التحقق من الصلاحيات من خلال Policy:
  - `RequireRole("SuperAdmin")`
  - `RequireRole("SuperAdmin","Admin")`
- وضع قواعد تمنع تضارب التعيين (`WorkerTemporaryAssignment`) على مستوى الخدمة قبل الحفظ.
- تدوين جميع تغييرات التسكين والإشعارات والتهيئة في `AuditLog`.
- الاعتماد على `factoryId` في الـ scope لتفادي تسريب بين المصانع مستقبلاً.

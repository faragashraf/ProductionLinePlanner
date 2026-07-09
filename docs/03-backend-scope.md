# 03 - نطاق Backend (ASP.NET Core)

## مسؤوليات Backend الأساسية
- إدارة المستخدمين، الأدوار، الصلاحيات.
- إدارة هيكلة المصنع (خطوط/مراحل/مرحلة فرعية).
- إدارة تخصيصات العمال الثابتة.
- إدارة التعيين المؤقت وقواعد الرجوع.
- قراءة حالة الحضور من ZKTime (واجهة قراءة مبدئية).
- حساب نسب الجاهزية لكل مرحلة/خط.
- إدارة الإشعارات اللحظية (SignalR).
- تقديم تقارير قصيرة جاهزة للواجهة.

## واجهات API المقترحة (فهرسة)
### Auth / Security
- `POST /api/auth/login`
- `GET /api/auth/me`
- `GET /api/auth/permissions`

### Production Lines
- `GET /api/lines`
- `GET /api/lines/{id}`
- `POST /api/lines`
- `PUT /api/lines/{id}`
- `DELETE /api/lines/{id}`

### Stages
- `GET /api/lines/{lineId}/stages`
- `POST /api/stages`
- `PUT /api/stages/{id}`
- `POST /api/stages/{id}/reorder` (اختياري MVP)

### Workers
- `GET /api/workers`
- `GET /api/workers/{id}`
- `PATCH /api/workers/{id}/default-stage`

### Assignments
- `GET /api/assignments/current`
- `POST /api/assignments/default`
- `PUT /api/assignments/{id}`
- `POST /api/assignments/temporary`
- `DELETE /api/assignments/temporary/{id}`

### Attendance/Readiness
- `GET /api/readiness/lines`
- `GET /api/readiness/line/{lineId}`
- `GET /api/readiness/worker/{workerId}`

### Notifications
- `GET /api/notifications`
- `PATCH /api/notifications/{id}/read`
- `PATCH /api/notifications/mark-all-read`

## منطق التعيين المؤقت
- يتحقق Backend عند الإنشاء من:
  - عدم تعارض تعيين العامل في نفس الفترة.
  - وجود صلاحية أدق من المرحلة المستهدفة.
- عند انتهاء التاريخ النهائي:
  - مهمة جدولة/Background job لإعادة تعيين worker.
  - في حال وجود defaultAssignment => استرجاعه تلقائيًا.
  - خلاف ذلك تحويل الحالة إلى `Unassigned`.

## أحداث SignalR
- قنوات مقترحة:
  - `line-status-updated`
  - `worker-assignment-changed`
  - `attendance-updated`
  - `notification-added`

## سياسات الأمن
- Authorization policies على مستوى endpoints.
- منع الوصول العرضي للبيانات بين الأقسام إن وُجد تعدد أقسام.
- تدقيق تغييرات الإسناد الحساسة (من، إلى، زمن، مستخدم التعديل).

## قواعد الجودة والقيود
- لا استعلامات معقدة في نهاية واحدة بدون تفويض واضح.
- لا نشر مباشرة من فرع غير مراجع إلى `main`.
- كل API ترجع أخطاء واضحة قابلة للعرض للموبايل.

## أسئلة مفتوحة
- هل يكون التحقق من التعارض على أساس “تاريخ نهائي فقط” أم “فترة زمنية كاملة”؟
- هل توجد حدود على عدد التعيينات غير المنتهية لكل عامل؟
- هل يلزم دعم إلغاء مؤكد لتعطيل تعيين فعلي عبر سبب.

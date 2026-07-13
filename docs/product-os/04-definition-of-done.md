# Definition of Done

## Checklist عامة لكل Capability

### Backend
- [ ] APIs مغطاة باختبارات (unit/integration) للمسارات الأساسية.
- [ ] Services وEngine مفصولة عن الطبقة التعريفية.
- [ ] لا يوجد منطق عمل مكرر بين الخدمات/الـ endpoints.

### Frontend
- [ ] الحالة (state) واضحة ومحدودة scope.
- [ ] UX مطابق للسيناريوهات المحددة.
- [ ] معالجة أخطاء API واضحة للمستخدم.

### Database
- [ ] النماذج/Data contracts تناسب التغييرات المطلوبة.
- [ ] migration/versioning موثق للـ schema.
- [ ] فهارس/قيود integral حيث يلزم.

### Security
- [ ] صلاحيات موحدة في backend.
- [ ] تسجيل دخول/إداريات حساسة ضمن صلاحيات دقيقة.
- [ ] لا يوجد تسريبات بيانات (PII/secret).

### Permissions
- [ ] كل endpoint له شرط صلاحية واضح.
- [ ] حالات المستخدم المصرّح له/غير المصرّح له مغطاة.
- [ ] أي role/permission جديد موثق في Bible/OS.

### Audit
- [ ] عمليات تعديل حساسة لها audit trails.
- [ ] الأحداث الحرجة تُسجل مع user/context/time.

### Excel
- [ ] استيراد/تصدير (إن وجد) فيه validation، ونموذج أعطال واضح.
- [ ] دعم ملفات غير مكتملة وعدم كسر السير.

### Tests
- [ ] unit/integration/e2e حسب نوع capability.
- [ ] تغطية سيناريوهات الفشل وليس النجاح فقط.
- [ ] pass rate مستهدف لا يقل عن الحدود المتفق عليها.

### UX
- [ ] لا يوجد blocking أو ambiguous states.
- [ ] accessibility أساسية (labels، حالة التحميل، رسائل الخطأ).
- [ ] تنسيق mobile-first في الواجهات ذات الصلة.

### Skeleton Loading
- [ ] skeleton/placeholder للحالات المتأخرة.
- [ ] لا يوجد فجوات blank state غير مبررة.

### Product Bible
- [ ] أي تعديل وظيفي/مفاهيمي في السلوك تم عكسه في Bible.
- [ ] عدم وجود تعارض مع النصوص الحالية في Bible.

### Review
- [ ] Architecture Review مكتملة.
- [ ] Security Review منسقة حسب المخاطر.
- [ ] الملاحظات الحرجة/العالية مغلقة بالكامل.

### Validation
- [ ] نتائج validation مثبتة في التذاكر/الملاحظات.
- [ ] فحص smoke قبل التقدم إلى checkpoint.

### Checkpoint
- [ ] التحقق النهائي من جميع أقسام DoD ذات الصلة.
- [ ] اعتماد مسئول الـ capability قبل Merge.

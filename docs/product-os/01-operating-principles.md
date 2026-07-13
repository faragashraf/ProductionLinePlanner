# Operating Principles

## 1) Capability First

البدء من `capability` وليس من Ticket أو صفحة منفصلة.
كل عمل يجب أن يجيب عن:
- ما القدرة التي نُضيفها؟
- ما القيمة التي تغيّرها في المنتج؟
- كيف نتأكد من اكتمالها؟

## 2) Product Bible First

لا نُنتج تنفيذات جديدة تخالف Bible.
إذا وُجد تضارب، تُحدَّث Bible أولاً أو يتم إعادة تصميم capability قبل التنفيذ.

## 3) Security by Default

الأمان افتراضي داخل التصميم، وليس طبقة تالية بعد الدمج.
المبادئ الأساسية: least privilege, input validation, auditability, secure defaults.

## 4) Business before Hardening

المخرجات التجارية (Business Outcomes) تُثبت القدرة أولاً.
بعد ذلك تُضاف إجراءات Hardening حسب درجة المخاطر والتأثير.

## 5) No duplicated business logic

منع تكرار منطق العمل في أكثر من طبقة/نقطة نهاية.
يُسجّل المنطق في خدمات القدرات القابلة لإعادة الاستخدام.

## 6) Thin Endpoints

Endpoints في API تكون thin:
- التحقق من الصلاحية/التحويل السطحي.
- لا تحتوي منطقًا تجاريًا كبيرًا.
- تعتمد على الخدمات/الأجهزة الدومين/الـ engines.

## 7) Backend is Security Authority

التحكم في الأدوار والصلاحيات والتحقق الأمني النهائي يكون في backend.
الـ frontend يوجّه تجربة المستخدم فقط، لا يُعد مصدراً للحسم الأمني.

## 8) Source of Truth

- النماذج والداتا تكون المصدر الوحيد للحقيقة.
- قواعد البنية والتصميم في `docs/product-bible`.
- حالات التنفيذ ومخرجات المراجعات في Product OS.

## 9) Review before Merge

لا يجوز Merge بدون:
- Architecture Review مكتملة.
- Checkpoint موثق.
- DoD مقفلة لكل البنود ذات الصلة.

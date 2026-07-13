# Product Operating System

## ما هو Product OS؟

**Product Operating System (Product OS)** هو النظام التشغيلي الرسمي لإدارة تطوير المنتج في المشروع.
هو مجموعة قواعد ثابتة ومسارات عمل قابلة للتنفيذ تقرر **متى** نعمل، **كيف** نتحقق من الجودة، و**متى** نُغلق التغيرات للدمج.

يصف Product OS:

- متى وكيف نبدأ capability جديدة.
- كيف نربط القرارات بين البنية والتطبيق والتوثيق.
- متى نعد الكود جاهزًا لمراجعة Merge.
- متى نُدخل مرحلة Hardening بعد الدمج أو قبل Release.

## علاقته بـ Product Bible

`Product Bible` يعرّف **ماذا** يجب بناؤه (الحُكم التجاري، النطاق، القواعد، المعاني).
أما `Product OS` فيعرّف **كيف** نبني ذلك بشكل منضبط وقابل للتكرار.

- Bible = المرجع الوظيفي والسياسي للحل.
- OS = قواعد التشغيل اليومي لبناء وتحرير ومراجعة الحل.

الرابط المرجعي: [Product Bible](../product-bible/README.md)

## Quick Start للمطور الجديد (5 دقائق)

1. اقرأ [Operating Principles](01-operating-principles.md) ثم [Delivery Workflow](02-delivery-workflow.md) لفهم قواعد العمل والمسار الإلزامي.
2. افتح [Capability Index](03-capability-board.md) وحدد ملف الـ capability الذي ستعمل عليه داخل [`capabilities/`](capabilities/).
3. راجع أو أكمل **Definition of Done** الخاصة بالـ capability، بالاستناد إلى [القائمة العامة](04-definition-of-done.md) و[القالب](templates/definition-of-done-template.md).
4. نفّذ ضمن الفرع والنطاق المسجلين، واتبع [Branch Strategy](05-branch-strategy.md) و[AI Development Playbook](08-ai-development-playbook.md).
5. قبل طلب المراجعة، أكمل [Review Policy](06-review-policy.md)، وثّق نتائج التحقق، وأنشئ Checkpoint. لا يتم الـ Merge إلا بعد استيفاء [Checkpoint Policy](11-checkpoint-policy.md) وإغلاق الملاحظات المانعة.

## Lifecycle الكامل لكل Capability

كل قدرة جديدة تمرّ بهذه الدورة:

1. **التخطيط (Capability Definition)**: تعريف النطاق، حالات النجاح، المخاطر، واعتمادية السجل في ملف capability مستقل.
2. **الأرشفة التصميمية (Architecture)**: توثيق الحل المبدئي قبل التنفيذ.
3. **Bible Update**: تحديث Product Bible عند إضافة أو تعديل حكم تجاري أو نطاق وظيفي.
4. **التنفيذ**: تنفيذ backend/frontend/test وفقًا لنقاط الإنجاز.
5. **Architecture Review**: مراجعة مطابقة الحل للبنية.
6. **Fixes**: معالجة ملاحظات المراجعة.
7. **Validation**: اختبارات موثّقة وتشغيل smoke checks.
8. **Checkpoint**: اعتماد استقرار capability قبل الدمج/النشر التجريبي.
9. **Merge**: دمج التغييرات بعد اجتياز مراجعة الجودة.
10. **Hardening**: إغلاق ثغرات الجودة/الأمان/الاستقرار المتأخرة وفقًا لـ [Hardening Policy](07-hardening-policy.md).

التفصيل الرسمي للمراحل وحالات الانتقال موجود في [Capability Lifecycle](10-capability-lifecycle.md).

## Workflow العام

```mermaid
flowchart LR
    A[Architecture] --> B[Definition of Done]
    B --> C[Bible Update]
    C --> D[Implementation]
    D --> E[Architecture Review]
    E --> F[Fixes]
    F --> G[Validation]
    G --> H[Checkpoint]
    H --> I[Merge]
    I --> J[Hardening]
```

## كيف يُستخدم هذا النظام

- أي capability لا تبدأ بوضوح في `Definition of Done` تعتبر غير مقبولة للبدء.
- أي capability لا تنتقل عبر المراجعة والـ checkpoint لا يمكن المضيّ في Merge.
- الحقول المذكورة هنا تُصبح مرجعًا إلزاميًا للفرق والأتمتة المستقبلية.

# Checkpoint Policy

## الغرض

الـ Checkpoint هو سجل اعتماد قابل للتدقيق يثبت أن Capability اجتازت DoD والمراجعة والتحقق، وهو شرط للـ Merge وليس بديلاً عنه.

## Checkpoint

ينشئ مسؤول الـ capability Checkpoint بعد Validation ويضم:

- رابط أو مرجع سجل capability وDefinition of Done المكتملة.
- نتائج الاختبارات وsmoke validation ذات الصلة.
- حالة Architecture Review وTerra Validation.
- قائمة الملاحظات المفتوحة مع التصنيف والمالك والموعد، إن وجدت.
- قرار واضح: `Approved for Merge` أو `Not Approved`.

لا يُعتمد Checkpoint مع ملاحظة `Critical` أو `High` مفتوحة، أو مع بنود DoD إلزامية غير محققة.

## Commit

- الـ commit هو نقطة حفظ تقنية صغيرة ومركزة، وليس اعتمادًا للجودة أو الإطلاق.
- يصف التغيير بوضوح ويربطه بالـ capability عندما يكون ذلك ممكنًا.
- لا يحل commit محل مراجعة أو Checkpoint، ولا يعني وحده أن العمل صالح للـ Merge.

## Merge

يُسمح بالـ Merge فقط عندما يكون Checkpoint معتمدًا، وReview Status مكتملًا، ولا توجد ملاحظات مانعة بحسب [Hardening Policy](07-hardening-policy.md). بعد الـ Merge تُحدّث حالة capability إلى `Merged` وتوثق أي أعمال متابعة.

## Hardening Branch

- تستخدم `hardening/*` لإصلاحات الجودة أو الأمان أو الاستقرار التي لا توسع القيمة التجارية المعتمدة.
- يجب أن يرتبط اسم الفرع بالـ capability والملاحظة، مثل `hardening/iam-audit-retention`.
- ملاحظات `Critical` و`High` تظل مانعة للـ Merge الخاص بنطاقها؛ لا تستخدم hardening لتأجيل إصلاح مانع.
- يخضع فرع hardening للمراجعة والتحقق وCheckpoint مناسبين لحجم الخطر.

## Release

الـ Release قرار نشر، لا قرار دمج. يتطلب capability مدموجة، Checkpoint مرجعيًا، ملاحظات release، وsmoke validation في بيئة النشر. إذا ظهر عيب بعد النشر، يُصنف ويُدار عبر `hotfix/*` أو `hardening/*` وفق شدته.

## مسؤوليات القرار

| قرار | المسؤول عن التحضير | المعتمد |
| --- | --- | --- |
| Commit | المنفذ | لا يتطلب اعتمادًا مستقلًا |
| Checkpoint | مسؤول الـ capability | المراجع المعتمد |
| Merge | مسؤول الـ capability | المراجع المعتمد وفق Review Policy |
| Hardening scope | مسؤول الـ capability | مالك المنتج/المراجع وفق المخاطر |
| Release | Release owner | مالك المنتج أو المفوض |

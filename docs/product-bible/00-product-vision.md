# 00 - Product Vision

## Why

إدارة التشغيل اليومي في المصنع تحتاج مرجعًا مركزيًا للصناعة وليس مجرد CRUD. هذا الـBible يثبت أن المنتج مبني حول **المحركات + الصلاحيات + التكاملات المتحكّم فيها**.

## Scope

- Confirmed: التكامل الأساسي مع ZK attendance موجود في الموديل الحالي.
- Approved: بناء مرجعية معمارية قابلة للتوسع عبر Capability Contexts.
- Deferred: تنفيذ كل capability (خصوصًا Payroll/Production earnings) يتم على مراحل.

## Scope of this document

- توثيق القرارات في 6 مجالات: العمال، الأقسام، ZKTime، الرواتب، الإنتاج، الأذونات.
- تحديد الفجوات المعمارية الحالية وعدم معالجتها كـ code في هذه المرحلة.

## Non-goals (هذا المرحلة)

- لا تنفيذ backend/frontend.
- لا migrations.
- لا runtime experiments.

## Strategic principle

- **Capability first, not screen first.**
- **Factory Planner source of operational truth stays inside domain DB except external attendance source.**

## Outcome

ننتج مرجعًا رسميًا يمكن تحويله لخطط تنفيذ واضحة دون تكرار الحقائق بين الملفات.

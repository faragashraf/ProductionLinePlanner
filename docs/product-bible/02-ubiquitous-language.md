# 02 - Ubiquitous Language

## Why

منع اختلاف معنى نفس المصطلح بين frontend/backend/UX.

## Core language

- **Worker**: عامل فعلي في المصنع (مرجع تشغيل).
- **Worker identity link**: `AttendanceUserId`/`BadgeNumber` كمفاتيح مصدرية من ZKTime.
- **Capability**: إجراء يسمح بفعل وظيفي محدد (`workers.view`, `compensation.manage`).
- **Production quantity**: الكمية الفعلية المنتجة في فترة زمنية، وتُسجّل مرة واحدة.
- **Worker allocation**: مساهمة عامل في مرحلة (نسبة / دور / نوع أجر).
- **Compensation unit**: نتيجة تحويل كمية/وقت/نمط أجر.

## Confirmed definitions

- Present/Absent/Late/Unassigned مذكورة ضمن منطق الحضور في الكود الحالي (`AttendanceRecord`, `AssignmentEngine`, `AttendanceEngine`).

## Planned definitions

- **Production model** يربط Main/Sub stage مع Product model configs.
- **Role grants + user grants + user deny**.
- **Permission catalog versioned**.

## Rejected definitions

- `Salary` داخل `Worker` كقيمة مباشرة. هذا اللفظ لن يُستخدم للمنطق النهائي.

## English/Arabic naming

- تُستخدم أسماء الكفاءات بوضوح بالإنجليزية داخل API (`workers.manage`...) مع شرح عربي موازٍ.

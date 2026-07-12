# 07 - Employee Administration

## Why

الإدارة الصحيحة للعاملين أساس أي readiness صحيح.

## Status by classification

- Confirmed: `Worker` و`AttendanceUserId` + `BadgeNumber` موجودة.
- Planned: فصل كتابة ZK keys من التعديلات العامة.
- Approved: Controlled-write service interfaces للقراءة/الكتابة.

## Target entities

- `Worker` (مرجع هوية محلية + روابط attendence ids).
- `Department` (local)
- `WorkerEmploymentStatus`

## Rules

1. **Attendance identity key ثابت**
   - `AttendanceUserId` هو المفتاح المرجعي.
   - الاسم غير identifier.

2. **BadgeNumber read-only in V1 unless justified**
   - إذا يوجد دليل قوي ومؤكد: can be overridden داخل flow رسمي.

3. **Controlled writes**
   - الاسم.
   - القسم.
   - حالة العامل (فعال/غير فعال).
   - الصورة (اختياري إذا وجد دعم آمن).
   - القسم.

## APIs (Planned)

- `PATCH /api/workers/{id}` with explicit DTOs and action audit.
- `GET /api/workers` with department filters and status filters.

## Current conflict

- `UpdateWorkerRequest` يسمح تعديل `AttendanceUserId` مباشرة الآن (needs governance override).

## Alternative rejected

- Allowing direct badge sync overwrite without conflict policy.

## Failure handling

- Lock out write when duplicate external IDs exist.
- Conflict on unmatched external keys => operation rejected with recovery workflow.

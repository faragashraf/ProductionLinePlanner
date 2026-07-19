# 07 - Employee Administration

## Why

الإدارة الصحيحة للعاملين أساس أي readiness صحيح.

## Status by classification

- Confirmed: `Worker` و`AttendanceUserId` + `BadgeNumber` موجودة.
- Confirmed: صور العامل محلية الملكية وتُقدّم عبر API محمي ومراجع hash-versioned.
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

4. **Worker photo ownership**
   - `ProductionLinePlanner` هو المالك الوحيد للصورة المحلية المعتمدة.
   - `ZKTime.USERINFO.PHOTO` مصدر read-only ولا توجد writer interface له.
   - اعتماد صورة محلية يمنع أي استبدال تلقائي لاحق من المصدر الخارجي.
   - المحتوى يُخزن خارج `wwwroot`، و`Worker.PhotoReference` يحمل local API URL مع SHA-256 version.

## APIs (Planned)

- `PATCH /api/workers/{id}` with explicit DTOs and action audit.
- `GET /api/workers` with department filters and status filters.

## Photo APIs (Confirmed)

- `GET /api/workers/{id}/photo?v={sha256}` requires `workers.view`.
- `PUT /api/workers/{id}/photo` requires `workers.manage` and accepts multipart field `photo`.
- `DELETE /api/workers/{id}/photo` requires `workers.manage`.
- Allowed uploads: structurally validated JPEG, PNG, and BMP, up to 5 MiB.
- Missing content returns 404/no-store so the existing local avatar placeholder is used.

## Current conflict

- `UpdateWorkerRequest` يسمح تعديل `AttendanceUserId` مباشرة الآن (needs governance override).

## Alternative rejected

- Allowing direct badge sync overwrite without conflict policy.

## Failure handling

- Lock out write when duplicate external IDs exist.
- Conflict on unmatched external keys => operation rejected with recovery workflow.

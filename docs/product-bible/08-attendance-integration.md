# 08 - Attendance Integration

## Why

المصدر الزمني للحضور لا ينبغي أن يصبح مرجعًا داخليًا للهوية فقط، بل مصدر حالة operational-time.

## Confirmed

- `AttendanceSourceOptions` + `AttendanceDbContext` للقراءة من `USERINFO` و`CHECKINOUT`.
- `AttendanceSyncService.SyncTodayAsync` يقرأ CHECKINOUT ويحدّث/ينشئ `AttendanceRecord`.

## Approved target

- Keep `Attendance` source read-only at DB level.
- Sync result idempotent by date and worker.
- No planner-side writes to CHECKINOUT.

## Target model

- `AttendanceRecord`: internal immutable business snapshot per worker/time.
- Snapshot of source id `SourceRawId` preserved for traceability.

## Conflict and migration

- Architecture Conflict: Current sync performs insert/update on Planner `AttendanceRecords` while reading attendance source — صحيح وظيفيًا كـ ingestion layer، لكن يلزم فواصل تشغيلية وعمليات تشغيل فشل واضحة.
- Approved: Introduce operation metadata on sync for visibility (`sync status`, `updated`, `inserted`, `skipped`).

## Failure handling

- إذا فشل الاتصال بالمصدر: fail clean مع error code.
- إن لم يوجد mapping لأي worker: نتيجة واضحة في `unmatchedSourceUsersCount`.

## API direction

- `POST /api/attendance/sync/today` -> `attendance.sync`.
- `GET /api/attendance/today`, `/workers/{id}`, `/stages/{id}`.

## Deferred

- real-time push from source (webhook) — بدايةً عبر sync job.

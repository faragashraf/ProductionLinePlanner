# 01 - Status & Evidence

## Confirmed (ملخص الأدلة الحالية)

1. **AuthN/AuthZ تعتمد أدوارًا ثابتة فقط**
   - `src/backend/ProductionLinePlanner.Api/Program.cs`: `AddAuthorization` ينشئ سياسات `SuperAdmin` و `Admin` فقط.
   - المجموعات الحالية: `MapGroup(...).RequireAuthorization("Admin")` أو `RequireAuthorization("SuperAdmin")`.

2. **worker identities تحمل مفاتيح ZK في نفس الكيان**
   - `src/backend/ProductionLinePlanner.Domain/Entities/Worker.cs` فيه `AttendanceUserId`, `BadgeNumber`.
   - طلبات الإنشاء/التعديل تشمل هذه الحقول (`CreateWorkerRequest`, `UpdateWorkerRequest`).

3. **نموذج الأجور/الراتب التاريخي غير موجود**
   - لا يوجد `WorkerSalaryHistory` ضمن `src/backend/ProductionLinePlanner.Domain/Entities/*`.
   - لا يوجد Salary على مستوى ProductModel أو Stage.

4. **نموذج الإنتاج لا يحتوى Stage pricing/time**
   - `MainStage` / `SubStage` موجودان فقط (`src/backend/ProductionLinePlanner.Domain/Entities/MainStage.cs`, `SubStage.cs`).
   - لا توجد `ProductModel`, `ProductModelStage`, `ProductionStageOutput`, `WorkerAllocation`.

5. **إدماج ZKTime: القراءة + تحديث AttendanceRecord داخل Planner DB**
   - `src/backend/ProductionLinePlanner.Infrastructure/Attendance/AttendanceSyncService.cs` يقرأ `CHECKINOUT` ويكتب/يُحدّث `AttendanceRecords`.

6. **إدارة الأدوار الحالية في المستخدمين**
   - `AppUser`, `AppRole`, enum `UserRole` موجودة في `src/backend/ProductionLinePlanner.Domain/Entities`.

7. **واجهة المستخدم الحالية لا تحتوي canMatch/permission framework**
   - `app-routing.module.ts` يستخدم guard `AuthGuard` + `RoleGuard` فقط.
   - التنقل في `AppShellComponent` ثابت من ملف واحد (navigationItems).

8. **لا يوجد واجهات إدارة صلاحيات/claims ديناميكية فعلية**
   - `AuthTokenService.ResolvePermissionsForRoles` ترجع صلاحيات hard-coded:
     - `users.read`, `users.write`, `system.read`, `system.write`.

9. **Endpoint `GET /api/auth/me` يعيد الأدوار + صلاحيات لحظية**
   - موجود في `Program.cs` ويعيد `roles + permissions` من المستخدم الحالي.

10. **لا يوجد audit منفصل لنطاق صلاحيات المستخدمين**
   - لا endpoints لإدارة صلاحيات/roles في `Program.cs` حاليًا.

## Architecture Conflicts

| موجود حاليًا | السلوك المستهدف | المخاطر | Migration phase |
|---|---|---|---|
| role checks ثابتة في endpoints | Capability-based permissions | توسع الصلاحيات يصبح risk-prone | Phase 1 |
| worker write يسمح تحديث AttendanceUserId وBadge مباشرة | ربط ثابت بـexternal id + تغييرات ضوابطية | أخطاء ربط الهوية | Phase 1+2 |
| لا يوجد Salary history | WorkerSalaryHistory + CompensationMode | ارتكاز راتب داخل Worker | Phase 4-5 |
| لا يوجد ProductModel pricing | سعر/وقت لكل ProductModelStage | تضخيم قرارات إنتاج | Phase 5 |
| لا يوجد ProductionStageOutput / WorkerAllocation | تفريق الإنتاج الكمي عن عدد المساهمات | تضخيم كمية الإنتاج | Phase 6 |

## Rejected

- رفض بناء صلاحيات UI role-string منفصلة لكل شاشة دون policy موحد.

## Deferred

- RBAC ديناميكي متعدد المؤسسة multi-tenant.
- IDOR hardening تفصيلي على كل FK-scoped query (يُعالج لاحقًا ضمن Security Gates).

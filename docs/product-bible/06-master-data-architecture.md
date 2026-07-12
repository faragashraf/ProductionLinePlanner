# 06 - Master Data Architecture

## Why

Master data يحكم اتساق التنفيذ وتشغيل الصلاحيات والرواتب.

## Decision summary

- **Approved**: Separate master-data domains: Employee, Factory Structure, Product Catalog, Compensation Catalog.
- **Approved**: Avoid monolithic Worker/Factory planner entity bundles.

## Entities (target)

### Employee domain
- Worker
- WorkerSalaryHistory
- Department
- WorkerDepartmentSnapshot

### Factory domain
- Factory / ProductionLine / MainStage / SubStage

### Product domain
- ProductModel
- ProductModelStage

### Compensation domain
- CompensationMode
- PayrollRun / CompensationAllocation

## Current vs target

### Confirmed
- Worker, Factory, ProductionLine, MainStage, SubStage موجودة في الكود (`src/backend/ProductionLinePlanner.Domain/Entities/*`).

### Architecture Conflict
- لا يوجد Department entity فعليًا ضمن domain؛ يوجد Department فقط من Attendance source.

## Permissions per capability (mapping)

- `workers.view`:
  - قراءة Worker + assignments + attendance snapshots.
- `workers.manage`:
  - تعديل Worker fields + status.

## Validation

- معرفات خارجيًا ثابتة على مستوى `AttendanceUserId`.
- `EmployeeCode` unique.

## Acceptance

- لا duplicate منطق `attendance identity` بين جداول متعددة.
- كل منطق تعديل هوية العامل يمر عبر Capability `employees.manage`.

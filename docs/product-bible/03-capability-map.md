# 03 - Capability Map

## Capability domains

1. **Identity and Access Management**
2. **Employee Administration**
3. **Attendance Integration**
4. **Factory Structure**
5. **Planning and Assignments**
6. **Production Engineering**
7. **Production Execution**
8. **Compensation**
9. **Reporting and Analytics**
10. **Administration and Audit**
11. **Shared Import/Export Platform**

## Responsibilities

- كل Capability تملك نطاقًا تجاريًا + API endpoints + permissions.
- الواجهات تستهلك نفس capability names عبر PermissionService.

## Ownership (اقتراح)

- Backend: owners = domain engine + service layer.
- Frontend: owners = shared feature shells + reusable components.
- Shared: Architecture review لكل capability قبل كل merge.

## Capability vs Route conflict

- Confirmed: التوجيه الحالي مبني على routes وroles.
- Approved: migration إلى permissions per capability مع route + UI enforcement.

## Capability status

| Capability | Status | Source |
|---|---|---|
| Identity & Auth | Approved (target) | `13-identity-access-and-permissions.md` |
| Attendance Sync | Approved | `08-attendance-integration.md` |
| Assignment | Approved | `11-production-execution.md` |
| Compensation | Planned | `12-compensation.md` |
| Import/Export | Planned | `18-excel-import-export.md` |

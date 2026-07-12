# ADR-003: Planner May Perform Controlled Worker and Department Writes

## Context

Workers need updates (name, department, status, photo) but full mutation of attendance identity fields must be governed.

## Decision

Factory Planner can write worker/department operational data through controlled services with strict validation, while Attendance keys are validated before change.

## Consequences

- `workers.manage` controls who can change worker master data.
- `AttendanceUserId` change is validated and audited.
- Update APIs must separate writeable and immutable fields.

## Alternatives Rejected

- Unrestricted self-service updates for any authenticated user.
- Full write access to all external fields (USERID/BADGENUMBER) without validation.

## Status

Approved

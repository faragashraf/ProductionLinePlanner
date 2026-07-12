# ADR-002: ZKTime Owns Attendance Transactions

## Context

Attendance source (`USERINFO`, `CHECKINOUT`) is external and used today for `AttendanceUserId` and check-in/out.

## Decision

`AttendanceRecord` inside Planner is an internal operational snapshot; source transactions and source identifiers remain owned by ZKTime.

## Consequences

- Planner reads and ingests attendance data.
- Attendance records are append/update internal snapshot and are not source-of-truth for payroll overrides.
- Source payload and IDs are retained for traceability.

## Alternatives Rejected

- Treating ZKTime tables as full writable source for operations.
- Ignoring source IDs in sync for audit/trace.

## Status

Approved

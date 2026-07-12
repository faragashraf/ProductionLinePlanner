# ADR-004: Attendance Transactions Are Immutable From Planner

## Context

Current sync updates existing `AttendanceRecord` rows per worker/date, but writes to source tables (`CHECKINOUT`) are not supported in Planner.

## Decision

Planner must not provide any endpoint that writes raw `CHECKINOUT`-equivalent records.

## Consequences

- Security boundary: only ingestion reads from ZK tables.
- Attendance corrections done through Planner must create controlled re-sync or correction jobs, not direct row edits to source semantics.
- UI should treat attendance as read-only operational fact except Planner-level overrides policy.

## Alternatives Rejected

- Endpoint allowing direct transaction mutation for source-level attendance.
- Mixed write model where some sync channels bypass validation.

## Status

Approved

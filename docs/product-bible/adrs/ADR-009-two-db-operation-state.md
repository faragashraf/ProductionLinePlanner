# ADR-009: Two-Database Writes Use Explicit Operation State

## Context

Attendance sync reads from ZK source context and writes Planner `AttendanceRecords` in `AppDbContext`.

## Decision

Any future cross-context writes (source + app DB) must include explicit operation result state (`inserted/updated/skipped`, match counts) and reconciliation logs.

## Consequences

- No assumption of distributed transactions.
- Explicit failure channels and partial-success status.
- Operational observability for sync runs.

## Alternatives Rejected

- Silent success when one DB write succeeds and other fails.
- Assuming atomic behavior across two different DbContexts.

## Status

Approved

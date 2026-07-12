# ADR-005: Worker Identity Links by Stable External Attendance ID

## Context

`Worker` currently stores both `AttendanceUserId` and `BadgeNumber`.

## Decision

`AttendanceUserId` is the primary stable identity link; name is mutable, ID is read-mostly after initial match.

## Consequences

- Matching logic during sync prefers unique `AttendanceUserId`.
- `BadgeNumber` can be shown and updated only if explicitly needed and approved.
- Avoid duplicated identity sources by keeping one canonical internal match strategy.

## Alternatives Rejected

- Using Name as primary identity.
- Storing two competing worker identities as independent keys.

## Status

Approved

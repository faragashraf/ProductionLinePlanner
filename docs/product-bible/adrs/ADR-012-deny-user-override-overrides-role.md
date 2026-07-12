# ADR-012: User Permission Deny Overrides Role Grant

## Context

Role-based grants are coarse; some users may need temporary revocations.

## Decision

`UserPermissionOverride` is required with `Grant`/`Deny`; deny has priority over role-derived permissions when effective permissions are computed.

## Consequences

- Fine-grained control without redesigning roles.
- Break-glass behavior remains possible via superadmin operations.
- Permission changes become auditable and deterministic.

## Alternatives Rejected

- Union of role grants and user grants only.
- Runtime UI-only override list.

## Status

Approved

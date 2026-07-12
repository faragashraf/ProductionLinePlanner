# ADR-010: Authorization Uses Capabilities/Permissions, Not Role Strings

## Context

Current API uses role policies (`Admin`, `SuperAdmin`) and hard-coded permission mapping in token service.

## Decision

Adopt permission-based authorization as primary model (`workers.view`, `compensation.manage`, etc.) and keep roles as role-group labels.

## Consequences

- Endpoint security is explicit and testable.
- Feature-level permissions are more granular.
- UI can reuse same catalog.

## Alternatives Rejected

- Keeping role checks only.
- Creating separate permission definitions in UI only.

## Status

Approved

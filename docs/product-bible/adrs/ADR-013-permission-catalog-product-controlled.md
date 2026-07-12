# ADR-013: Permission Catalog Is Product-Controlled, Assignments Data-Driven

## Context

Permission names can drift if UI and backend are not aligned.

## Decision

The catalog is product-defined and version-controlled (shared constants / seed package), while role and user assignments are data-driven in DB.

## Consequences

- Prevents arbitrary permission creation.
- Enables static analysis and migration of permissions.
- Supports deprecation pipeline for old permissions.

## Alternatives Rejected

- Allowing UI-defined free text permissions.
- Full dynamic permission creation without catalog governance.

## Status

Approved

# ADR-011: Frontend Security Uses canMatch + canActivate + Permission Directive

## Context

Current frontend uses `AuthGuard` and `RoleGuard` only, with static route role checks.

## Decision

Implement reusable permission guards:
- permission service
- canMatch guard (route preloading prevention)
- canActivate guard (navigation enforcement)
- directive (`plpCan`) for component-level UX.

## Consequences

- Unauthorized modules are not loaded.
- 403 UX becomes consistent.
- Shared security behavior across route/menu/button.

## Alternatives Rejected

- Hiding menu only (no route protection).
- Component-based checks only (no guard layer).

## Status

Approved

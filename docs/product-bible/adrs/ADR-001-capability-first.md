# ADR-001: Architecture by Capability Instead of Screens/Tables

## Context

Current implementation organizes UI and some services by screens, while long-term needs indicate cross-cutting decisions (auth, payroll, production execution, attendance).

## Decision

Product V1 will be documented and implemented by Business Capabilities and bounded contexts, not by screens.

## Consequences

- APIs and UI must map to capabilities (IAM, Attendance, Production, Compensation).
- Shared components and permissions become first-class.
- Future changes remain scoped.

## Alternatives Rejected

- God-like module per feature screen with duplicated business checks.
- Direct UI-driven business rules.

## Status

Approved

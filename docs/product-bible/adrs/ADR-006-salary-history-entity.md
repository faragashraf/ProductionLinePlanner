# ADR-006: Worker Salary Uses Effective-Dated History

## Context

Salary currently lacks dedicated history entity, and there is no `WorkerSalaryHistory` in domain.

## Decision

Introduce `WorkerSalaryHistory` with effective date intervals and current salary determination by date.

## Consequences

- No single mutable Salary field in `Worker`.
- Historical payroll audit becomes deterministic.
- Overlapping intervals validation is mandatory.

## Alternatives Rejected

- Adding `Salary` field directly in `Worker` for all calculations.
- Editing historical production payout on every salary correction.

## Status

Approved

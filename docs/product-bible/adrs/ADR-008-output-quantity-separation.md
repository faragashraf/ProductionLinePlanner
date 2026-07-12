# ADR-008: Production Quantity Is Stored Once, Separate from Allocations

## Context

Without clear separation, worker allocations can double count total output.

## Decision

`ProductionStageOutput.Quantity` records physical output once. `WorkerAllocation` records participation only.

## Consequences

- Physical output and payroll metrics remain decoupled.
- payroll engines can apply modes (shared percentage / fixed rate / custom) safely.
- Duplicate records from multiple allocations are avoided.

## Alternatives Rejected

- Deriving `ProductionStageOutput` as the sum of WorkerAllocation quantities.
- Storing output per worker as final production metric.

## Status

Approved

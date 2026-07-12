# ADR-007: Stage Pricing and Time Belong to ProductModelStage

## Context

`MainStage` and `SubStage` represent structure, not product-specific economics.

## Decision

Pricing and standard seconds are assigned at `ProductModelStage` (model+stage relationship).

## Consequences

- Supports same physical stage with different per-model rates.
- avoids global pricing drift.
- payroll uses snapshot copied at production-record time.

## Alternatives Rejected

- Storing `PiecePrice` and `StandardSeconds` in `SubStage`.
- Global stage pricing across all product models.

## Status

Approved

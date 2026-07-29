# ProductionLinePlanner Agent Instructions

## Frontend responsive verification

Every frontend change must receive a visual review before it is considered complete. Review Desktop first, then Android tablet, then mobile; verify RTL, touch targets, filters, tables, dialogs, and loading/empty/error states. Check for clipping, overlap, and unintended horizontal scrolling. Build and unit-test success alone is not sufficient. Report the reviewed viewports and result explicitly; if visual review is unavailable, say so and do not claim completion. Fix visual regressions before closing the work.

Reference viewports and the reusable checklist live in `docs/frontend/responsive-qa.md`.

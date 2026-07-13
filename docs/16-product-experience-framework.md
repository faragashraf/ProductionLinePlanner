# Product Experience Framework

The Product Experience Framework is the Angular composition layer above the Production Design System and PrimeNG 17. It is additive: existing pages are not migrated automatically. New and migrated pages must compose these primitives rather than add page-local dialog, toolbar, table, state, status, action, responsive, or motion implementations.

## Import surface

Import standalone primitives from `src/app/shared/product`:

- `PlpDialogComponent` (`plp-dialog`) for operational dialogs with standard header, subtitle, error, save/cancel footer, body slot, RTL sizing, loading, and tablet/mobile behavior.
- `PlpConfirmDialogComponent` once in an application shell plus `PlpConfirmationService` for all confirmation requests.
- `PlpFormComponent` and `PlpFormFieldComponent` for semantic form layouts and projected PrimeNG controls.
- `PlpProductPageHeaderComponent` and `PlpProductToolbarComponent` for page context, breadcrumb/action slots, search, filters, density, and wrapping.
- `PlpTableComponent` for projected PrimeNG `pTemplate` table content, loading skeleton, empty state, operational density, and reflow wrapper.
- `PlpProductEmptyStateComponent`, `PlpProductErrorStateComponent`, `PlpProductLoadingStateComponent`, and `PlpProductUnauthorizedStateComponent` for all non-data states.
- `PlpStatusBadgeComponent` for the centralized Production Status Language.
- `PlpActionButtonComponent` for save, cancel, edit, delete, activate, deactivate, refresh, approve, reject, import, and export actions.
- `PlpMotionDirective` for approved fade motion only.

## Composition rules

1. Use PrimeNG controls inside `plp-form-field`; page code owns reactive/template form state but not field layout or validation presentation.
2. Project PrimeNG `pTemplate="header"` and `pTemplate="body"` into `plp-table`. Project row actions inside the projected body template.
3. Include one `plp-confirm-dialog` at the future application composition root, then request confirmations through `PlpConfirmationService`. Do not call `ConfirmationService` directly from pages.
4. Use `plp-action-button` for standardized operations. Do not add page-local labels, icon strings, or severity classes for those operations.
5. Use `plp-status-badge` rather than page-local raw status colours. Unknown statuses intentionally resolve to Neutral.
6. Apply `[plpMotion]` only for approved entry motion. Global reduced-motion rules remain authoritative.

## Responsive and accessibility contract

- Product dialog widths use the Design System gutter tokens: 16px phone, 20px Android-tablet portrait, and 24px tablet landscape/laptop.
- Controls use standard 44px touch targets. `compact` density is available only when the global fine-pointer desktop contract allows it.
- Toolbar and dialog actions wrap on phone; dialog actions remain reachable without horizontal scrolling.
- The form field exposes label, required/optional, help, validation error, disabled, and read-only presentation. The projected control remains the semantic interactive element.
- State shells provide `status` or `alert` roles; loading includes an accessible status label.

## Prohibited patterns

- No local `p-dialog`, `p-confirmDialog`, `p-table`, loading, empty, error, or unauthorized reimplementation in new screens.
- No raw PrimeIcon strings for standardized actions; use the action or status contracts.
- No page-local responsive breakpoints, touch heights, or custom motion.
- No raw status colours or duplicate status switch statements.

## Adoption

Adopt this framework one capability at a time only after approval. The Product Layer deliberately does not change existing routes, permissions, business logic, or pages.

# Production Design System Foundation

Production Line Planner / Factory Planner uses one Arabic-first, RTL-native, mobile-first visual foundation. This document defines reusable contracts only; page migration is a separate phase.

## Source of truth

- Tokens: `src/frontend/src/app/shared/design-system/tokens/design-tokens.scss`
- Global responsive, accessibility, typography, and density utilities: `src/frontend/src/app/shared/design-system/foundation/global-foundation.scss`
- PrimeNG 17 compatibility bridge: `src/frontend/src/app/shared/design-system/primeng/primeng-theme-bridge.scss`
- Runtime PrimeNG z-index configuration: `src/frontend/src/app/shared/design-system/layering/production-z-index.ts`
- Icons: `src/frontend/src/app/shared/design-system/icons/production-icon-map.ts`
- Visual tones and generic statuses: `src/frontend/src/app/shared/design-system/status/production-status-map.ts`

New UI consumes these contracts instead of creating another token file, status palette, generic component style, or icon library.

## Theme and tokens

Light is the only implemented palette. Semantic colour values are scoped under `html[data-theme="light"]`; the document root sets `data-theme="light"`. A future theme must provide the same semantic token names. Do not add dark mode during this foundation phase.

Use semantic tokens such as `--plp-color-text`, `--plp-color-danger`, `--plp-radius-md`, and `--plp-shadow-2`. Raw colours, raw status colours, raw z-indexes, and arbitrary spacing do not belong in new page CSS.

The only approved spacing scale is `4, 8, 12, 16, 20, 24, 32, 40, 48`, exposed as `--plp-space-*`. Existing `--plp-spacing-*` and legacy font aliases are migration compatibility aliases only. Remove them after all current pages have migrated to the new names.

## Cairo typography

`@fontsource/cairo` version `5.2.7` is the self-hosted package dependency. `styles.scss` imports only weights `400`, `500`, `600`, and `700`; each package stylesheet uses `font-display: swap`. No Google Fonts request is required at runtime.

Use `plp-text-display`, `plp-text-page-title`, `plp-text-section-title`, `plp-text-body`, `plp-text-supporting`, `plp-text-caption`, and `plp-text-dense`. Use `plp-code`, `plp-id`, `plp-quantity`, `plp-price`, `plp-date`, or `plp-value-ltr` for isolated LTR values such as codes, GUIDs, phone numbers, email addresses, dates, quantities, and prices.

## Touch and density contract

The standard phone/tablet target and control height is 44px through `--plp-control-height-standard`. PrimeNG buttons, fields, selects, menu actions, paginator controls, checkbox wrappers, radio wrappers, and icon actions consume it. Checkbox and radio marks remain visually compact inside a 44px interactive wrapper.

`--plp-control-height-compact-desktop` is 40px and may be used only inside `.plp-density-compact` at `1024px+` with `hover: hover` and `pointer: fine`. It is never the default for touch devices. Native legacy page controls are not globally resized; new native controls opt in with `plp-control` or `plp-touch-target`.

Table density is controlled by `--plp-table-row-min-height`, `--plp-table-cell-padding-block`, `--plp-table-cell-padding-inline`, and the desktop-only dense equivalents. Use `plp-operational-table` around a PrimeNG table when horizontal scrolling is the operationally correct mobile fallback. The application root never scrolls horizontally.

## Responsive rules

| Range | Device | Page padding | Dialog gutter |
| --- | --- | --- | --- |
| 320–599px | Android phone | 16px | 16px |
| 600–767px | Android tablet portrait | 20px | 20px |
| 768–1023px | Android tablet landscape | 24px | 24px |
| 1024–1279px | Laptop/Desktop | 32px | 24px |
| 1280px+ | Wide desktop | 40px | 24px |

Use `plp-page-frame`, `plp-form-grid`, `plp-responsive-grid`, `plp-action-group`, and `plp-operational-table`. Forms are one column by default and may use the two-column utility from 768px. Three-column operational grids begin at 1024px.

## PrimeNG 17 bridge

The tested dependency is PrimeNG `17.18.15` with `lara-light-blue`. Lara 17 contains static component colours, so root CSS variables alone are insufficient. The compatibility bridge owns token-based normal, hover, focus, active, selected, disabled, invalid, and read-only treatment for the supported component families:

- buttons and ripple
- inputs, InputNumber, Dropdown/Select, and Multiselect
- checkbox and radio wrappers
- dialogs and confirm dialogs
- toast
- tables and paginator
- tags and badges
- toolbar, card, panel, tabs, menus, tree, and connected overlays
- skeleton and tooltip

PrimeNG-version-specific descendant selectors remain only in the bridge. Do not copy them into pages. Re-verify this bridge before a PrimeNG major upgrade.

## Runtime layering

CSS `--plp-z-*` tokens document the hierarchy. `configureProductionPrimeNg()` configures PrimeNG runtime values: dropdown `1000`, menu `1050`, modal/confirm dialog `1200`, and tooltip `1400`. PrimeNG 17 writes toast z-index inline from its modal value, so the bridge deliberately applies `--plp-z-toast` (`1300`) with a narrowly scoped `!important` rule. This keeps toast above dialogs and below tooltips.

## Status and icon language

`PRODUCTION_VISUAL_TONE_MAP` is the shared visual-tone contract. It maps `success`, `warning`, `danger`, `info`, and `neutral` to a semantic CSS token, soft token, PrimeNG severity, and default PrimeIcon. Generic statuses and domain-specific factory statuses retain their own Arabic labels and domain icons, but reference the shared tone type. Unknown generic and factory statuses degrade to `neutral`.

Use `productionIconFor()` for standard actions. PrimeIcons are the default icon library. Use `productionNavigationIconFor()` for back/forward controls; it selects the appropriate arrow for RTL or LTR reading direction.

## RTL, motion, and accessibility

The root direction is RTL. The bridge contains a narrow PrimeNG 17 compatibility section for physical-direction link alignment and submenu arrows; new CSS uses logical properties. Do not add broad direction overrides.

Use motion tokens only. Motion is limited to opacity, transform, colour, border, and shadow. `prefers-reduced-motion` globally removes non-essential animation and transition duration. Visible focus, high-contrast focus, disabled/read-only clarity, a screen-reader-only utility, and standard touch targets are supplied globally.

## Prohibited patterns

- Raw colour, raw status colour, raw z-index, or arbitrary spacing in new page CSS.
- Per-page copies of standard button, field, dialog, table, overlay, or status styles.
- A second generic status map or icon library.
- Desktop-first layouts, page-wide horizontal overflow, or default targets below 44px on touch devices.
- Remote runtime font dependencies.
- Motion that ignores reduced-motion preferences.

## Migration expectation

Future page work composes this foundation with existing shared state primitives. Migrate reusable patterns in tested, bounded changes; do not combine page migration with API, permissions, route, or business-rule changes.

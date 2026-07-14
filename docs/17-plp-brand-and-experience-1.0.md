# PLP Brand & Experience 1.0

## Approved direction

PLP uses **Flowline Signal**: a planned production flow passing through three connected manufacturing stages, supported by a restrained drive cue and an upward planning/performance trace. The production rail is the dominant visual idea. The drive cue exists to imply dependable industrial motion; it must never read as a standalone generic gear logo.

## Logo meaning

- The rail represents an observable production line.
- Three rising stage cells represent connected manufacturing stages and operational progression.
- The small notched hub represents controlled production drive.
- The upward trace represents planning, visibility, and improving performance.

The mark is flat, geometric, and intentionally low-detail. It is a product signal, not a factory illustration.

## Official variants

| Variant | Use | Minimum size |
| --- | --- | --- |
| Horizontal Arabic lockup | Login, reports, spacious desktop identity | 144px wide |
| Compact mark | App Shell, product navigation, product surfaces | 20px |
| Header mark | Compact/mobile header | 24px |
| Login mark | Login identity moment | 48px |
| Favicon mark | Browser tab only; deliberately simplified | 16px |
| Square app icon | PWA/app launcher source | 192px |
| Monochrome | Print, watermark, one-colour reports | 16px |
| PLP monogram | Exceptional constrained Latin-only contexts | 20px |

The Arabic product name is live text in the runtime horizontal lockup: `منصة تخطيط خطوط الإنتاج`. Do not convert it to paths for product UI. SVG exports retain text for print and report use.

## Clear space and incorrect use

Keep clear space of `--plp-brand-clear-space` around compact instances; horizontal instances require at least the mark width divided by four. Do not:

- stretch, rotate, outline, shadow, or apply gradients to the mark;
- recolour individual pieces outside brand aliases or a monochrome treatment;
- replace the mark with a generic gear, factory silhouette, or page-specific illustration;
- animate it continuously;
- place it over busy photography or low-contrast surfaces;
- duplicate the SVG in page templates. Use `plp-brand-logo`.

## Color and typography

The runtime component uses existing Product Design System aliases: `--plp-brand-flow-primary`, `--plp-brand-flow-ink`, `--plp-brand-flow-progress`, `--plp-brand-mark-surface`, and `--plp-brand-mark-wordmark`. These resolve to the approved blue system, semantic green, and ink on light surfaces. Delivery SVGs use the same canonical light palette for standalone export.

Cairo is the Arabic lockup typeface. Arabic is always primary; the optional `PLP` monogram is for constrained Latin contexts only.

## Light, future dark, print, and monochrome

Light surfaces use blue flow/ink, soft-blue stage surfaces, and a restrained green planning trace. Dark theme is not implemented in Brand & Experience 1.0. Its future theme map must override the existing inverse aliases rather than fork logo geometry.

For print or one-colour reports, use the monochrome asset in a single ink colour. Never simulate depth with shadows, effects, or metallic rendering.

## Motion and reduced motion

Only Login may animate: the planning trace reveals once and the three stage cells activate in sequence over `--plp-brand-motion-duration` (600ms). App Shell marks are static. `prefers-reduced-motion: reduce` renders the finished mark immediately, with no stroke draw, transform, or stagger.

No GIF, video, canvas, Lottie, filters, or looping conveyor animation is permitted.

## Accessibility

`plp-brand-logo` is decorative by default for a compact mark beside visible product text. Pass a concise Arabic `label` when the mark communicates product identity on its own. A horizontal lockup exposes its live Arabic text when no explicit label is supplied. SVG artwork is non-focusable.

Maintain contrast against the chosen surface and never use colour as the only status signal.

## Favicon and PWA guidance

The favicon is not a scaled master mark: it keeps only the drive/route silhouette required at 16px. The PWA icon uses the compact mark on the approved solid blue field with protected padding. The manifest references only external SVG source assets; generated raster sizes are deferred until an installation workflow requires them.

## Runtime usage

Use the single `plp-brand-logo` component:

```html
<plp-brand-logo variant="header"></plp-brand-logo>
<plp-brand-logo variant="horizontal"></plp-brand-logo>
<plp-brand-logo variant="login" [animated]="true" label="شعار منصة تخطيط خطوط الإنتاج"></plp-brand-logo>
```

Do not import a heavy graphics or animation package. The component uses native SVG and CSS only.

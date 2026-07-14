# PLP Flowline Signal assets

`plp-brand-master.svg` is the source export for the Flowline Signal: a production rail, three connected stages, a restrained drive cue, and one upward planning trace. The production flow is primary; the drive cue is supporting.

Delivery variants are intentionally purpose-specific:

- `plp-logo-lockup-ar.svg` — horizontal Arabic lockup;
- `plp-logo-icon.svg` — compact mark;
- `plp-favicon.svg` — simplified 16px route/drive silhouette;
- `plp-app-icon.svg` — PWA source icon;
- `plp-app-shell-mark.svg` and `plp-login-hero-mark.svg` — product-surface exports;
- `plp-logo-monochrome.svg` and `plp-logo-inverse.svg` — print and prepared inverse delivery;
- `plp-monogram.svg` — exceptional constrained Latin context only.

Runtime UI must use `plp-brand-logo`, not these files, so light-surface colours follow Product Design System aliases. Pass an Arabic `label` only when a compact mark conveys product identity on its own; leave it decorative beside visible product text. The App Shell mark is static. The Login mark may use one-time motion, disabled for `prefers-reduced-motion`.

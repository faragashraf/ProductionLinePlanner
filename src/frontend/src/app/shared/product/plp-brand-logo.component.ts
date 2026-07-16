import { ChangeDetectionStrategy, Component, Input, booleanAttribute } from '@angular/core';
import { PRODUCT_IDENTITY } from '../../core/config/product-identity.config';

export type PlpBrandLogoVariant = 'mark' | 'horizontal' | 'header' | 'login' | 'monochrome';

/**
 * The sole runtime source for the Flowline Signal identity. Compact marks are
 * decorative beside visible product text; standalone marks receive a label.
 */
@Component({
  selector: 'plp-brand-logo',
  standalone: true,
  template: `
    <span
      class="plp-brand-logo"
      [class.plp-brand-logo--horizontal]="isHorizontal"
      [class.plp-brand-logo--inverse]="inverse"
      [class.plp-brand-logo--animated]="animated && variant === 'login'"
      [class.plp-brand-logo--monochrome]="variant === 'monochrome'"
      [attr.data-plp-brand-variant]="variant"
      [attr.role]="label ? 'img' : null"
      [attr.aria-label]="label || null"
      [attr.aria-hidden]="isDecorative ? 'true' : null"
      [style.--plp-brand-logo-size]="size || null"
    >
      <svg class="plp-brand-logo__symbol" viewBox="0 0 64 64" aria-hidden="true" focusable="false">
        <circle class="plp-brand-logo__drive" cx="13" cy="42" r="6"/>
        <path class="plp-brand-logo__drive-teeth" d="M13 33v3M22 42h-3M13 51v-3M4 42h3"/>
        <path class="plp-brand-logo__rail" d="M7 45h49"/>
        <rect class="plp-brand-logo__stage plp-brand-logo__stage--one" x="19" y="34" width="8" height="8" rx="2"/>
        <rect class="plp-brand-logo__stage plp-brand-logo__stage--two" x="31" y="29" width="8" height="13" rx="2"/>
        <rect class="plp-brand-logo__stage plp-brand-logo__stage--three" x="43" y="24" width="8" height="18" rx="2"/>
        <path class="plp-brand-logo__signal" d="m15 35.5 8-6.5 12 3 13-14h7m-4-4 4 4-4 4"/>
      </svg>
      <span class="plp-brand-logo__wordmark plp-text-supporting" dir="rtl" [hidden]="!isHorizontal" [attr.aria-hidden]="label ? 'true' : null">{{ productIdentity.nameAr }}</span>
    </span>
  `,
  styles: [`
    :host { --plp-brand-logo-size: 2rem; }
    .plp-brand-logo { --b: var(--plp-brand-flow-primary); --i: var(--plp-brand-flow-ink); --s: var(--plp-brand-mark-surface); --g: var(--plp-brand-flow-progress); --w: var(--plp-brand-mark-wordmark); align-items: center; display: inline-flex; line-height: 0; }
    .plp-brand-logo__symbol { block-size: var(--plp-brand-logo-size); display: block; inline-size: var(--plp-brand-logo-size); }
    .plp-brand-logo__drive { fill: var(--s); stroke: var(--b); stroke-width: 1.5; }
    .plp-brand-logo__drive-teeth, .plp-brand-logo__rail { fill: none; stroke: var(--b); stroke-linecap: round; }
    .plp-brand-logo__drive-teeth { stroke-width: 1.5; }
    .plp-brand-logo__rail { stroke-width: 3; }
    .plp-brand-logo__stage { fill: var(--b); }
    .plp-brand-logo__stage--two { fill: var(--i); }
    .plp-brand-logo__signal { fill: none; stroke: var(--g); stroke-linecap: round; stroke-linejoin: round; stroke-width: 3; }
    .plp-brand-logo--horizontal { gap: var(--plp-brand-clear-space); }
    .plp-brand-logo__wordmark { color: var(--w); font-weight: var(--plp-font-weight-bold); white-space: nowrap; }
    .plp-brand-logo--inverse { --b: var(--plp-brand-flow-inverse); --i: var(--plp-brand-mark-surface); --s: transparent; --g: var(--plp-brand-flow-inverse-progress); --w: var(--plp-brand-flow-inverse); }
    .plp-brand-logo--monochrome { --b: currentColor; --i: currentColor; --s: currentColor; --g: currentColor; --w: currentColor; }
    .plp-brand-logo--monochrome .plp-brand-logo__drive { fill-opacity: .16; }
    .plp-brand-logo--animated .plp-brand-logo__signal { animation: plp-brand-trace var(--plp-brand-motion-duration) var(--plp-motion-ease-emphasized) both; stroke-dasharray: 58; }
    .plp-brand-logo--animated .plp-brand-logo__stage { animation: plp-brand-stage var(--plp-motion-fast) var(--plp-motion-ease-standard) both; }
    .plp-brand-logo--animated .plp-brand-logo__stage--one { animation-delay: 120ms; }
    .plp-brand-logo--animated .plp-brand-logo__stage--two { animation-delay: 240ms; }
    .plp-brand-logo--animated .plp-brand-logo__stage--three { animation-delay: 360ms; }
    @keyframes plp-brand-trace { from { opacity: .2; stroke-dashoffset: 58; } to { opacity: 1; stroke-dashoffset: 0; } }
    @keyframes plp-brand-stage { from { opacity: .45; } to { opacity: 1; } }
    @media (prefers-reduced-motion: reduce) { .plp-brand-logo--animated .plp-brand-logo__signal, .plp-brand-logo--animated .plp-brand-logo__stage { animation: none; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpBrandLogoComponent {
  readonly productIdentity = PRODUCT_IDENTITY;

  @Input() variant: PlpBrandLogoVariant = 'mark';
  @Input() label = '';
  @Input() size = '';
  @Input({ transform: booleanAttribute }) inverse = false;
  @Input({ transform: booleanAttribute }) animated = false;

  get isHorizontal(): boolean {
    return this.variant === 'horizontal';
  }

  get isDecorative(): boolean {
    return !this.label && !this.isHorizontal;
  }
}

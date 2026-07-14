import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlpBrandLogoComponent, PlpBrandLogoVariant } from './plp-brand-logo.component';

@Component({
  template: '<plp-brand-logo [variant]="variant" [label]="label" [animated]="animated"></plp-brand-logo>'
})
class BrandLogoHostComponent {
  variant: PlpBrandLogoVariant = 'mark';
  label = '';
  animated = false;
}

describe('PlpBrandLogoComponent', () => {
  let fixture: ComponentFixture<BrandLogoHostComponent>;
  let host: BrandLogoHostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BrandLogoHostComponent],
      imports: [PlpBrandLogoComponent]
    });

    fixture = TestBed.createComponent(BrandLogoHostComponent);
    host = fixture.componentInstance;
  });

  it('renders a decorative compact Flowline mark when adjacent copy names the product', () => {
    fixture.detectChanges();
    const logo = runtimeLogo();

    expect(logo.getAttribute('data-plp-brand-variant')).toBe('mark');
    expect(logo.getAttribute('aria-hidden')).toBe('true');
    expect(logo.querySelectorAll('.plp-brand-logo__stage')).toHaveSize(3);
    expect(logo.querySelector('.plp-brand-logo__rail')).not.toBeNull();
  });

  it('exposes a concise accessible name for a standalone compact mark', () => {
    host.label = 'شعار منصة تخطيط خطوط الإنتاج';
    fixture.detectChanges();
    const logo = runtimeLogo();

    expect(logo.getAttribute('role')).toBe('img');
    expect(logo.getAttribute('aria-label')).toBe('شعار منصة تخطيط خطوط الإنتاج');
    expect(logo.getAttribute('aria-hidden')).toBeNull();
  });

  it('renders live Arabic text for the horizontal lockup without requiring a duplicate label', () => {
    host.variant = 'horizontal';
    fixture.detectChanges();
    const logo = runtimeLogo();

    expect(logo.getAttribute('aria-hidden')).toBeNull();
    expect(logo.textContent).toContain('منصة تخطيط خطوط الإنتاج');
    expect(logo.querySelector('.plp-brand-logo__wordmark')).not.toBeNull();
  });

  it('only enables the one-time motion class for the Login variant', () => {
    host.variant = 'login';
    host.animated = true;
    fixture.detectChanges();

    expect(runtimeLogo().classList.contains('plp-brand-logo--animated')).toBeTrue();

    host.variant = 'header';
    fixture.detectChanges();
    expect(runtimeLogo().classList.contains('plp-brand-logo--animated')).toBeFalse();
  });

  it('supports the single-colour delivery mode without changing Flowline geometry', () => {
    host.variant = 'monochrome';
    fixture.detectChanges();
    const logo = runtimeLogo();

    expect(logo.classList.contains('plp-brand-logo--monochrome')).toBeTrue();
    expect(logo.querySelectorAll('.plp-brand-logo__stage')).toHaveSize(3);
  });

  function runtimeLogo(): HTMLElement {
    return fixture.nativeElement.querySelector('[data-plp-brand-variant]') as HTMLElement;
  }
});

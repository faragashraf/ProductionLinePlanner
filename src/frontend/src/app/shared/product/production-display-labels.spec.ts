import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { PlpProductMetadataItemComponent, PlpProductMetadataRowComponent } from './plp-metadata-row.component';
import { PlpResponsiveEntityRowComponent } from './plp-responsive-entity-row.component';
import { productionDisplayLabel } from './production-display-labels';

@Component({
  standalone: true,
  imports: [PlpProductMetadataItemComponent, PlpProductMetadataRowComponent, PlpResponsiveEntityRowComponent],
  template: `
    <plp-responsive-entity-row title="التجميع النهائي" code="ST-67" icon="pi pi-sitemap">
      <plp-product-metadata-row plp-entity-metadata label="بيانات المرحلة">
        <plp-product-metadata-item label="العمال" value="12" icon="pi pi-users"></plp-product-metadata-item>
      </plp-product-metadata-row>
      <strong plp-entity-value>245.50</strong>
    </plp-responsive-entity-row>
  `
})
class EntityRowHostComponent {}

describe('Production display language and entity hierarchy', () => {
  it('maps technical production values without exposing raw backend terminology', () => {
    expect(productionDisplayLabel('SharedPercentage')).toBe('توزيع نسبي مشترك');
    expect(productionDisplayLabel('EqualShare')).toBe('توزيع متساوٍ');
    expect(productionDisplayLabel('FullRatePerWorker')).toBe('القيمة كاملة لكل عامل');
    expect(productionDisplayLabel('Ready')).toBe('جاهزة');
    expect(productionDisplayLabel('Default')).toBe('تسكين أساسي');
    expect(productionDisplayLabel('UnknownTechnicalValue')).toBe('غير محدد');
  });

  it('renders primary identity, code, metadata, and value in separate responsive regions', () => {
    TestBed.configureTestingModule({ imports: [EntityRowHostComponent, NoopAnimationsModule] });
    const fixture = TestBed.createComponent(EntityRowHostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.plp-responsive-entity-row__title')?.textContent).toContain('التجميع النهائي');
    expect(fixture.nativeElement.querySelector('.plp-responsive-entity-row__code')?.textContent).toContain('ST-67');
    expect(fixture.nativeElement.querySelector('.plp-product-metadata__tag')?.textContent).toContain('العمال: 12');
    expect(fixture.nativeElement.querySelector('.plp-responsive-entity-row__value')?.textContent).toContain('245.50');
  });
});

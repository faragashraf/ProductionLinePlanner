import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { WorkerAssignmentDetailsComponent } from './worker-assignment-details.component';

@Component({
  standalone: true,
  imports: [WorkerAssignmentDetailsComponent],
  template: `
    <section data-dialog="temporary" style="width: 640px">
      <plp-worker-assignment-details
        fullName="عامل باسم عربي طويل للاختبار"
        employeeCode="W-1077"
        productionLineName="خط 3"
        [isOnActiveService]="true"
        [stageNames]="stageNames"
      ></plp-worker-assignment-details>
    </section>
    <section data-dialog="default" style="width: 640px">
      <plp-worker-assignment-details
        fullName="عامل آخر"
        employeeCode="1078"
        productionLineName="خط 3"
        [isOnActiveService]="true"
        [stageNames]="stageNames"
      ></plp-worker-assignment-details>
    </section>
  `
})
class AssignmentDialogsHostComponent {
  stageNames = ['علم وش / 2', 'اسم مرحلة طويل جدًا يلتف داخل حدود صف العامل دون توسيع النافذة'];
}

describe('WorkerAssignmentDetailsComponent', () => {
  let fixture: ComponentFixture<AssignmentDialogsHostComponent>;

  beforeEach(async () => {
    document.documentElement.setAttribute('data-theme', 'light');
    await TestBed.configureTestingModule({ imports: [AssignmentDialogsHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(AssignmentDialogsHostComponent);
    fixture.detectChanges();
  });

  afterEach(() => document.documentElement.removeAttribute('data-theme'));

  it('uses the same shared worker pattern for temporary and permanent assignment contexts', () => {
    const temporary = fixture.nativeElement.querySelector('[data-dialog="temporary"] plp-worker-assignment-details');
    const permanent = fixture.nativeElement.querySelector('[data-dialog="default"] plp-worker-assignment-details');

    expect(temporary).not.toBeNull();
    expect(permanent).not.toBeNull();
    expect(temporary.getAttribute('dir')).toBe('rtl');
    expect(permanent.getAttribute('dir')).toBe('rtl');
    expect(temporary.querySelectorAll('plp-responsive-entity-row').length).toBe(1);
    expect(permanent.querySelectorAll('plp-responsive-entity-row').length).toBe(1);
  });

  it('omits the redundant active-service status from both assignment contexts', () => {
    const temporaryText = fixture.nativeElement.querySelector('[data-dialog="temporary"]').textContent as string;
    const permanentText = fixture.nativeElement.querySelector('[data-dialog="default"]').textContent as string;

    expect(temporaryText).not.toContain('على رأس العمل');
    expect(permanentText).not.toContain('على رأس العمل');
    expect(fixture.nativeElement.querySelector('plp-status-badge')).toBeNull();
  });

  it('renders line and stage count as separate, differently styled metadata variants', () => {
    const temporary = fixture.debugElement.query(By.css('[data-dialog="temporary"]'));
    const metadataItems = temporary.queryAll(By.css('.plp-responsive-entity-row__metadata > plp-product-metadata-row > .plp-product-metadata > plp-product-metadata-item'));
    const stageItems = temporary.queryAll(By.css('.plp-worker-assignment-details__stages plp-product-metadata-item'));
    const text = temporary.nativeElement.textContent as string;
    const lineTag = temporary.nativeElement.querySelector('.plp-worker-assignment-details__metadata--line .p-tag') as HTMLElement;
    const countTag = temporary.nativeElement.querySelector('.plp-worker-assignment-details__metadata--stage-count .p-tag') as HTMLElement;

    expect(metadataItems[0].nativeElement.textContent).toContain('الخط: خط 3');
    expect(metadataItems[0].nativeElement.classList).toContain('plp-worker-assignment-details__metadata--line');
    expect(metadataItems[1].nativeElement.textContent).toContain('عدد المراحل: 2');
    expect(metadataItems[1].nativeElement.classList).toContain('plp-worker-assignment-details__metadata--stage-count');
    expect(getComputedStyle(lineTag).color).not.toBe(getComputedStyle(countTag).color);
    expect(stageItems.length).toBe(2);
    expect(stageItems[0].nativeElement.textContent).toContain('علم وش / 2');
    expect(stageItems[1].nativeElement.textContent).toContain('اسم مرحلة طويل جدًا');
    expect(text).not.toContain('مشارك حاليًا في مرحلتين:');
  });

  it('keeps the code compact, isolates its direction, and presents readable wrapping stage chips', () => {
    const code = fixture.nativeElement.querySelector('[data-dialog="temporary"] .plp-responsive-entity-row__code') as HTMLElement;
    const longStageValue = fixture.nativeElement.querySelectorAll('[data-dialog="temporary"] .plp-worker-assignment-details__stages .p-tag-value')[1] as HTMLElement;
    const stageList = fixture.nativeElement.querySelector('[data-dialog="temporary"] .plp-worker-assignment-details__stages') as HTMLElement;
    const stageTag = longStageValue.closest('.p-tag') as HTMLElement;
    const row = fixture.nativeElement.querySelector('[data-dialog="temporary"] .plp-responsive-entity-row') as HTMLElement;
    const workerName = fixture.nativeElement.querySelector('[data-dialog="temporary"] .plp-responsive-entity-row__title') as HTMLElement;

    expect(getComputedStyle(code).direction).toBe('ltr');
    expect(getComputedStyle(code).display).toBe('inline-flex');
    expect(getComputedStyle(code).flexGrow).toBe('0');
    expect(code.getBoundingClientRect().width).toBeLessThan(row.getBoundingClientRect().width);
    expect(getComputedStyle(stageList).flexWrap).toBe('wrap');
    expect(getComputedStyle(stageList).flexBasis).toBe('100%');
    expect(getComputedStyle(longStageValue).whiteSpace).toBe('normal');
    expect(getComputedStyle(stageTag).color).toBe(getComputedStyle(workerName).color);
  });
});

import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { SharedModule } from '../../shared.module';

@Component({
  selector: 'plp-worker-assignment-card-test-host',
  template: `
    <section dir="rtl" style="width: 360px">
      <plp-worker-assignment-card
        data-context="permanent"
        selectionMode="multiple"
        [selected]="permanentSelected"
        fullName="عامل التسكين الدائم"
        employeeCode="W-1077"
        productionLineName="خط الخياطة"
        [stageNames]="stageNames"
        statusMessage="متاح للإضافة إلى هذه المرحلة"
        (selectionChange)="permanentSelected = $event"
      ></plp-worker-assignment-card>
      <plp-worker-assignment-card
        data-context="temporary"
        selectionMode="single"
        [selected]="temporarySelected"
        fullName="عامل التعيين المؤقت"
        employeeCode="W-1078"
        productionLineName="خط الخياطة"
        [stageNames]="stageNames"
        (selectionChange)="temporarySelected = $event"
      ></plp-worker-assignment-card>
      <plp-worker-assignment-card
        data-context="actual"
        selectionMode="multiple"
        [selected]="actualSelected"
        [expanded]="actualExpanded"
        fullName="عامل متعدد التسكينات"
        employeeCode="W-1079"
        [assignmentDetails]="actualAssignments"
        (selectionChange)="actualSelected = $event"
        (expandedChange)="actualExpanded = $event"
      ></plp-worker-assignment-card>
    </section>
  `
})
class WorkerAssignmentCardTestHostComponent {
  permanentSelected = false;
  temporarySelected = false;
  actualSelected = false;
  actualExpanded = false;
  stageNames = ['مرحلة أولى', 'اسم مرحلة طويل يلتف داخل البطاقة على شاشة Android Tablet'];
  actualAssignments = [
    { productionLineId: 'line-1', productionLineName: 'خط الخياطة 1', subStageId: 'stage-1', subStageName: 'تركيب العلامة' },
    { productionLineId: 'line-2', productionLineName: 'خط اللحام 2', subStageId: 'stage-2', subStageName: 'ازدواج كاموشا' },
  ];
}

describe('WorkerAssignmentCardComponent', () => {
  let fixture: ComponentFixture<WorkerAssignmentCardTestHostComponent>;

  beforeEach(async () => {
    document.documentElement.setAttribute('data-theme', 'light');
    await TestBed.configureTestingModule({
      declarations: [WorkerAssignmentCardTestHostComponent],
      imports: [HttpClientTestingModule, SharedModule]
    }).compileComponents();
    fixture = TestBed.createComponent(WorkerAssignmentCardTestHostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    document.documentElement.removeAttribute('data-theme');
  });

  it('uses one shared card, details, and responsive entity pattern in both assignment contexts', () => {
    const permanent = fixture.nativeElement.querySelector('[data-context="permanent"]');
    const temporary = fixture.nativeElement.querySelector('[data-context="temporary"]');

    for (const card of [permanent, temporary]) {
      expect(card.querySelectorAll('plp-worker-assignment-details').length).toBe(1);
      expect(card.querySelectorAll('plp-responsive-entity-row').length).toBe(1);
      expect(card.querySelector('.plp-responsive-entity-row__title')).not.toBeNull();
      expect(card.querySelector('.plp-responsive-entity-row__code')).not.toBeNull();
      expect(card.querySelector('.plp-worker-assignment-details__metadata--line')).not.toBeNull();
      expect(card.querySelector('.plp-worker-assignment-details__metadata--stage-count')).not.toBeNull();
      expect(card.querySelectorAll('.plp-worker-assignment-details__stage-chip').length).toBe(2);
    }
  });

  it('keeps the permanent checkbox and eligibility state inside the same card', () => {
    const permanent = fixture.nativeElement.querySelector('[data-context="permanent"]');
    const card = permanent.querySelector('.plp-worker-assignment-card');

    expect(card.tagName).toBe('LABEL');
    expect(card.querySelector('input[type="checkbox"]')).not.toBeNull();
    expect(card.querySelector('.plp-worker-assignment-card__state')?.textContent).toContain('متاح للإضافة إلى هذه المرحلة');
    expect(permanent.querySelector('table')).toBeNull();
  });

  it('selects the permanent worker from the full card and directly from its checkbox', () => {
    const host = fixture.componentInstance;
    const permanentCard = fixture.nativeElement.querySelector('[data-context="permanent"] .plp-worker-assignment-card') as HTMLLabelElement;
    const checkbox = permanentCard.querySelector('input') as HTMLInputElement;

    permanentCard.click();
    fixture.detectChanges();
    expect(host.permanentSelected).toBeTrue();
    expect(checkbox.checked).toBeTrue();
    expect(permanentCard.classList).toContain('is-selected');

    checkbox.click();
    fixture.detectChanges();
    expect(host.permanentSelected).toBeFalse();
    expect(checkbox.checked).toBeFalse();
  });

  it('keeps identity, code, and assignment state in the main row and expands every actual participation', () => {
    const actual = fixture.nativeElement.querySelector('[data-context="actual"]') as HTMLElement;
    const mainRow = actual.querySelector('.plp-responsive-entity-row') as HTMLElement;
    const expandButton = actual.querySelector('.plp-worker-assignment-card__expand') as HTMLButtonElement;
    const checkbox = actual.querySelector('input[type="checkbox"]') as HTMLInputElement;

    expect(mainRow.textContent).toContain('عامل متعدد التسكينات');
    expect(mainRow.textContent).toContain('W-1079');
    expect(mainRow.textContent).toContain('مسكن');
    expect(mainRow.textContent).not.toContain('خط الخياطة 1');
    expect(mainRow.textContent).not.toContain('تركيب العلامة');
    expect(expandButton.textContent).toContain('التسكينات (2)');
    expect(expandButton.getAttribute('aria-expanded')).toBe('false');
    expect(checkbox.checked).toBeFalse();

    expandButton.click();
    fixture.detectChanges();

    const expansion = actual.querySelector('.plp-worker-assignment-card__assignment-expansion') as HTMLElement;
    expect(expansion.textContent).toContain('التسكينات الحالية');
    expect(expansion.textContent).toContain('خط الإنتاج');
    expect(expansion.textContent).toContain('خط الخياطة 1');
    expect(expansion.textContent).toContain('المرحلة');
    expect(expansion.textContent).toContain('تركيب العلامة');
    expect(expansion.textContent).toContain('خط اللحام 2');
    expect(expansion.textContent).toContain('ازدواج كاموشا');
    expect(expansion.querySelectorAll('.plp-worker-assignment-card__assignment-item').length).toBe(2);
    expect(expansion.querySelectorAll('.plp-worker-assignment-card__assignment-fact').length).toBe(4);
    expect(checkbox.checked).toBeFalse();
    expect(fixture.componentInstance.actualSelected).toBeFalse();
  });

  it('provides keyboard focus semantics and a stable touch target for both modes', () => {
    const permanentCheckbox = fixture.nativeElement.querySelector('[data-context="permanent"] input') as HTMLInputElement;
    const temporaryButton = fixture.nativeElement.querySelector('[data-context="temporary"] button') as HTMLButtonElement;

    permanentCheckbox.focus();
    expect(document.activeElement).toBe(permanentCheckbox);
    expect(permanentCheckbox.getAttribute('aria-label')).toContain('عامل التسكين الدائم');
    expect(parseFloat(getComputedStyle(permanentCheckbox.closest('label')!).minHeight)).toBeGreaterThanOrEqual(44);

    temporaryButton.focus();
    expect(document.activeElement).toBe(temporaryButton);
    temporaryButton.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.temporarySelected).toBeTrue();
    expect(temporaryButton.getAttribute('aria-pressed')).toBe('true');
  });

  it('keeps RTL metadata and long stage names wrapped without horizontal overflow', () => {
    const section = fixture.nativeElement.querySelector('section') as HTMLElement;
    const cards = fixture.nativeElement.querySelectorAll('.plp-worker-assignment-card') as NodeListOf<HTMLElement>;
    const longStage = fixture.nativeElement.querySelector('[data-context="permanent"] .plp-worker-assignment-details__stage-chip:last-child .p-tag-value') as HTMLElement;

    expect(section.getAttribute('dir')).toBe('rtl');
    expect(getComputedStyle(longStage).whiteSpace).toBe('normal');
    cards.forEach(card => expect(card.scrollWidth).toBeLessThanOrEqual(card.clientWidth));
  });
});

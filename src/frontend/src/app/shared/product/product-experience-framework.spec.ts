import { Component, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ConfirmationService } from 'primeng/api';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PlpConfirmationService } from './plp-confirmation.service';
import { PlpDialogComponent } from './plp-dialog.component';
import { PlpFormFieldComponent } from './plp-form-field.component';
import { PlpProductLoadingStateComponent } from './plp-loading-state.component';
import { PlpTableComponent } from './plp-table.component';
import { PlpProductToolbarComponent } from './plp-toolbar.component';
import { PLP_ACTION_DEFINITIONS, plpActionIconFor } from './product-action';
import { PLP_DIALOG_SIZE_CLASS, PLP_RESPONSIVE_CONTRACT } from './product-responsive';

@Component({
  standalone: true,
  imports: [PlpActionButtonComponent],
  template: `<plp-action-button action="save" (triggered)="onSave()"></plp-action-button>`
})
class ActionHostComponent {
  saves = 0;

  onSave(): void {
    this.saves += 1;
  }
}

@Component({
  standalone: true,
  imports: [PlpProductToolbarComponent],
  template: `<plp-product-toolbar [searchValue]="query" (searchValueChange)="query = $event"></plp-product-toolbar>`
})
class ToolbarHostComponent {
  query = '';
}

describe('Product Experience Framework', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [NoopAnimationsModule] }));

  it('centralizes all required operational action labels and PrimeIcons', () => {
    expect(Object.keys(PLP_ACTION_DEFINITIONS)).toEqual([
      'save',
      'cancel',
      'edit',
      'delete',
      'activate',
      'deactivate',
      'refresh',
      'approve',
      'reject',
      'import',
      'export'
    ]);
    expect(plpActionIconFor('delete')).toBe('pi-trash');
  });

  it('emits the reusable action event exactly once from the rendered button', () => {
    const fixture = createComponent(ActionHostComponent);

    fixture.nativeElement.querySelector('button').click();

    expect(fixture.componentInstance.saves).toBe(1);
  });

  it('uses token-aligned phone and Android-tablet dialog gutters', () => {
    expect(PLP_RESPONSIVE_CONTRACT.dialogGutter.phone).toBe('var(--plp-space-16)');
    expect(PLP_RESPONSIVE_CONTRACT.dialogGutter.tabletPortrait).toBe('var(--plp-space-20)');
    expect(PLP_RESPONSIVE_CONTRACT.dialogGutter.tabletLandscape).toBe('var(--plp-space-24)');
    expect(PLP_DIALOG_SIZE_CLASS.wide).toBe('plp-product-dialog--wide');
  });

  it('renders form labels, required state, help, and validation errors without local markup', () => {
    const fixture = createComponent(PlpFormFieldComponent);
    fixture.componentRef.setInput('label', 'اسم المصنع');
    fixture.componentRef.setInput('required', true);
    fixture.componentRef.setInput('help', 'استخدم الاسم التشغيلي');
    fixture.componentRef.setInput('error', 'الاسم مطلوب');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('اسم المصنع');
    expect(fixture.nativeElement.textContent).toContain('*');
    expect(fixture.nativeElement.textContent).toContain('الاسم مطلوب');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('shows the shared loading state before a table has data', () => {
    const fixture = createComponent(PlpTableComponent);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.directive(PlpProductLoadingStateComponent))).not.toBeNull();
  });

  it('emits toolbar search input through its shared contract', () => {
    const fixture = createComponent(ToolbarHostComponent);
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = 'خط خياطة';
    input.dispatchEvent(new Event('input'));

    expect(fixture.componentInstance.query).toBe('خط خياطة');
  });

  it('builds a standard PrimeNG confirmation request through the wrapper service', () => {
    const confirmationService = jasmine.createSpyObj<ConfirmationService>('ConfirmationService', ['confirm']);
    const service = new PlpConfirmationService(confirmationService);
    const accept = jasmine.createSpy('accept');

    service.confirm({ header: 'تأكيد الحذف', message: 'هل تريد المتابعة؟', accept, acceptAction: 'delete' });

    expect(confirmationService.confirm).toHaveBeenCalledTimes(1);
    expect(confirmationService.confirm).toHaveBeenCalledWith(
      jasmine.objectContaining({
        key: 'plp-confirm',
        icon: 'pi-trash',
        acceptButtonStyleClass: 'p-button-danger',
        accept
      })
    );
  });

  it('keeps dialog sizing and save/cancel behavior inside the reusable shell', () => {
    const fixture = createComponent(PlpDialogComponent);
    fixture.componentRef.setInput('title', 'تعديل');
    fixture.componentRef.setInput('size', 'wide');
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.dialogClass).toContain('plp-product-dialog--wide');
    expect(fixture.debugElement.query(By.css('p-dialog'))).not.toBeNull();
  });
});

function createComponent<T>(component: Type<T>): ComponentFixture<T> {
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return fixture;
}

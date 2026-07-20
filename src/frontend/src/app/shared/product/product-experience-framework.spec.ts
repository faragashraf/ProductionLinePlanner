import { Component, NgZone, Type } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { ConfirmationService, PrimeNGConfig } from 'primeng/api';
import { Table, TableModule } from 'primeng/table';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PlpConfirmationService } from './plp-confirmation.service';
import { PlpDialogComponent } from './plp-dialog.component';
import { PlpFormFieldComponent } from './plp-form-field.component';
import { PlpProductLoadingStateComponent } from './plp-loading-state.component';
import { PlpProductEmptyStateComponent } from './plp-empty-state.component';
import { PlpProductErrorStateComponent } from './plp-error-state.component';
import { PlpProductPageHeaderComponent } from './plp-page-header.component';
import { PlpTableComponent } from './plp-table.component';
import { PlpProductToolbarComponent } from './plp-toolbar.component';
import { PlpResponsiveTableDirective } from './plp-responsive-table.directive';
import { PlpTablePaginationDirective } from './plp-table-pagination.directive';
import { PLP_ACTION_DEFINITIONS, plpActionIconFor } from './product-action';
import { PLP_DIALOG_SIZE_CLASS, PLP_RESPONSIVE_CONTRACT } from './product-responsive';
import { configureProductionPrimeNg } from '../design-system/layering/production-z-index';
import { SharedModule } from '../shared.module';
import { PlpProductMetadataItemComponent, PlpProductMetadataRowComponent } from './plp-metadata-row.component';

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
  template: `<plp-product-toolbar [searchValue]="query" [clearEnabled]="true" (searchValueChange)="query = $event"></plp-product-toolbar>`
})
class ToolbarHostComponent {
  query = '';
}

@Component({
  standalone: true,
  imports: [TableModule, PlpResponsiveTableDirective],
  template: `
    <p-table plpResponsiveTable="scroll" [plpStickyActions]="true" [value]="rows">
      <ng-template pTemplate="header"><tr><th>الاسم</th><th>الإجراءات</th></tr></ng-template>
      <ng-template pTemplate="body" let-row><tr><td>{{ row.name }}</td><td><button type="button">تعديل</button></td></tr></ng-template>
    </p-table>
  `
})
class ResponsiveTableHostComponent {
  rows = [{ name: 'مرحلة تجريبية' }];
}

@Component({
  standalone: true,
  imports: [TableModule, PlpResponsiveTableDirective, PlpTablePaginationDirective],
  template: `
    <div dir="rtl">
      <p-table
        plpResponsiveTable="scroll"
        plpTablePagination
        [plpPaginationPageSize]="5"
        [plpPaginationResetKey]="filter"
        [value]="visibleRows"
        dataKey="id"
      >
        <ng-template pTemplate="header"><tr><th>الاسم</th></tr></ng-template>
        <ng-template pTemplate="body" let-row><tr><td>{{ row.name }}</td></tr></ng-template>
      </p-table>
    </div>
  `
})
class PaginatedTableHostComponent {
  filter = '';
  readonly rows = Array.from({ length: 12 }, (_, index) => ({ id: index + 1, name: `مرحلة ${index + 1}` }));

  get visibleRows(): { id: number; name: string }[] {
    return this.filter ? this.rows.slice(0, 1) : this.rows;
  }
}

@Component({
  standalone: true,
  imports: [HttpClientTestingModule, SharedModule],
  template: `
    <plp-page-header title="العاملون" description="دليل القوة العاملة">
      <plp-product-metadata-row header-metadata plp-page-metadata label="ملخص العاملين">
        <plp-product-metadata-item label="الإجمالي" value="120"></plp-product-metadata-item>
      </plp-product-metadata-row>
      <button header-actions type="button">إضافة</button>
    </plp-page-header>
    <plp-loading-skeleton [lines]="2" [showAvatar]="true"></plp-loading-skeleton>
    <plp-empty-state actionText="تحديث"></plp-empty-state>
    <plp-error-state></plp-error-state>
  `
})
class LegacyFoundationHostComponent {}

@Component({
  standalone: true,
  imports: [HttpClientTestingModule, SharedModule],
  template: `
    <plp-factory-card title="مصنع القاهرة" metadataLabel="العاملون" metadataValue="90"></plp-factory-card>
    <plp-production-line-card lineName="خط التجميع" statusText="جاهز" [totalStages]="8" activeStage="الخياطة"></plp-production-line-card>
    <plp-main-stage-card name="التجميع" [workersCurrent]="8" [workersRequired]="10"></plp-main-stage-card>
    <plp-sub-stage-card name="الخياطة" [workersCurrent]="4" [workersRequired]="5"></plp-sub-stage-card>
    <plp-worker-card fullName="أحمد علي" code="W-100" assignmentType="تعيين حالي" lastActivity="اليوم"></plp-worker-card>
    <plp-assignment-card worker="أحمد علي" fromStage="القص" toStage="الخياطة" assignmentType="مؤقت"></plp-assignment-card>
  `
})
class BusinessHierarchyHostComponent {}

@Component({
  standalone: true,
  imports: [PlpProductPageHeaderComponent, PlpProductMetadataItemComponent, PlpProductMetadataRowComponent],
  template: `
    <plp-product-page-header title="بيانات العامل">
      <plp-product-metadata-row plp-page-metadata label="بيانات العامل الإضافية">
        <plp-product-metadata-item label="الكود" value="W-100"></plp-product-metadata-item>
        <plp-product-metadata-item label="القسم" value="التجميع"></plp-product-metadata-item>
      </plp-product-metadata-row>
    </plp-product-page-header>
  `
})
class MetadataHierarchyHostComponent {}

describe('Product Experience Framework', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule, NoopAnimationsModule] });
    configureProductionPrimeNg(TestBed.inject(PrimeNGConfig));
  });

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
    expect(plpActionIconFor('delete')).toBe('pi pi-trash');
  });

  it('emits the reusable action event exactly once from the rendered button', () => {
    const fixture = createComponent(ActionHostComponent);
    const icon = fixture.nativeElement.querySelector('.p-button-icon') as HTMLElement;

    fixture.nativeElement.querySelector('button').click();

    expect(fixture.componentInstance.saves).toBe(1);
    expect(icon.classList.contains('pi')).toBeTrue();
    expect(icon.classList.contains('pi-save')).toBeTrue();
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

  it('keeps loading geometry stable across equivalent change-detection passes', () => {
    const fixture = createComponent(PlpProductLoadingStateComponent);
    fixture.componentRef.setInput('lines', 5);
    fixture.detectChanges();
    const rows = fixture.componentInstance.rows;

    fixture.detectChanges();

    expect(fixture.componentInstance.rows).toBe(rows);
    expect(fixture.nativeElement.querySelectorAll('.plp-product-loading__row')).toHaveSize(5);
  });

  it('routes legacy page and state selectors through the shared Product Experience foundation', () => {
    const fixture = createComponent(LegacyFoundationHostComponent);

    expect(fixture.debugElement.query(By.directive(PlpProductPageHeaderComponent))).not.toBeNull();
    expect(fixture.debugElement.query(By.directive(PlpProductLoadingStateComponent))).not.toBeNull();
    expect(fixture.debugElement.query(By.directive(PlpProductEmptyStateComponent))).not.toBeNull();
    expect(fixture.debugElement.query(By.directive(PlpProductErrorStateComponent))).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('دليل القوة العاملة');
    expect(fixture.nativeElement.textContent).toContain('إضافة');
    expect(fixture.nativeElement.querySelector('.plp-product-metadata__tag')?.textContent).toContain('الإجمالي: 120');
  });

  it('keeps entity names primary and secondary facts in the shared PrimeNG metadata pattern', () => {
    const fixture = createComponent(MetadataHierarchyHostComponent);
    const title = fixture.nativeElement.querySelector('h1') as HTMLHeadingElement;
    const metadata = fixture.nativeElement.querySelector('[role="list"]') as HTMLElement;
    const tags = fixture.nativeElement.querySelectorAll('.plp-product-metadata__tag');

    expect(title.textContent).toContain('بيانات العامل');
    expect(metadata.getAttribute('aria-label')).toBe('بيانات العامل الإضافية');
    expect(tags).toHaveSize(2);
    expect(tags[0].classList.contains('p-tag')).toBeTrue();
    expect(tags[0].textContent).toContain('الكود: W-100');
    expect(tags[1].textContent).toContain('القسم: التجميع');
  });

  it('renders the shared metadata hierarchy inside the actual reusable business cards', () => {
    const fixture = createComponent(BusinessHierarchyHostComponent);
    const headings = fixture.nativeElement.querySelectorAll('h3, h4') as NodeListOf<Element>;
    const metadataTags = fixture.nativeElement.querySelectorAll('.plp-product-metadata__tag') as NodeListOf<Element>;
    const primaryNames = Array.from(headings, heading => heading.textContent?.trim());
    const tags = Array.from(metadataTags, tag => tag.textContent?.trim());

    expect(primaryNames).toContain('مصنع القاهرة');
    expect(primaryNames).toContain('خط التجميع');
    expect(primaryNames).toContain('التجميع');
    expect(primaryNames).toContain('الخياطة');
    expect(primaryNames).toContain('أحمد علي');
    expect(tags).toContain('العاملون: 90');
    expect(tags).toContain('عدد المراحل: 8');
    expect(tags).toContain('الكود: W-100');
    expect(tags).toContain('من: القص');
    expect(tags).toContain('إلى: الخياطة');
  });

  it('applies the shared contained-scroll and sticky-actions contract without duplicating table templates', () => {
    const fixture = createComponent(ResponsiveTableHostComponent);
    const table = fixture.nativeElement.querySelector('p-table') as HTMLElement;

    expect(table.classList.contains('plp-operational-table')).toBeTrue();
    expect(table.classList.contains('plp-operational-table--scroll')).toBeTrue();
    expect(table.classList.contains('plp-operational-table--sticky-actions')).toBeTrue();
    expect(table.getAttribute('data-plp-table-presentation')).toBe('scroll');
  });

  it('attaches responsive table geometry observers outside Angular change detection', fakeAsync(() => {
    const ngZone = TestBed.inject(NgZone);
    const runOutsideAngular = spyOn(ngZone, 'runOutsideAngular').and.callThrough();

    const fixture = createComponent(ResponsiveTableHostComponent);
    flushMicrotasks();

    const attachesOverflowObserverOutsideAngular = runOutsideAngular.calls
      .allArgs()
      .some(([callback]) => String(callback).includes('attachOverflowObserver'));

    expect(attachesOverflowObserverOutsideAngular).toBeTrue();
    fixture.destroy();
  }));

  it('does not attach responsive table observers after the host is destroyed', fakeAsync(() => {
    const fixture = createComponent(ResponsiveTableHostComponent);
    const table = fixture.nativeElement.querySelector('p-table') as HTMLElement;

    fixture.destroy();
    flushMicrotasks();

    expect(table.classList.contains('plp-horizontal-overflow')).toBeFalse();
  }));

  it('configures client pagination with the standard total, current-page report, and mobile-safe paginator shell', () => {
    const fixture = createComponent(PaginatedTableHostComponent);
    const paginator = fixture.nativeElement.querySelector('.p-paginator') as HTMLElement;

    expect(paginator).not.toBeNull();
    expect(paginator.textContent).toContain('عرض 1–5 من 12');
    expect(fixture.nativeElement.querySelector('p-table').classList.contains('plp-table-pagination')).toBeTrue();
    expect(getComputedStyle(paginator).direction).toBe('rtl');
  });

  it('keeps the paginator page-size control and option list labelled after Arabic localization', () => {
    const fixture = createComponent(PaginatedTableHostComponent);
    const pageSizeControl = fixture.nativeElement.querySelector('.p-paginator-rpp-options [role="combobox"]') as HTMLElement;

    expect(pageSizeControl).not.toBeNull();
    expect(pageSizeControl.getAttribute('aria-label')).toBe('عدد الصفوف في الصفحة');

    pageSizeControl.click();
    fixture.detectChanges();

    const optionList = document.body.querySelector('.p-dropdown-items[role="listbox"]') as HTMLElement;
    expect(optionList).not.toBeNull();
    expect(optionList.getAttribute('aria-label')).toBe('قائمة الخيارات');
  });

  it('changes pages and page size through the shared PrimeNG paginator state', () => {
    const fixture = createComponent(PaginatedTableHostComponent);
    const next = fixture.nativeElement.querySelector('.p-paginator-next') as HTMLButtonElement;

    next.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('مرحلة 6');

    const table = fixture.debugElement.query(By.directive(Table)).componentInstance as Table;
    table.onPageChange({ first: 0, rows: 20 });
    fixture.detectChanges();
    expect(table.rows).toBe(20);
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('مرحلة 12');
  });

  it('resets to the first page when a filter changes and supports empty and one-page results', () => {
    const fixture = createComponent(PaginatedTableHostComponent);
    const next = fixture.nativeElement.querySelector('.p-paginator-next') as HTMLButtonElement;
    next.click();
    fixture.detectChanges();

    fixture.componentInstance.filter = 'one';
    fixture.detectChanges();

    const paginator = fixture.nativeElement.querySelector('.p-paginator') as HTMLElement;
    expect(paginator.textContent).toContain('عرض 1–1 من 1');
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('مرحلة 1');

    fixture.componentInstance.filter = 'empty';
    fixture.componentInstance.rows.splice(0, fixture.componentInstance.rows.length);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.p-paginator')).not.toBeNull();
  });

  it('emits toolbar search input through its shared contract', () => {
    const fixture = createComponent(ToolbarHostComponent);
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = 'خط خياطة';
    input.dispatchEvent(new Event('input'));

    expect(fixture.componentInstance.query).toBe('خط خياطة');
  });

  it('clears a populated toolbar search through its shared contract', () => {
    const fixture = createComponent(ToolbarHostComponent);
    fixture.componentInstance.query = 'مرحلة القص';
    fixture.detectChanges();

    const clearButton = fixture.nativeElement.querySelector('.plp-product-toolbar__clear') as HTMLButtonElement;
    clearButton.click();

    expect(fixture.componentInstance.query).toBe('');
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
        icon: 'pi pi-trash',
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

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { Observable, of, throwError } from 'rxjs';
import { DepartmentItem, ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';

describe('ManufacturingDepartmentsPageComponent', () => {
  function createComponent(departments$: Observable<DepartmentItem[]> = of([
    { departmentId: 4, name: 'Challenger' },
    { departmentId: 7, name: 'Assembly', isActive: true }
  ])): ComponentFixture<ManufacturingDepartmentsPageComponent> {
    const api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', ['departments']);
    api.departments.and.returnValue(departments$);

    TestBed.configureTestingModule({
      declarations: [ManufacturingDepartmentsPageComponent],
      imports: [SharedModule, ButtonModule, TableModule],
      providers: [{ provide: ManufacturingMasterDataApiService, useValue: api }]
    });

    const fixture = TestBed.createComponent(ManufacturingDepartmentsPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders department rows from the API response', () => {
    const fixture = createComponent();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Challenger');
    expect(text).toContain('4');
    expect(text).toContain('Assembly');
    expect(text).toContain('نشط');
  });

  it('renders the empty state when no departments are returned', () => {
    const fixture = createComponent(of([]));

    expect(fixture.nativeElement.textContent).toContain('لا توجد أقسام');
  });

  it('renders the error state when loading departments fails', () => {
    const fixture = createComponent(throwError(() => new Error('Department load failed')));

    expect(fixture.nativeElement.textContent).toContain('تعذر تحميل قائمة الأقسام');
    expect(fixture.nativeElement.textContent).toContain('Department load failed');
  });

  it('filters departments by name and code on the client', () => {
    const fixture = createComponent();
    const input = fixture.debugElement.query(By.css('#departmentsSearch')).nativeElement as HTMLInputElement;

    input.value = '7';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Assembly');
    expect(text).not.toContain('Challenger');
  });
});

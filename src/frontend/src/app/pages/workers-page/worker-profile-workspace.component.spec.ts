import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { PaginatorModule } from 'primeng/paginator';
import { ButtonModule } from 'primeng/button';
import { RippleModule } from 'primeng/ripple';
import { TableModule } from 'primeng/table';
import { of, Subject, throwError } from 'rxjs';
import { WorkerManagementFacade } from './worker-management.facade';
import { WorkerManagementProfile } from './worker-management.models';
import { WorkerProfileWorkspaceComponent } from './worker-profile-workspace.component';

describe('WorkerProfileWorkspaceComponent', () => {
  let fixture: ComponentFixture<WorkerProfileWorkspaceComponent>;
  let component: WorkerProfileWorkspaceComponent;
  let facade: jasmine.SpyObj<WorkerManagementFacade>;

  const worker: WorkerManagementProfile = {
    id: '11111111-1111-1111-1111-111111111111',
    local: { displayName: 'عامل محلي طويل الاسم', photoUrl: null, phone: null, salary: null, profileStatus: 'complete', employmentStatus: 'active', employmentEndDate: null },
    source: { sourceName: null, attendanceUserId: '99', attendanceDepartmentId: 4, badgeNumber: 'B-1', employeeCode: 'EMP-1', employmentStatus: 'Active', department: null, shift: null, lastObservedAt: '2026-07-29T07:00:00Z', linkStatus: 'linked' },
    assignments: [], assignmentStatus: 'unassigned', defaultSubStageId: null,
    attendance: null, system: { createdAtUtc: '2026-01-01T08:00:00Z', updatedAtUtc: '2026-07-29T07:00:00Z' },
    dataStates: { assignments: 'empty', attendance: 'forbidden', salary: 'empty' }
  };

  beforeEach(async () => {
    facade = jasmine.createSpyObj<WorkerManagementFacade>('WorkerManagementFacade', ['saveLocalProfile', 'uploadPhoto', 'deletePhoto', 'loadAttendanceHistory']);
    facade.saveLocalProfile.and.returnValue(of(worker));
    facade.uploadPhoto.and.returnValue(of({ ...worker, local: { ...worker.local, photoUrl: '/api/workers/11111111-1111-1111-1111-111111111111/photo?v=' + 'a'.repeat(64) } }));
    facade.deletePhoto.and.returnValue(of(worker));
    facade.loadAttendanceHistory.and.returnValue(of({
      items: [{
        recordId: 'record-1', productionDate: '2026-07-29', attendanceStatus: 'Present', source: 'AttendanceSync',
        movements: [
          { occurredAtUtc: '2026-07-29T05:00:00Z', movementType: 'In' },
          { occurredAtUtc: '2026-07-29T14:00:00Z', movementType: 'Out' }
        ]
      }],
      page: 1, pageSize: 10, totalCount: 1, totalPages: 1
    }));
    await TestBed.configureTestingModule({
      declarations: [WorkerProfileWorkspaceComponent], imports: [CommonModule, FormsModule, ReactiveFormsModule, ButtonModule, RippleModule, TableModule, PaginatorModule],
      providers: [{ provide: WorkerManagementFacade, useValue: facade }], schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(WorkerProfileWorkspaceComponent);
    component = fixture.componentInstance;
    component.worker = structuredClone(worker);
    component.canManage = true;
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: null, firstChange: true, isFirstChange: () => true } });
    fixture.detectChanges();
  });

  it('renders real system values with precise empty states instead of a generic unavailable label', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(worker.local.displayName);
    expect(text).toContain('لم تُسجل قيمة حالية');
    component.selectSection('system');
    fixture.detectChanges();
    const systemText = fixture.nativeElement.querySelector('[data-profile-section="system"]')?.textContent as string;
    expect(systemText).toContain('B-1');
    expect(systemText).toContain('99');
    expect(systemText).not.toContain('غير متاح');
  });

  it('saves local name and employment status through the facade', () => {
    component.draftForm.patchValue({ displayName: 'اسم محلي محفوظ', employmentStatus: 'inactive' });
    component.saveDraft();
    expect(facade.saveLocalProfile).toHaveBeenCalledWith(jasmine.any(Object), { displayName: 'اسم محلي محفوظ', employmentStatus: 'inactive' });
    expect(component.saveMessage).toContain('تم حفظ البيانات المحلية');
  });

  it('rejects an unsupported photo before the API call', async () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['plain'], 'worker.gif', { type: 'image/gif' })] });
    await component.onPhotoSelected(input);
    expect(component.photoError).toContain('JPEG');
    expect(facade.uploadPhoto).not.toHaveBeenCalled();
  });

  it('rejects a photo larger than 5 MiB before the API call', async () => {
    const input = document.createElement('input');
    const oversized = new File([new Uint8Array((5 * 1024 * 1024) + 1)], 'worker.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [oversized] });
    await component.onPhotoSelected(input);
    expect(component.photoError).toContain('5 MiB');
    expect(facade.uploadPhoto).not.toHaveBeenCalled();
  });

  it('uploads a selected valid photo and emits the cache-busted profile', async () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array([0x42, 0x4d, 0, 0])], 'worker.bmp', { type: 'image/bmp' })] });
    await component.onPhotoSelected(input);
    component.uploadSelectedPhoto();
    expect(facade.uploadPhoto).toHaveBeenCalledWith(jasmine.any(Object), jasmine.any(File));
    expect(component.worker.local.photoUrl).toContain('?v=');
  });

  it('does not start a profile save while a photo mutation is in progress', () => {
    component.draftForm.patchValue({ displayName: 'اسم جديد' });
    component.isPhotoBusy = true;

    component.saveDraft();

    expect(facade.saveLocalProfile).not.toHaveBeenCalled();
  });

  it('maps a server failure to a safe message without exposing backend detail', async () => {
    facade.uploadPhoto.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 500,
      error: { message: 'SQL connection details must not reach the user' }
    })));
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array([0x42, 0x4d, 0, 0])], 'worker.bmp', { type: 'image/bmp' })] });
    await component.onPhotoSelected(input);

    component.uploadSelectedPhoto();

    expect(component.photoError).toBe('تعذر رفع الصورة.');
    expect(component.photoError).not.toContain('SQL');
  });

  it('clears a pending preview when the worker changes without leaking it to the next profile', async () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array([0x42, 0x4d, 0, 0])], 'worker.bmp', { type: 'image/bmp' })] });
    await component.onPhotoSelected(input);
    expect(component.selectedPhoto).not.toBeNull();

    component.worker = { ...structuredClone(worker), id: '22222222-2222-2222-2222-222222222222', local: { ...worker.local, displayName: 'عامل آخر' } };
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: worker, firstChange: false, isFirstChange: () => false } });

    expect(component.selectedPhoto).toBeNull();
    expect(component.photoPreviewUrl).toBe('');
  });

  it('does not create a preview after the workspace closes during asynchronous file validation', async () => {
    let resolveBytes!: (value: ArrayBuffer) => void;
    const bytes = new Promise<ArrayBuffer>(resolve => resolveBytes = resolve);
    const delayedFile = {
      name: 'worker.bmp', type: 'image/bmp', size: 4,
      slice: () => ({ arrayBuffer: () => bytes })
    } as unknown as File;
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [delayedFile] });

    const selection = component.onPhotoSelected(input);
    component.ngOnDestroy();
    resolveBytes(new Uint8Array([0x42, 0x4d, 0, 0]).buffer);
    await selection;

    expect(component.selectedPhoto).toBeNull();
    expect(component.photoPreviewUrl).toBe('');
  });

  it('loads detailed attendance once, renders explicit in/out labels and uses the scoped reference colors', () => {
    component.canViewAttendance = true;
    component.selectSection('attendance');
    fixture.detectChanges();

    expect(facade.loadAttendanceHistory).toHaveBeenCalledOnceWith(worker.id, jasmine.objectContaining({ page: 1, pageSize: 10 }));
    const movements = Array.from(fixture.nativeElement.querySelectorAll('.worker-profile__movement')) as HTMLElement[];
    expect(movements.length).toBe(2);
    expect(movements[0].textContent).toContain('حضور');
    expect(movements[1].textContent).toContain('انصراف');
    expect(movements[0].classList).toContain('worker-profile__movement--in');
    expect(movements[1].classList).toContain('worker-profile__movement--out');
  });

  it('rejects an inverted attendance range without calling the API', () => {
    component.canViewAttendance = true;
    component.attendanceFromDate = '2026-07-30';
    component.attendanceToDate = '2026-07-29';
    facade.loadAttendanceHistory.calls.reset();

    component.applyAttendanceFilter();

    expect(component.attendanceRangeError).toContain('تاريخ البداية');
    expect(facade.loadAttendanceHistory).not.toHaveBeenCalled();
  });

  it('separates attendance empty and error states and retries only the failed request', () => {
    component.canViewAttendance = true;
    facade.loadAttendanceHistory.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.selectSection('attendance');
    fixture.detectChanges();
    expect(component.attendanceHistoryState).toBe('error');
    expect(component.attendanceHistoryError).toBe('تعذر تحميل سجل الحضور والانصراف.');

    facade.loadAttendanceHistory.and.returnValue(of({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 1 }));
    component.retryAttendanceHistory();
    fixture.detectChanges();
    expect(component.attendanceHistoryState).toBe('loaded');
    expect(component.attendanceHistory).toEqual([]);
  });

  it('cancels attendance history state when another worker replaces the open profile', () => {
    const response = new Subject<import('./worker-management.models').WorkerAttendanceHistoryPage>();
    facade.loadAttendanceHistory.and.returnValue(response);
    component.canViewAttendance = true;
    component.selectSection('attendance');
    const previous = component.worker;
    component.worker = { ...structuredClone(worker), id: '22222222-2222-2222-2222-222222222222' };
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: previous, firstChange: false, isFirstChange: () => false } });

    response.next({ items: [{ recordId: 'stale', productionDate: '2026-07-29', attendanceStatus: 'Present', source: null, movements: [] }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });

    expect(component.attendanceHistoryState).toBe('idle');
    expect(component.attendanceHistory).toEqual([]);
  });

  it('shows the organizational department action only with the existing combined permission input', () => {
    component.worker = { ...component.worker, organizationalDepartmentName: null, organizationalDepartmentId: null };
    component.selectSection('operations');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('غير معيّن');
    expect(fixture.nativeElement.textContent).not.toContain('تعيين إلى قسم');

    component.canAssignDepartment = true;
    fixture.detectChanges();
    const button = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .find((item: unknown) => (item as HTMLElement).textContent?.includes('تعيين إلى قسم')) as HTMLButtonElement;
    expect(button).toBeTruthy();
    spyOn(component.departmentAssignmentRequested, 'emit');
    button.click();
    expect(component.departmentAssignmentRequested.emit).toHaveBeenCalled();
  });
});

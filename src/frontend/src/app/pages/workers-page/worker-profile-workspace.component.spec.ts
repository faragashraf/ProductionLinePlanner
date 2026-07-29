import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { WorkerManagementFacade } from './worker-management.facade';
import { WorkerManagementProfile } from './worker-management.models';
import { WorkerProfileWorkspaceComponent } from './worker-profile-workspace.component';

describe('WorkerProfileWorkspaceComponent', () => {
  let fixture: ComponentFixture<WorkerProfileWorkspaceComponent>;
  let component: WorkerProfileWorkspaceComponent;
  let facade: jasmine.SpyObj<WorkerManagementFacade>;

  const worker: WorkerManagementProfile = {
    id: '11111111-1111-1111-1111-111111111111',
    local: { displayName: 'عامل محلي طويل الاسم', photoUrl: null, salary: null, profileStatus: 'complete', employmentStatus: 'active' },
    source: { sourceName: null, badgeNumber: 'B-1', employeeCode: 'EMP-1', employmentStatus: null, department: null, shift: null, lastObservedAt: null, linkStatus: 'linked' },
    assignments: [], history: [], sourcePreview: [], assignmentStatus: 'unassigned', defaultSubStageId: null
  };

  beforeEach(async () => {
    facade = jasmine.createSpyObj<WorkerManagementFacade>('WorkerManagementFacade', ['saveLocalProfile', 'uploadPhoto', 'deletePhoto']);
    facade.saveLocalProfile.and.returnValue(of(worker));
    facade.uploadPhoto.and.returnValue(of({ ...worker, local: { ...worker.local, photoUrl: '/api/workers/11111111-1111-1111-1111-111111111111/photo?v=' + 'a'.repeat(64) } }));
    facade.deletePhoto.and.returnValue(of(worker));
    await TestBed.configureTestingModule({
      declarations: [WorkerProfileWorkspaceComponent], imports: [CommonModule, ReactiveFormsModule],
      providers: [{ provide: WorkerManagementFacade, useValue: facade }], schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(WorkerProfileWorkspaceComponent);
    component = fixture.componentInstance;
    component.worker = structuredClone(worker);
    component.canManage = true;
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: null, firstChange: true, isFirstChange: () => true } });
    fixture.detectChanges();
  });

  it('renders application data and marks unavailable source values explicitly', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(worker.local.displayName);
    expect(text).toContain('لا تقرأ هذه الشاشة نظام البصمة مباشرةً');
    component.selectSection('source');
    fixture.detectChanges();
    const sourceName = fixture.nativeElement.querySelectorAll('[data-profile-section="source"] input')[2] as HTMLInputElement;
    expect(sourceName.value).toBe('غير متاح من قاعدة بيانات التطبيق');
  });

  it('saves local name and employment status through the facade', () => {
    component.draftForm.patchValue({ displayName: 'اسم محلي محفوظ', employmentStatus: 'inactive' });
    component.saveDraft();
    expect(facade.saveLocalProfile).toHaveBeenCalledWith(jasmine.any(Object), { displayName: 'اسم محلي محفوظ', employmentStatus: 'inactive' });
    expect(component.saveMessage).toContain('تم حفظ البيانات المحلية');
  });

  it('rejects an unsupported photo before the API call', () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['plain'], 'worker.gif', { type: 'image/gif' })] });
    component.onPhotoSelected(input);
    expect(component.photoError).toContain('JPEG');
    expect(facade.uploadPhoto).not.toHaveBeenCalled();
  });

  it('rejects a photo larger than 5 MiB before the API call', () => {
    const input = document.createElement('input');
    const oversized = new File([new Uint8Array((5 * 1024 * 1024) + 1)], 'worker.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [oversized] });
    component.onPhotoSelected(input);
    expect(component.photoError).toContain('5 MiB');
    expect(facade.uploadPhoto).not.toHaveBeenCalled();
  });

  it('uploads a selected valid photo and emits the cache-busted profile', () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['bitmap'], 'worker.bmp', { type: 'image/bmp' })] });
    component.onPhotoSelected(input);
    component.uploadSelectedPhoto();
    expect(facade.uploadPhoto).toHaveBeenCalled();
    expect(component.worker.local.photoUrl).toContain('?v=');
  });

  it('maps a server failure to a safe message without exposing backend detail', () => {
    facade.uploadPhoto.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 500,
      error: { message: 'SQL connection details must not reach the user' }
    })));
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['bitmap'], 'worker.bmp', { type: 'image/bmp' })] });
    component.onPhotoSelected(input);

    component.uploadSelectedPhoto();

    expect(component.photoError).toBe('تعذر رفع الصورة.');
    expect(component.photoError).not.toContain('SQL');
  });
});

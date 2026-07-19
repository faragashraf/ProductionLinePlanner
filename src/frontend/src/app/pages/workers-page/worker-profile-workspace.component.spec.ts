import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { WORKER_MANAGEMENT_FIXTURES } from './worker-management.fixtures';
import { WorkerManagementProfile } from './worker-management.models';
import { WorkerProfileWorkspaceComponent } from './worker-profile-workspace.component';

describe('WorkerProfileWorkspaceComponent', () => {
  let fixture: ComponentFixture<WorkerProfileWorkspaceComponent>;
  let component: WorkerProfileWorkspaceComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [WorkerProfileWorkspaceComponent],
      imports: [CommonModule, ReactiveFormsModule],
      schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(WorkerProfileWorkspaceComponent);
    component = fixture.componentInstance;
    component.worker = clone(WORKER_MANAGEMENT_FIXTURES[0]);
    component.canManage = true;
    component.canViewAssignments = true;
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: null, firstChange: true, isFirstChange: () => true } });
    fixture.detectChanges();
  });

  it('keeps the local name primary and labels source data as secondary', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(component.worker.local.displayName);
    expect(text).toContain('الاسم المرصود من نظام البصمة');
    expect(text).toContain(component.worker.source.sourceName!);
    expect(text).toContain('بيانات محلية');
    expect(text).toContain('من نظام البصمة');
  });

  it('renders every source field read-only', () => {
    component.selectSection('source');
    fixture.detectChanges();
    const fields = Array.from(fixture.nativeElement.querySelectorAll('[data-profile-section="source"] input')) as HTMLInputElement[];
    expect(fields.length).toBe(7);
    expect(fields.every(field => field.readOnly)).toBeTrue();
  });

  it('keeps local draft edits isolated from the fixture profile', () => {
    const originalName = component.worker.local.displayName;
    component.draftForm.patchValue({ displayName: 'اسم محلي داخل المسودة', salaryAmount: 12000 });
    component.saveDraft();
    expect(component.draftMessage).toContain('لم يتغير السجل الأصلي');
    expect(component.worker.local.displayName).toBe(originalName);
    expect(component.worker.local.salary?.amount).not.toBe(12000);
  });

  it('shows the missing-photo placeholder explanation and long Arabic names without truncating the value', () => {
    component.worker = clone(WORKER_MANAGEMENT_FIXTURES.find(worker => worker.id === 'worker-long-arabic-name')!);
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: null, firstChange: false, isFirstChange: () => false } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(component.worker.local.displayName);
    expect(fixture.nativeElement.textContent).toContain('لا توجد — يظهر البديل القياسي');
  });

  it('announces identity conflict without relying on color alone', () => {
    component.worker = clone(WORKER_MANAGEMENT_FIXTURES.find(worker => worker.id === 'worker-identity-conflict')!);
    component.ngOnChanges({ worker: { currentValue: component.worker, previousValue: null, firstChange: false, isFirstChange: () => false } });
    fixture.detectChanges();
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain('تعارض يحتاج مراجعة هوية');
  });

  it('exposes a no-action source preview and closes back to the list', () => {
    component.selectSection('source-preview');
    fixture.detectChanges();
    const preview = fixture.nativeElement.querySelector('[data-profile-section="source-preview"]') as HTMLElement;
    expect(component.activeSection).toBe('source-preview');
    expect(preview.textContent).toContain('BadgeNumber');
    expect(preview.querySelector('button')).toBeNull();
    spyOn(component.closed, 'emit');
    component.closed.emit();
    expect(component.closed.emit).toHaveBeenCalled();
  });
});

function clone(profile: WorkerManagementProfile): WorkerManagementProfile {
  return JSON.parse(JSON.stringify(profile)) as WorkerManagementProfile;
}

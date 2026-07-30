import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { PlpFormSheetComponent } from './plp-form-sheet.component';

describe('PlpFormSheetComponent', () => {
  let fixture: ComponentFixture<PlpFormSheetComponent>;
  let originalMatchMedia: typeof window.matchMedia;
  let messages: jasmine.SpyObj<MessageService>;

  function setViewportMode(bottomSheet: boolean, reducedMotion = false): void {
    window.matchMedia = ((query: string) => ({
      matches: query.includes('max-width') ? bottomSheet : reducedMotion,
      media: query,
      onchange: null,
      addEventListener: jasmine.createSpy('addEventListener'),
      removeEventListener: jasmine.createSpy('removeEventListener'),
      addListener: jasmine.createSpy('addListener'),
      removeListener: jasmine.createSpy('removeListener'),
      dispatchEvent: jasmine.createSpy('dispatchEvent')
    })) as typeof window.matchMedia;
  }

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
    messages = jasmine.createSpyObj<MessageService>('MessageService', ['add']);
    TestBed.configureTestingModule({
      imports: [PlpFormSheetComponent, NoopAnimationsModule],
      providers: [{ provide: MessageService, useValue: messages }]
    });
  });

  afterEach(() => {
    fixture?.destroy();
    document.body.querySelectorAll('.p-dialog-mask').forEach(element => element.remove());
    window.matchMedia = originalMatchMedia;
  });

  it('uses PrimeNG bottom positioning and mask classes for phone and tablet sheets', () => {
    setViewportMode(true);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.dialogPosition).toBe('bottom');
    expect(fixture.componentInstance.sheetClass).toContain('plp-form-sheet--bottom');
    expect(fixture.componentInstance.sheetMaskClass).toContain('plp-form-sheet-mask--bottom');
    expect(document.body.querySelector('.p-dialog-mask.plp-form-sheet-mask')).not.toBeNull();
  });

  it('uses a centered dialog contract on desktop', () => {
    setViewportMode(false);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.dialogPosition).toBe('center');
    expect(fixture.componentInstance.sheetClass).toContain('plp-form-sheet--desktop');
    expect(fixture.componentInstance.transitionOptions).toContain('150ms');
  });

  it('applies the opt-in compact header density without changing dialog action sizing', () => {
    setViewportMode(false);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('compactHeader', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.sheetClass).toContain('plp-form-sheet--compact-header');
    const saveButton = document.body.querySelector('.p-dialog-footer .plp-action-button') as HTMLElement;
    expect(parseFloat(getComputedStyle(saveButton).minHeight)).toBeGreaterThanOrEqual(44);
  });

  it('applies an optional capability layout class to the real PrimeNG surface', () => {
    setViewportMode(false);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('styleClass', 'plp-form-sheet--staffing-directory');
    fixture.detectChanges();

    expect(fixture.componentInstance.sheetClass).toContain('plp-form-sheet--staffing-directory');
    expect(document.body.querySelector('.p-dialog.plp-form-sheet--staffing-directory')).not.toBeNull();
  });

  it('honors reduced motion and prevents duplicate save requests while the request is active', () => {
    setViewportMode(true, true);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const save = jasmine.createSpy('save');
    fixture.componentInstance.save.subscribe(save);

    fixture.componentInstance.requestSave();
    fixture.componentInstance.requestSave();

    expect(fixture.componentInstance.transitionOptions).toBe('0ms');
    expect(save).toHaveBeenCalledTimes(1);
  });

  it('keeps the sheet open when a save fails and emits close lifecycle state only when requested', () => {
    setViewportMode(true);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('error', 'تعذر الحفظ');
    fixture.detectChanges();
    const visibleChange = jasmine.createSpy('visibleChange');
    fixture.componentInstance.visibleChange.subscribe(visibleChange);

    fixture.componentInstance.requestSave();
    fixture.detectChanges();

    expect(fixture.componentInstance.visible).toBeTrue();
    expect(visibleChange).not.toHaveBeenCalled();
  });

  it('keeps the PrimeNG dialog close button functional with the shared close contract', () => {
    setViewportMode(false);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const visibleChange = jasmine.createSpy('visibleChange');
    fixture.componentInstance.visibleChange.subscribe(visibleChange);

    const closeButton = document.body.querySelector('.p-dialog .p-dialog-header-close') as HTMLButtonElement;
    const closeIcon = closeButton.querySelector('.p-dialog-header-close-icon') as HTMLElement;
    expect(closeButton).not.toBeNull();
    expect(closeButton.getAttribute('aria-label')).toBe('إغلاق النافذة');
    expect(closeIcon).not.toBeNull();
    expect(closeIcon.classList).toContain('p-dialog-header-close-icon');

    fixture.componentInstance.handleVisibleChange(false);

    expect(visibleChange).toHaveBeenCalledWith(false);
  });

  it('shows one success toast only after a requested save closes successfully', () => {
    setViewportMode(true);
    fixture = TestBed.createComponent(PlpFormSheetComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    fixture.componentInstance.requestSave();
    fixture.componentRef.setInput('saving', true);
    fixture.detectChanges();
    fixture.componentRef.setInput('successMessage', 'تم حفظ العامل.');
    fixture.componentRef.setInput('saving', false);
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();

    expect(messages.add).toHaveBeenCalledWith(jasmine.objectContaining({ severity: 'success', detail: 'تم حفظ العامل.' }));
  });
});

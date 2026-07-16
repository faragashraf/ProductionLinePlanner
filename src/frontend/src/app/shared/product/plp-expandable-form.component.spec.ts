import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { PlpExpandableFormComponent } from './plp-expandable-form.component';
import { PlpFormSheetComponent } from './plp-form-sheet.component';

describe('PlpExpandableFormComponent', () => {
  let fixture: ComponentFixture<PlpExpandableFormComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlpExpandableFormComponent, NoopAnimationsModule]
    });
    fixture = TestBed.createComponent(PlpExpandableFormComponent);
  });

  afterEach(() => {
    fixture?.destroy();
    document.body.querySelectorAll('.p-dialog-mask').forEach(element => element.remove());
  });

  it('forwards saving and failed-save state to the shared form sheet and keeps it open', () => {
    fixture.componentRef.setInput('expanded', true);
    fixture.componentRef.setInput('saving', true);
    fixture.componentRef.setInput('error', 'تعذر حفظ التغيير.');
    fixture.detectChanges();
    const sheet = fixture.debugElement.query(By.directive(PlpFormSheetComponent)).componentInstance as PlpFormSheetComponent;
    const expandedChange = jasmine.createSpy('expandedChange');
    fixture.componentInstance.expandedChange.subscribe(expandedChange);

    fixture.componentInstance.toggle();
    fixture.componentInstance.onSheetVisibleChange(false);

    expect(sheet.saving).toBeTrue();
    expect(sheet.error).toBe('تعذر حفظ التغيير.');
    expect(fixture.componentInstance.expanded).toBeTrue();
    expect(expandedChange).not.toHaveBeenCalled();
  });
});

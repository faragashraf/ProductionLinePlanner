import { FormBuilder, Validators } from '@angular/forms';
import { FormSubmissionValidationService } from './form-submission-validation.service';

describe('FormSubmissionValidationService', () => {
  const formBuilder = new FormBuilder();

  it('marks known required fields, returns one Arabic summary, and focuses the first invalid control', async () => {
    const service = new FormSubmissionValidationService();
    const form = formBuilder.group({ orderNumber: ['', Validators.required], productionDate: ['', Validators.required] });
    const root = document.createElement('form');
    const first = document.createElement('input');
    first.setAttribute('formControlName', 'orderNumber');
    const focus = spyOn(first, 'focus');
    root.append(first);

    const result = service.validate(form, [
      { control: 'orderNumber', message: 'أمر الإنتاج مطلوب' },
      { control: 'productionDate', message: 'تاريخ الإنتاج مطلوب' }
    ], [], root);
    await Promise.resolve();

    expect(result.valid).toBeFalse();
    expect(result.messages).toEqual(['أمر الإنتاج مطلوب', 'تاريخ الإنتاج مطلوب']);
    expect(result.summary).toContain('أمر الإنتاج مطلوب');
    expect(form.controls.orderNumber.touched).toBeTrue();
    expect(focus).toHaveBeenCalled();
  });

  it('does not add a local required-field error when the form is valid', () => {
    const service = new FormSubmissionValidationService();
    const form = formBuilder.group({ orderNumber: ['PO-119', Validators.required] });

    const result = service.validate(form, [{ control: 'orderNumber', message: 'أمر الإنتاج مطلوب' }]);

    expect(result.valid).toBeTrue();
    expect(result.messages).toEqual([]);
  });

  it('keeps a specific server validation message instead of a generic HTTP transport message', () => {
    const service = new FormSubmissionValidationService();

    expect(service.serverMessage({ error: { error: { message: 'يجب إضافة عامل واحد على الأقل إلى دفعة الإنتاج.' } } }, 'تعذر حفظ المسودة.'))
      .toBe('يجب إضافة عامل واحد على الأقل إلى دفعة الإنتاج.');
    expect(service.serverMessage({ error: { detail: 'تفاصيل مشكلة الخادم القابلة للقراءة.' } }, 'تعذر حفظ المسودة.'))
      .toBe('تفاصيل مشكلة الخادم القابلة للقراءة.');
    expect(service.serverMessage({ message: 'Http failure response for /api/production/records: 400 Bad Request' }, 'تعذر حفظ المسودة.'))
      .toBe('تعذر حفظ المسودة.');
  });
});

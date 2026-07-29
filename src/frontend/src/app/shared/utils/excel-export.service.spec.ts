import { TestBed } from '@angular/core/testing';
import {
  ExcelExportError,
  ExcelRow,
  ExcelExportService,
  loadExcelJsModule,
  resolveExcelJsModule,
  toExcelSafeCellValue
} from './excel-export.service';

describe('ExcelExportService', () => {
  let service: ExcelExportService;

  beforeEach(() => service = TestBed.inject(ExcelExportService));

  it('builds a typed multi-sheet workbook with the supplied values', async () => {
    const workbook = await service.buildWorkbook({
      fileName: 'daily.xlsx',
      worksheets: [
        { name: 'ملخص التشغيل', rows: [{ البيان: 'الكمية', القيمة: 1000 }] },
        { name: 'الإنتاج حسب المراحل', rows: [{ المرحلة: 'S1', الكمية: 250 }] },
        { name: 'توزيع العاملين', rows: [{ العامل: 'W1', الكمية: 250 }] }
      ]
    });

    expect(workbook.worksheets.map(sheet => sheet.name)).toEqual(['ملخص التشغيل', 'الإنتاج حسب المراحل', 'توزيع العاملين']);
    const workerSheet = workbook.getWorksheet('توزيع العاملين')!;
    expect(workerSheet.getRow(2).getCell(1).value).toBe('W1');
    expect(workerSheet.getRow(2).getCell(2).value).toBe(250);
  });

  it('sanitizes file and worksheet names without changing workbook data', async () => {
    expect(service.safeFileName('Daily/Line:1*Model?')).toBe('Daily-Line-1-Model.xlsx');
    const workbook = await service.buildWorkbook({ fileName: 'daily', worksheets: [{ name: 'تفاصيل/اليوم:*?', rows: [{ id: 1 }] }] });
    expect(workbook.worksheets[0].name).toBe('تفاصيل-اليوم---');
  });

  it('resolves namespace and default CommonJS interop shapes and creates a workbook from both', async () => {
    const browserModule = resolveExcelJsModule(await import('exceljs'));
    const namespaceModule = await loadExcelJsModule(async () => ({ Workbook: browserModule.Workbook }));
    const defaultModule = await loadExcelJsModule(async () => ({ default: { Workbook: browserModule.Workbook } }));

    expect(new namespaceModule.Workbook()).toBeTruthy();
    expect(new defaultModule.Workbook()).toBeTruthy();
  });

  it('converts defensive cell inputs to Excel-safe primitives', () => {
    expect(toExcelSafeCellValue(undefined)).toBeNull();
    expect(toExcelSafeCellValue(Number.POSITIVE_INFINITY)).toBe('');
    expect(toExcelSafeCellValue(new Date('invalid'))).toBe('');
    expect(toExcelSafeCellValue(['مرحلة 1', 'مرحلة 2'])).toBe('مرحلة 1، مرحلة 2');
    expect(toExcelSafeCellValue({ label: 'خط 1', id: 'line-1' })).toBe('خط 1');
    expect(toExcelSafeCellValue({ toNumber: () => 12.5 })).toBe(12.5);
    expect(typeof toExcelSafeCellValue({ nested: { value: 1 } })).toBe('string');
  });

  it('never passes an object value directly to a worksheet cell', async () => {
    const defensiveRow = { القيمة: { label: 'مصنع الاختبار', id: 'factory-1' } } as unknown as ExcelRow;
    const workbook = await service.buildWorkbook({ fileName: 'daily', worksheets: [{ name: 'ملخص', rows: [defensiveRow] }] });

    expect(workbook.getWorksheet('ملخص')?.getRow(2).getCell(1).value).toBe('مصنع الاختبار');
  });

  it('formats accounting sheets with RTL, frozen headers, filters, numeric formats, and a total row', async () => {
    const workbook = await service.buildWorkbook({
      fileName: 'daily',
      worksheets: [{
        name: 'تفاصيل الإنتاج',
        rows: [
          { العامل: 'W1', الكمية: 250, القيمة: 150 },
          { العامل: 'الإجمالي', الكمية: 250, القيمة: 150 }
        ],
        columnWidths: [18, 16, 16],
        columnFormats: { الكمية: '#,##0.####', القيمة: '#,##0.0000' },
        footerRowCount: 1
      }]
    });
    const worksheet = workbook.getWorksheet('تفاصيل الإنتاج')!;

    expect(worksheet.views[0]).toEqual(jasmine.objectContaining({ state: 'frozen', ySplit: 1, rightToLeft: true }));
    expect(worksheet.autoFilter).toBeTruthy();
    expect(worksheet.getColumn(2).numFmt).toBe('#,##0.####');
    expect(worksheet.getRow(3).font?.bold).toBeTrue();
    expect(worksheet.getRow(1).font?.bold).toBeTrue();
  });

  it('rejects an unsupported module shape before workbook construction', async () => {
    await expectAsync(loadExcelJsModule(async () => ({ default: {} })))
      .toBeRejectedWithError(TypeError, 'ExcelJS Workbook constructor is unavailable in the loaded browser module.');
  });

  it('serializes and downloads a real workbook buffer', async () => {
    const createObjectUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:excel-test');
    const revokeObjectUrl = spyOn(URL, 'revokeObjectURL');
    const anchorClick = spyOn(HTMLAnchorElement.prototype, 'click');

    await service.exportWorkbook({ fileName: 'daily', worksheets: [{ name: 'ملخص التشغيل', rows: [{ الكمية: 1000 }] }] });

    expect(createObjectUrl).toHaveBeenCalled();
    expect(anchorClick).toHaveBeenCalledTimes(1);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:excel-test');
  });

  it('reports serializing-workbook when writeBuffer fails', async () => {
    const workbook = await service.buildWorkbook({ fileName: 'daily', worksheets: [{ name: 'ملخص', rows: [{ id: 1 }] }] });
    spyOn(service, 'buildWorkbook').and.resolveTo(workbook);
    spyOn(workbook.xlsx, 'writeBuffer').and.rejectWith(new Error('buffer failed'));

    await expectAsync(service.exportWorkbook({ fileName: 'daily', worksheets: [] }))
      .toBeRejectedWith(jasmine.objectContaining<ExcelExportError>({ step: 'serializing-workbook' }));
  });

  it('reports downloading-file when Blob URL creation fails', async () => {
    const workbook = await service.buildWorkbook({ fileName: 'daily', worksheets: [{ name: 'ملخص', rows: [{ id: 1 }] }] });
    spyOn(service, 'buildWorkbook').and.resolveTo(workbook);
    spyOn(URL, 'createObjectURL').and.throwError('blob failed');

    await expectAsync(service.exportWorkbook({ fileName: 'daily', worksheets: [] }))
      .toBeRejectedWith(jasmine.objectContaining<ExcelExportError>({ step: 'downloading-file' }));
  });
});

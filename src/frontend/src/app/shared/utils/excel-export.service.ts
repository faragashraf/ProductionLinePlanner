import { Injectable } from '@angular/core';
import type { Workbook } from 'exceljs';

export type ExcelCellValue = string | number | boolean | Date | null | undefined;
export type ExcelRow = Readonly<Record<string, ExcelCellValue>>;
export type ExcelExportStep = 'loading-library' | 'building-workbook' | 'serializing-workbook' | 'downloading-file';

type ExcelJsWorkbookConstructor = new () => Workbook;
interface ExcelJsApi { Workbook: ExcelJsWorkbookConstructor; }
interface ExcelJsInteropModule { default?: unknown; Workbook?: unknown; }

export class ExcelExportError extends Error {
  constructor(readonly step: ExcelExportStep, override readonly cause: unknown) {
    super(`Excel export failed at ${step}.`);
    this.name = 'ExcelExportError';
  }
}

export function resolveExcelJsModule(moduleValue: unknown): ExcelJsApi {
  const module = moduleValue as ExcelJsInteropModule | null;
  const namespaceCandidate = module?.Workbook;
  if (typeof namespaceCandidate === 'function') return { Workbook: namespaceCandidate as ExcelJsWorkbookConstructor };

  const defaultModule = module?.default as ExcelJsInteropModule | null;
  const defaultCandidate = defaultModule?.Workbook;
  if (typeof defaultCandidate === 'function') return { Workbook: defaultCandidate as ExcelJsWorkbookConstructor };

  throw new TypeError('ExcelJS Workbook constructor is unavailable in the loaded browser module.');
}

export async function loadExcelJsModule(loader: () => Promise<unknown> = () => import('exceljs')): Promise<ExcelJsApi> {
  return resolveExcelJsModule(await loader());
}

export function toExcelSafeCellValue(value: unknown): Exclude<ExcelCellValue, undefined> {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') return Number.isFinite(value) ? value : '';
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? '' : value;
  if (Array.isArray(value)) return value.map(item => toExcelSafeCellValue(item)).filter(item => item !== null && item !== '').join('، ');
  if (typeof value === 'object') {
    const record = value as Readonly<Record<string, unknown>>;
    const preferredValue = record['label'] ?? record['name'] ?? record['code'];
    if (preferredValue !== undefined && preferredValue !== value) return toExcelSafeCellValue(preferredValue);
    const toNumber = record['toNumber'];
    if (typeof toNumber === 'function') {
      try {
        const numericValue = toNumber.call(value);
        if (typeof numericValue === 'number' && Number.isFinite(numericValue)) return numericValue;
      } catch {
        return '';
      }
    }
    try {
      return JSON.stringify(value) ?? '';
    } catch {
      return '';
    }
  }
  return String(value);
}

export interface ExcelWorksheetDefinition {
  name: string;
  rows: readonly ExcelRow[];
  columnWidths?: readonly number[];
  columnFormats?: Readonly<Record<string, string>>;
  footerRowCount?: number;
}

export interface ExcelWorkbookDefinition {
  fileName: string;
  worksheets: readonly ExcelWorksheetDefinition[];
}

@Injectable({ providedIn: 'root' })
export class ExcelExportService {
  async buildWorkbook(definition: ExcelWorkbookDefinition): Promise<Workbook> {
    let ExcelJS: ExcelJsApi;
    try {
      ExcelJS = await loadExcelJsModule();
    } catch (error) {
      throw new ExcelExportError('loading-library', error);
    }

    try {
      const workbook = new ExcelJS.Workbook();
      workbook.creator = 'ProductionLinePlanner';
      workbook.created = new Date();
      definition.worksheets.forEach(definitionSheet => {
        const worksheet = workbook.addWorksheet(this.safeWorksheetName(definitionSheet.name), { views: [{ rightToLeft: true }] });
        const headers = [...new Set(definitionSheet.rows.flatMap(row => Object.keys(row)))];
        worksheet.columns = headers.map((header, index) => ({
          header,
          key: header,
          width: definitionSheet.columnWidths?.[index] ?? 18
        }));
        definitionSheet.rows.forEach(row => worksheet.addRow(Object.fromEntries(
          headers.map(header => [header, toExcelSafeCellValue(row[header])])
        )));
        const headerRow = worksheet.getRow(1);
        headerRow.font = { bold: true, color: { argb: 'FFFFFFFF' } };
        headerRow.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1F4E78' } };
        headerRow.alignment = { horizontal: 'right', vertical: 'middle', wrapText: true };
        headerRow.height = 28;
        worksheet.views = [{ state: 'frozen', ySplit: 1, rightToLeft: true }];
        if (headers.length && worksheet.rowCount > 1) {
          worksheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: worksheet.rowCount, column: headers.length } };
        }
        worksheet.eachRow((row, rowNumber) => {
          row.alignment = { horizontal: 'right', vertical: 'middle', wrapText: true };
          if (rowNumber > 1) row.height = 22;
        });
        Object.entries(definitionSheet.columnFormats ?? {}).forEach(([header, format]) => {
          const columnIndex = headers.indexOf(header) + 1;
          if (!columnIndex) return;
          worksheet.getColumn(columnIndex).numFmt = format;
        });
        const footerRowCount = Math.max(0, definitionSheet.footerRowCount ?? 0);
        for (let index = 0; index < footerRowCount; index += 1) {
          const footerRow = worksheet.getRow(worksheet.rowCount - index);
          footerRow.font = { bold: true };
          footerRow.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFEAF2F8' } };
        }
      });
      return workbook;
    } catch (error) {
      throw new ExcelExportError('building-workbook', error);
    }
  }

  async exportWorkbook(definition: ExcelWorkbookDefinition): Promise<void> {
    const workbook = await this.buildWorkbook(definition);
    let bytes: Awaited<ReturnType<Workbook['xlsx']['writeBuffer']>>;
    try {
      bytes = await workbook.xlsx.writeBuffer();
    } catch (error) {
      throw new ExcelExportError('serializing-workbook', error);
    }

    let url: string | null = null;
    let anchor: HTMLAnchorElement | null = null;
    try {
      const blob = new Blob([bytes as unknown as BlobPart], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      url = URL.createObjectURL(blob);
      anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = this.safeFileName(definition.fileName, 'xlsx');
      anchor.style.display = 'none';
      document.body.appendChild(anchor);
      anchor.click();
    } catch (error) {
      throw new ExcelExportError('downloading-file', error);
    } finally {
      anchor?.remove();
      if (url) URL.revokeObjectURL(url);
    }
  }

  safeFileName(value: string, extension = 'xlsx'): string {
    const baseName = value.replace(/[\\/:*?"<>|]/g, '-').replace(/\s+/g, '-').replace(/-+/g, '-').replace(/^[.-]+|[.-]+$/g, '') || 'export';
    const normalizedExtension = extension.replace(/^\.+/, '');
    return baseName.toLocaleLowerCase().endsWith(`.${normalizedExtension.toLocaleLowerCase()}`)
      ? baseName
      : `${baseName}.${normalizedExtension}`;
  }

  private safeWorksheetName(value: string): string {
    return (value.replace(/[\\/?*:[\]]/g, '-').trim() || 'Sheet').slice(0, 31);
  }
}

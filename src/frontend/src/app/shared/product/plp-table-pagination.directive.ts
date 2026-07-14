import { Directive, DoCheck, Host, HostListener, Input, OnChanges, SimpleChanges, booleanAttribute } from '@angular/core';
import { Table } from 'primeng/table';

export const PLP_TABLE_PAGE_SIZE_OPTIONS = [5, 10, 20, 50] as const;

/**
 * Shared client/server-ready paginator configuration for Product Experience
 * Framework tables. It deliberately stays at the PrimeNG table boundary so
 * sorting, selection, filtering, and stable row identities keep working.
 */
@Directive({
  selector: 'p-table[plpTablePagination]',
  standalone: true,
  host: {
    class: 'plp-table-pagination',
    '[attr.data-plp-pagination]': 'plpTablePagination'
  }
})
export class PlpTablePaginationDirective implements OnChanges, DoCheck {
  @Input({ transform: booleanAttribute }) plpTablePagination = true;
  @Input() plpPaginationPageSize = 10;
  @Input() plpPaginationMobilePageSize: number | null = null;
  @Input() plpPaginationDesktopPageSize: number | null = null;
  @Input() plpPaginationPageSizeOptions: readonly number[] = PLP_TABLE_PAGE_SIZE_OPTIONS;
  @Input() plpPaginationResetKey: unknown;

  private previousResetKey: unknown;
  private appliedDefaultRows = 0;
  constructor(@Host() private readonly table: Table) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['plpPaginationResetKey'] && !changes['plpPaginationResetKey'].firstChange) {
      this.resetToFirstPage();
    }
    this.configure();
    this.previousResetKey = this.plpPaginationResetKey;
  }

  ngDoCheck(): void {
    if (!this.plpTablePagination || this.table.lazy) {
      return;
    }

    const totalRecords = this.table.totalRecords ?? this.table.value?.length ?? 0;
    const rows = this.normalizedRows;
    if (totalRecords === 0 || (this.table.first ?? 0) < totalRecords) {
      return;
    }

    this.table.first = Math.max(0, Math.floor((totalRecords - 1) / rows) * rows);
  }

  @HostListener('window:resize')
  onViewportResize(): void {
    if (!this.plpTablePagination || (this.table.rows ?? 0) !== this.appliedDefaultRows) {
      return;
    }

    this.applyResponsiveDefaultRows();
  }

  private configure(): void {
    if (!this.plpTablePagination) {
      return;
    }

    this.table.paginator = true;
    this.applyResponsiveDefaultRows();
    this.table.rowsPerPageOptions = [...this.plpPaginationPageSizeOptions];
    this.table.alwaysShowPaginator = true;
    this.table.showCurrentPageReport = true;
    this.table.currentPageReportTemplate = 'عرض {first}–{last} من {totalRecords} · ص {currentPage}/{totalPages}';
    this.table.showFirstLastIcon = false;
    this.table.showPageLinks = true;
    this.table.paginatorStyleClass = 'plp-table-pagination__paginator';
  }

  private resetToFirstPage(): void {
    if (!this.plpTablePagination || this.previousResetKey === this.plpPaginationResetKey) {
      return;
    }

    this.table.first = 0;
    this.table.firstChange.emit(0);
  }

  private get normalizedRows(): number {
    const tableRows = this.table.rows ?? 0;
    return tableRows > 0 ? tableRows : this.responsiveDefaultRows;
  }

  private applyResponsiveDefaultRows(): void {
    const currentRows = this.table.rows ?? 0;
    const canApplyDefault = currentRows < 1 || currentRows === this.appliedDefaultRows;
    const nextDefault = this.responsiveDefaultRows;
    if (canApplyDefault) {
      this.table.rows = nextDefault;
    }
    this.appliedDefaultRows = nextDefault;
  }

  private get responsiveDefaultRows(): number {
    const isPhone = typeof window !== 'undefined' && window.matchMedia('(max-width: 767px)').matches;
    const responsiveSize = isPhone ? this.plpPaginationMobilePageSize : this.plpPaginationDesktopPageSize;
    return Math.max(responsiveSize ?? this.plpPaginationPageSize, 1);
  }
}

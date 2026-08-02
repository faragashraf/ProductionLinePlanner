import { AfterViewInit, ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, HostListener, Input, OnChanges, OnDestroy, Optional, SimpleChanges, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { OverlayPanel } from 'primeng/overlaypanel';
import { buildApiUrl } from '../../../core/config/api.config';
import { WorkerPhotoClientCacheService } from '../../../core/services/worker-photo-client-cache.service';
import { FactoryStatus, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-worker-avatar',
  templateUrl: './worker-avatar.component.html',
  styleUrls: ['./worker-avatar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAvatarComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('photoPreview') private photoPreview?: OverlayPanel;

  @Input() fullName: unknown = '';
  @Input() code: unknown = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() imageUrl?: unknown;
  @Input() photoReference?: unknown;
  @Input() hasPhoto?: boolean;
  @Input() photoVersion?: string | null;
  @Input() decorative = false;
  @Input() lazy = true;

  private imageFailed = false;
  private lastImageReference = '';
  private protectedImageUrl = '';
  private protectedPhotoRequest?: Subscription;
  private photoObserver?: IntersectionObserver;
  private canLoadProtectedPhoto = false;
  private photoLoading = false;
  private viewInitialized = false;
  private photoPreviewOpen = false;

  constructor(
    @Optional() private readonly photoCache?: WorkerPhotoClientCacheService,
    @Optional() private readonly changeDetector?: ChangeDetectorRef,
    @Optional() private readonly host?: ElementRef<HTMLElement>
  ) {}

  ngAfterViewInit(): void {
    this.viewInitialized = true;
    this.armProtectedPhotoLoading(this.currentReference);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['photoReference'] && !changes['imageUrl'] && !changes['hasPhoto'] && !changes['photoVersion']) return;

    const nextReference = this.currentReference;
    if (nextReference !== this.lastImageReference || this.hasPhoto === false) {
      this.lastImageReference = nextReference;
      this.imageFailed = false;
      this.clearProtectedImage();
    }

    if (this.hasPhoto === false) {
      this.canLoadProtectedPhoto = false;
      this.photoObserver?.disconnect();
      this.photoObserver = undefined;
      return;
    }

    // Directly-instantiated tests and non-DOM consumers cannot observe
    // visibility, so they remain deterministic without bypassing cache sharing.
    if (!this.host || !this.lazy) {
      this.canLoadProtectedPhoto = true;
    }
    if (this.canLoadProtectedPhoto) {
      this.loadProtectedPhoto(nextReference);
    } else if (this.viewInitialized) {
      this.armProtectedPhotoLoading(nextReference);
    }
  }

  ngOnDestroy(): void {
    this.photoObserver?.disconnect();
    this.clearProtectedImage();
  }

  get initials(): string {
    const parts = this.safeFullName.trim().split(' ').map((item) => item.trim()).filter(Boolean);
    if (!parts.length) return this.safeCode ? this.safeCode.slice(0, 2) : '؟';
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
    return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
  }

  get safeFullName(): string { return this.coerceLabel(this.fullName); }
  get safeCode(): string { return this.coerceLabel(this.code); }
  get isLoadingPhoto(): boolean { return this.photoLoading; }

  get safeImageUrl(): string {
    if (this.imageFailed || this.hasPhoto === false) return '';
    const candidate = this.currentReference;
    if (this.isProtectedWorkerPhotoReference(candidate)) return this.protectedImageUrl;
    return this.isSafeSameOriginReference(candidate) ? candidate : '';
  }

  get imageAlt(): string {
    if (this.decorative) return '';
    return this.safeFullName ? `صورة العامل ${this.safeFullName}` : 'الصورة الافتراضية للعامل';
  }

  onImageError(): void {
    this.imageFailed = true;
    this.clearProtectedImage();
    this.changeDetector?.markForCheck();
  }

  showPhotoPreview(event: Event, preview: OverlayPanel): void {
    if (!this.safeImageUrl) return;
    preview.show(event);
  }

  togglePhotoPreview(event: Event, preview: OverlayPanel): void {
    if (!this.safeImageUrl) return;

    if (this.photoPreviewOpen) {
      preview.hide();
      return;
    }

    preview.show(event);
  }

  hidePhotoPreview(preview: OverlayPanel): void {
    preview.hide();
  }

  onPhotoPreviewShow(): void {
    this.photoPreviewOpen = true;
  }

  onPhotoPreviewHide(): void {
    this.photoPreviewOpen = false;
  }

  @HostListener('document:pointerdown', ['$event'])
  onGlobalPointerDown(event: PointerEvent): void {
    if (!this.photoPreviewOpen || !this.host?.nativeElement) return;

    const target = event.target as Node | null;
    if (!target || this.host.nativeElement.contains(target)) return;

    this.photoPreview?.hide();
    this.photoPreviewOpen = false;
  }

  get statusTone(): string { return `plp-worker-avatar--${resolveFactoryStatus(this.status).toneClass}`; }
  get avatarClasses(): string { return `plp-worker-avatar ${this.statusTone} plp-worker-avatar--${this.size}`; }

  private get currentReference(): string {
    return this.coerceLabel(this.photoReference) || this.coerceLabel(this.imageUrl);
  }

  private coerceLabel(value: unknown): string {
    if (typeof value === 'string') return value;
    if (typeof value === 'number') return String(value);
    return '';
  }

  private isSafeSameOriginReference(reference: string): boolean {
    const candidate = reference.trim();
    if (!candidate || candidate.startsWith('data:') || candidate.startsWith('javascript:') || candidate.startsWith('//')) return false;
    try {
      const resolved = new URL(candidate, document.baseURI);
      return resolved.origin === document.location.origin && (resolved.protocol === 'http:' || resolved.protocol === 'https:');
    } catch { return false; }
  }

  private isProtectedWorkerPhotoReference(reference: string): boolean {
    const candidate = reference.trim();
    if (!candidate) return false;
    if (/^\/api\/workers\/[0-9a-f-]{36}\/photo(?:\?.*)?$/i.test(candidate)) return true;
    try {
      const apiReference = new URL(buildApiUrl('/api/workers/placeholder/photo'), document.baseURI);
      const resolved = new URL(candidate, document.baseURI);
      return resolved.origin === apiReference.origin && /^\/api\/workers\/[0-9a-f-]{36}\/photo$/i.test(resolved.pathname);
    } catch { return false; }
  }

  private loadProtectedPhoto(reference: string): void {
    if (this.imageFailed || this.hasPhoto === false || !this.isProtectedWorkerPhotoReference(reference) || !this.photoCache || this.protectedPhotoRequest) return;

    this.photoLoading = true;
    this.protectedPhotoRequest = this.photoCache.load(reference).subscribe((image) => {
      this.photoLoading = false;
      if (!image || typeof URL.createObjectURL !== 'function') {
        this.imageFailed = true;
      } else {
        this.protectedImageUrl = URL.createObjectURL(image);
      }
      this.protectedPhotoRequest = undefined;
      this.changeDetector?.markForCheck();
    });
  }

  private armProtectedPhotoLoading(reference: string): void {
    if (!this.isProtectedWorkerPhotoReference(reference) || this.hasPhoto === false) return;

    if (!this.lazy || !this.host || typeof IntersectionObserver === 'undefined') {
      this.canLoadProtectedPhoto = true;
      this.loadProtectedPhoto(reference);
      return;
    }

    this.photoObserver?.disconnect();
    this.photoObserver = new IntersectionObserver((entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return;
      this.canLoadProtectedPhoto = true;
      this.photoObserver?.disconnect();
      this.photoObserver = undefined;
      this.loadProtectedPhoto(this.currentReference);
    }, { rootMargin: '160px 0px' });
    this.photoObserver.observe(this.host.nativeElement);
  }

  private clearProtectedImage(): void {
    this.protectedPhotoRequest?.unsubscribe();
    this.protectedPhotoRequest = undefined;
    this.photoLoading = false;
    if (this.protectedImageUrl && typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(this.protectedImageUrl);
    this.protectedImageUrl = '';
  }
}

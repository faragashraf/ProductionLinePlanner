import { HttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { OverlayPanel } from 'primeng/overlaypanel';
import { buildApiUrl } from '../../../core/config/api.config';
import { WorkerPhotoClientCacheService } from '../../../core/services/worker-photo-client-cache.service';
import { WorkerAvatarComponent } from './worker-avatar.component';

describe('WorkerAvatarComponent', () => {
  it('uses a same-origin worker photo reference when one is available', () => {
    const component = new WorkerAvatarComponent();
    component.fullName = 'أحمد سعيد';
    component.photoReference = '/assets/worker-photos/worker-101.jpg';

    expect(component.safeImageUrl).toBe('/assets/worker-photos/worker-101.jpg');
    expect(component.imageAlt).toContain('أحمد سعيد');
  });

  it('keeps detail avatars larger than compact assignment avatars', () => {
    const detail = new WorkerAvatarComponent();
    detail.size = 'lg';
    const assignment = new WorkerAvatarComponent();
    assignment.size = 'md';

    expect(detail.avatarClasses).toContain('plp-worker-avatar--lg');
    expect(assignment.avatarClasses).toContain('plp-worker-avatar--md');
  });

  it('uses the official default avatar and does not request a known missing worker photo', () => {
    const get = jasmine.createSpy('get');
    const cache = new WorkerPhotoClientCacheService({ get } as unknown as HttpClient);
    const component = new WorkerAvatarComponent(cache);
    component.hasPhoto = false;
    component.photoReference = '/api/workers/11111111-1111-1111-1111-111111111119/photo';
    component.ngOnChanges({ hasPhoto: {} as any, photoReference: {} as any });

    expect(component.safeImageUrl).toBe('');
    expect(get).not.toHaveBeenCalled();
  });

  it('falls back to the default avatar after an image load failure and rejects inline image payloads', () => {
    const component = new WorkerAvatarComponent();
    component.photoReference = '/assets/worker-photos/missing.jpg';
    component.onImageError();
    expect(component.safeImageUrl).toBe('');

    const inlineImage = new WorkerAvatarComponent();
    inlineImage.photoReference = 'data:image/png;base64,large-payload';
    expect(inlineImage.safeImageUrl).toBe('');
  });

  it('shows the large photo preview on hover and hides it on mouse leave', () => {
    const component = new WorkerAvatarComponent();
    const preview = jasmine.createSpyObj<OverlayPanel>('OverlayPanel', ['show', 'hide']);
    const event = new MouseEvent('mouseenter');
    component.photoReference = '/assets/worker-photos/worker-101.jpg';

    component.showPhotoPreview(event, preview);
    component.hidePhotoPreview(preview);

    expect(preview.show).toHaveBeenCalledOnceWith(event);
    expect(preview.hide).toHaveBeenCalledTimes(1);
  });

  it('does not show a large preview for the default worker avatar', () => {
    const component = new WorkerAvatarComponent();
    const preview = jasmine.createSpyObj<OverlayPanel>('OverlayPanel', ['show', 'hide']);

    component.showPhotoPreview(new MouseEvent('mouseenter'), preview);

    expect(preview.show).not.toHaveBeenCalled();
  });

  it('loads a protected worker photo through one shared authenticated cache request', () => {
    const get = jasmine.createSpy('get').and.returnValue(of(new Blob(['bmp'], { type: 'image/bmp' })));
    const cache = new WorkerPhotoClientCacheService({ get } as unknown as HttpClient);
    const objectUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:worker-119');
    const workerId = '11111111-1111-1111-1111-111111111119';
    const reference = `/api/workers/${workerId}/photo?v=v1`;
    const first = new WorkerAvatarComponent(cache);
    const second = new WorkerAvatarComponent(cache);
    first.lazy = false;
    second.lazy = false;
    first.photoReference = reference;
    second.photoReference = reference;
    first.ngOnChanges({ photoReference: {} as any });
    second.ngOnChanges({ photoReference: {} as any });

    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith(buildApiUrl(reference), jasmine.objectContaining({ responseType: 'blob' }));
    expect(objectUrl).toHaveBeenCalledTimes(2);
    expect(first.safeImageUrl).toBe('blob:worker-119');
  });

  it('starts loading when a protected photo is added after the avatar view is initialized', () => {
    const get = jasmine.createSpy('get').and.returnValue(of(new Blob(['bmp'], { type: 'image/bmp' })));
    const cache = new WorkerPhotoClientCacheService({ get } as unknown as HttpClient);
    const component = new WorkerAvatarComponent(cache);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:new-worker-photo');
    component.lazy = false;
    component.hasPhoto = false;
    component.ngAfterViewInit();

    component.hasPhoto = true;
    component.photoReference = '/api/workers/11111111-1111-1111-1111-111111111119/photo?v=v2';
    component.ngOnChanges({ hasPhoto: {} as any, photoReference: {} as any });

    expect(get).toHaveBeenCalledTimes(1);
    expect(component.safeImageUrl).toBe('blob:new-worker-photo');
  });

  it('caches a failed protected request for the current lifecycle without a retry storm', () => {
    const get = jasmine.createSpy('get').and.returnValue(throwError(() => new Error('429')));
    const cache = new WorkerPhotoClientCacheService({ get } as unknown as HttpClient);
    const component = new WorkerAvatarComponent(cache);
    component.lazy = false;
    component.photoReference = '/api/workers/11111111-1111-1111-1111-111111111119/photo';
    component.ngOnChanges({ photoReference: {} as any });
    component.ngOnChanges({ photoVersion: {} as any });

    expect(component.safeImageUrl).toBe('');
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('limits a normal ten-row page to the actual workers with photos', () => {
    const get = jasmine.createSpy('get').and.returnValue(of(new Blob(['bmp'], { type: 'image/bmp' })));
    const cache = new WorkerPhotoClientCacheService({ get } as unknown as HttpClient);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:worker');

    Array.from({ length: 10 }, (_, index) => {
      const component = new WorkerAvatarComponent(cache);
      component.lazy = false;
      component.hasPhoto = index < 4;
      component.photoReference = `/api/workers/11111111-1111-1111-1111-${String(index + 1).padStart(12, '0')}/photo?v=v1`;
      component.ngOnChanges({ photoReference: {} as any, hasPhoto: {} as any });
    });

    expect(get).toHaveBeenCalledTimes(4);
  });
});

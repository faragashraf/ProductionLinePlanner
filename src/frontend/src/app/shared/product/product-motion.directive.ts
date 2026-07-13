import { Directive, HostBinding, Input } from '@angular/core';

export type PlpMotionKind = 'fade' | 'none';

/** Applies only approved product motion classes; pages do not define local motion. */
@Directive({
  selector: '[plpMotion]',
  standalone: true
})
export class PlpMotionDirective {
  @Input('plpMotion') motion: PlpMotionKind = 'fade';

  @HostBinding('class.plp-motion-fade')
  get usesFadeMotion(): boolean {
    return this.motion === 'fade';
  }
}

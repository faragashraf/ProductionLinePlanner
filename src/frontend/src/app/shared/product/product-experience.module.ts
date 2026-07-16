import { NgModule } from '@angular/core';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PlpProductEmptyStateComponent } from './plp-empty-state.component';
import { PlpProductErrorStateComponent } from './plp-error-state.component';
import { PlpFormFieldComponent } from './plp-form-field.component';
import { PlpFormComponent } from './plp-form.component';
import { PlpProductLoadingStateComponent } from './plp-loading-state.component';
import { PlpMotionDirective } from './product-motion.directive';
import { PlpBrandLogoComponent } from './plp-brand-logo.component';

/**
 * Compatibility import for the eagerly loaded application composition.
 *
 * Keep this surface limited to shell and login primitives. Feature-only table,
 * overflow, pagination, and expandable-form primitives are standalone and
 * imported directly by their lazy modules so they cannot leak PrimeNG's table
 * graph into the initial bundle.
 */
@NgModule({
  imports: [
    InputTextModule,
    MessageModule,
    PlpActionButtonComponent,
    PlpProductEmptyStateComponent,
    PlpProductErrorStateComponent,
    PlpFormFieldComponent,
    PlpFormComponent,
    PlpProductLoadingStateComponent,
    PlpMotionDirective,
    PlpBrandLogoComponent
  ],
  exports: [
    InputTextModule,
    MessageModule,
    PlpActionButtonComponent,
    PlpProductEmptyStateComponent,
    PlpProductErrorStateComponent,
    PlpFormFieldComponent,
    PlpFormComponent,
    PlpProductLoadingStateComponent,
    PlpMotionDirective,
    PlpBrandLogoComponent
  ]
})
export class ProductExperienceModule {}

import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';

import { AppModule } from './app/app.module';
import { initializeLocalDevelopmentRuntime } from './app/core/runtime/local-development-runtime';
import { environment } from './environments/environment';

if (!environment.production) {
  initializeLocalDevelopmentRuntime(
    window.location,
    environment.apiBaseUrl,
    window as unknown as Record<string, unknown>,
    navigator.serviceWorker
  );
}

platformBrowserDynamic().bootstrapModule(AppModule)
  .catch(err => console.error(err));

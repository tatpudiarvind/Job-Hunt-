import { ApplicationConfig, ErrorHandler, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { providePrimeNG } from 'primeng/config';
import { MessageService } from 'primeng/api';
import { DialogService } from 'primeng/dynamicdialog';
import { routes } from './app.routes';
import { JobHunterPreset } from './core/primeng-preset';
import { authInterceptor } from './core/auth.interceptor';
import { GlobalErrorHandler } from './core/global-error-handler';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    providePrimeNG({
      ripple: false,
      theme: {
        preset: JobHunterPreset,
        options: { darkModeSelector: false, cssLayer: { name: 'primeng', order: 'primeng' } }
      }
    }),
    MessageService,
    DialogService,
    { provide: ErrorHandler, useClass: GlobalErrorHandler }
  ]
};

import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection
} from '@angular/core';
import { MatIconRegistry } from '@angular/material/icon';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { firstValueFrom } from 'rxjs';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LanguageService } from './core/language.service';
import { TranslatedPaginatorIntl } from './core/paginator-intl';
import { ThemeService } from './core/theme.service';
import { TranslocoHttpLoader } from './core/transloco-loader';

registerLocaleData(localePt, 'pt-BR');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideTransloco({
      config: {
        availableLangs: ['pt-BR', 'en'],
        defaultLang: 'pt-BR',
        fallbackLang: 'pt-BR',
        reRenderOnLangChange: true,
        prodMode: !isDevMode()
      },
      loader: TranslocoHttpLoader
    }),
    { provide: MatPaginatorIntl, useClass: TranslatedPaginatorIntl },
    provideAppInitializer(async () => {
      inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
      inject(ThemeService);
      const language = inject(LanguageService);
      const auth = inject(AuthService);
      // Restaura a sessão (cookie de refresh) antes do roteamento inicial; ela pode trocar o idioma.
      await firstValueFrom(auth.restoreSession());
      // Traduções carregadas antes da primeira renderização: componentes como mat-select
      // guardam o rótulo inicial e não o atualizariam depois.
      await language.ready();
    })
  ]
};

import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { filter, map } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/language.service';
import { LearningService } from '../../core/learning.service';
import { Language, Role } from '../../core/models';
import { ThemeService } from '../../core/theme.service';
import { ChangePasswordDialog } from '../../pages/account/change-password-dialog';
import { NotificationBell } from '../notification-bell/notification-bell';

interface NavItem {
  labelKey: string;
  icon: string;
  link: string[];
  roles?: Role[];
}

const COLLAPSE_KEY = 'k2.sidebar.collapsed';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule, TranslocoPipe, NotificationBell],
  templateUrl: './shell.html',
  styleUrl: './shell.scss'
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly language = inject(LanguageService);
  protected readonly learning = inject(LearningService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected readonly isMobile = toSignal(
    inject(BreakpointObserver).observe('(max-width: 900px)').pipe(map((s) => s.matches)),
    { initialValue: false }
  );
  protected readonly collapsed = signal(this.readCollapsed());
  protected readonly mobileOpen = signal(false);

  protected readonly languages: Language[] = ['pt-BR', 'en'];

  protected readonly initials = computed(() =>
    (this.auth.user()?.name ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0]!.toUpperCase())
      .join('')
  );

  /** Apenas módulos já implementados aparecem no menu. */
  protected readonly navItems = computed<NavItem[]>(() => {
    const user = this.auth.user();
    if (!user) return [];
    const items: NavItem[] = [
      { labelKey: 'nav.dashboard', icon: 'space_dashboard', link: ['/dashboard'] },
      user.role === 'SuperAdmin' || !user.companyId
        ? { labelKey: 'nav.companies', icon: 'domain', link: ['/companies'] }
        : { labelKey: 'nav.myCompany', icon: 'domain', link: ['/companies', user.companyId] },
      { labelKey: 'nav.conversations', icon: 'forum', link: ['/conversations'] },
      { labelKey: 'nav.contacts', icon: 'contacts', link: ['/contacts'] },
      { labelKey: 'nav.appointments', icon: 'event', link: ['/appointments'] },
      { labelKey: 'nav.agents', icon: 'smart_toy', link: ['/agents'] },
      { labelKey: 'nav.knowledge', icon: 'menu_book', link: ['/knowledge'] },
      { labelKey: 'nav.playground', icon: 'science', link: ['/playground'], roles: ['SuperAdmin', 'CompanyAdmin', 'AgentManager'] },
      { labelKey: 'nav.channels', icon: 'hub', link: ['/channels'] },
      { labelKey: 'nav.automations', icon: 'bolt', link: ['/automations'] },
      { labelKey: 'nav.costs', icon: 'payments', link: ['/costs'], roles: ['SuperAdmin', 'CompanyAdmin'] },
      { labelKey: 'nav.users', icon: 'group', link: ['/users'], roles: ['SuperAdmin', 'CompanyAdmin'] },
      { labelKey: 'nav.system', icon: 'monitor_heart', link: ['/system'] }
    ];
    // A central de aprendizado só aparece no menu com o Modo Iniciante ligado (a rota continua acessível).
    if (this.learning.enabled()) items.push({ labelKey: 'nav.learn', icon: 'school', link: ['/learn'] });
    return items.filter((i) => !i.roles || i.roles.includes(user.role));
  });

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.mobileOpen.set(false));
  }

  toggleSidebar(): void {
    if (this.isMobile()) {
      this.mobileOpen.update((v) => !v);
      return;
    }
    this.collapsed.update((v) => !v);
    try {
      localStorage.setItem(COLLAPSE_KEY, String(this.collapsed()));
    } catch {
      /* ignora */
    }
  }

  setLanguage(lang: Language): void {
    this.language.use(lang);
    this.auth.updateLanguage(lang).subscribe({ error: () => undefined });
  }

  openChangePassword(): void {
    this.dialog.open(ChangePasswordDialog, { width: '440px' });
  }

  private readCollapsed(): boolean {
    try {
      return localStorage.getItem(COLLAPSE_KEY) === 'true';
    } catch {
      return false;
    }
  }
}

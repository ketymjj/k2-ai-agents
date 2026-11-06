import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NotificationsApi } from '../../core/api/notifications.api';
import { AppNotification, NotificationType } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';

const ICONS: Record<NotificationType, string> = {
  Info: 'info',
  NewLead: 'person_add',
  Handoff: 'support_agent',
  Appointment: 'event',
  System: 'settings'
};

/** Sino do cabeçalho: lista as notificações e recebe novas em tempo real. */
@Component({
  selector: 'app-notification-bell',
  imports: [DatePipe, MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslocoPipe],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.scss'
})
export class NotificationBell implements OnInit, OnDestroy {
  private readonly api = inject(NotificationsApi);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly icons = ICONS;
  protected readonly items = signal<AppNotification[]>([]);
  protected readonly unread = signal(0);

  ngOnInit(): void {
    this.realtime.acquire();
    this.load();
    this.realtime.notification$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((n) => {
      this.items.update((list) => [n, ...list.filter((x) => x.id !== n.id)].slice(0, 20));
      this.unread.update((u) => u + 1);
    });
  }

  ngOnDestroy(): void {
    this.realtime.release();
  }

  load(): void {
    this.api.list().subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.unread.set(r.unread);
      },
      error: () => undefined
    });
  }

  open(n: AppNotification): void {
    if (!n.isRead) {
      this.api.markRead(n.id).subscribe({ error: () => undefined });
      this.items.update((list) => list.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
      this.unread.update((u) => Math.max(0, u - 1));
    }
    if (n.link) this.router.navigateByUrl(n.link);
  }

  markAll(event: Event): void {
    event.stopPropagation();
    this.api.markAllRead().subscribe({
      next: () => {
        this.items.update((list) => list.map((x) => ({ ...x, isRead: true })));
        this.unread.set(0);
      },
      error: () => undefined
    });
  }
}

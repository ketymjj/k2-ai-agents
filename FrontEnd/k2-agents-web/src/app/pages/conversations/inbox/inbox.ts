import { DatePipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnDestroy, OnInit, afterRenderEffect, computed, inject, input, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { Observable, debounceTime, distinctUntilChanged } from 'rxjs';
import { ContactsApi } from '../../../core/api/contacts.api';
import { ConversationsApi } from '../../../core/api/conversations.api';
import { AuthService } from '../../../core/auth/auth.service';
import { errorMessage } from '../../../core/http-error';
import { ChatMessage, ConversationContact, ConversationEvent, ConversationStatus, ConversationSummary } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { RealtimeService } from '../../../core/realtime.service';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

type StatusFilter = ConversationStatus | '' | 'mine';

@Component({
  selector: 'app-inbox',
  imports: [ModuleGuide, 
    DatePipe, RouterLink, FormsModule, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatProgressBarModule, MatTooltipModule, TranslocoPipe
  ],
  templateUrl: './inbox.html',
  styleUrl: './inbox.scss'
})
export class Inbox implements OnInit, OnDestroy {
  private readonly api = inject(ConversationsApi);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly realtime = inject(RealtimeService);
  protected readonly auth = inject(AuthService);
  protected readonly formatPhone = ContactsApi.formatPhone;

  /** Query param: conversa selecionada. */
  readonly id = input<string>();

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly statusFilters: StatusFilter[] = ['', 'WaitingHuman', 'mine', 'Open', 'HumanActive', 'Closed'];
  protected readonly status = signal<StatusFilter>('');
  protected readonly search = new FormControl('', { nonNullable: true });

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly selected = signal<ConversationSummary | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly contact = signal<ConversationContact | null>(null);
  protected readonly events = signal<ConversationEvent[]>([]);
  protected readonly loadingDetail = signal(false);
  protected readonly acting = signal(false);
  protected readonly showSide = signal(true);

  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly identifying = signal(false);
  protected readonly identifyError = signal<string | null>(null);
  protected readonly identifyForm = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.email, Validators.maxLength(160)]],
    phone: ['', Validators.maxLength(32)]
  });

  protected readonly canAttend = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager', 'Attendant'));
  protected readonly isMine = computed(() => this.selected()?.assignedUserId === this.auth.user()?.id);

  constructor() {
    afterRenderEffect(() => {
      this.messages();
      const el = this.scroller()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  ngOnInit(): void {
    this.realtime.acquire();
    this.realtime.messageCreated$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((e) => {
      if (e.conversationId !== this.selected()?.id) return;
      this.messages.update((list) => (list.some((m) => m.id === e.message.id) ? list : [...list, e.message]));
    });
    this.realtime.conversationUpdated$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((c) => {
      const statusChanged = this.selected()?.id === c.id && this.selected()?.status !== c.status;
      this.upsert(c);
      // Contato pode ter sido capturado na conversa aberta; status pode ter mudado por outro atendente.
      if (this.selected()?.id === c.id && (statusChanged || c.contactId !== this.contact()?.id)) this.refreshSide(c.id);
    });

    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload());
    this.reload();
    const initial = this.id();
    if (initial) this.open(initial);
  }

  ngOnDestroy(): void {
    this.realtime.release();
  }

  setStatus(status: StatusFilter): void {
    this.status.set(status);
    this.reload();
  }

  reload(): void {
    const s = this.status();
    this.loading.set(true);
    this.api
      .list({ status: s === 'mine' ? '' : s, mine: s === 'mine' ? true : undefined, search: this.search.value, pageSize: 50 })
      .subscribe({
        next: (r) => {
          this.conversations.set(r.items);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.notify.error(err);
        }
      });
  }

  open(id: string): void {
    this.router.navigate([], { queryParams: { id }, replaceUrl: true });
    this.loadingDetail.set(true);
    this.draft.set('');
    this.api.get(id).subscribe({
      next: (detail) => {
        this.selected.set(detail.summary);
        this.messages.set(detail.messages);
        this.contact.set(detail.contact);
        this.loadingDetail.set(false);
        this.loadEvents(id);
      },
      error: (err) => {
        this.loadingDetail.set(false);
        this.notify.error(err);
      }
    });
  }

  back(): void {
    this.selected.set(null);
    this.messages.set([]);
    this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  // ---------- Ações do atendimento ----------

  close(): void {
    this.act(this.api.close(this.selected()!.id), 'conversations.closed');
  }

  reopen(): void {
    this.act(this.api.reopen(this.selected()!.id), 'conversations.reopened');
  }

  transfer(): void {
    this.act(this.api.transfer(this.selected()!.id), 'conversations.transferred');
  }

  claim(): void {
    this.act(this.api.claim(this.selected()!.id), 'conversations.claimed');
  }

  returnToAi(): void {
    this.act(this.api.returnToAi(this.selected()!.id), 'conversations.returned');
  }

  send(): void {
    const c = this.selected();
    const text = this.draft().trim();
    if (!c || !text || this.sending()) return;
    this.sending.set(true);
    this.api.reply(c.id, text).subscribe({
      next: (message) => {
        this.draft.set('');
        this.sending.set(false);
        this.messages.update((list) => (list.some((m) => m.id === message.id) ? list : [...list, message]));
        this.loadEvents(c.id);
      },
      error: (err) => {
        this.sending.set(false);
        this.notify.error(err);
      }
    });
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.send();
    }
  }

  identify(): void {
    const c = this.selected();
    if (!c || this.identifyForm.invalid) {
      this.identifyForm.markAllAsTouched();
      return;
    }
    const v = this.identifyForm.getRawValue();
    this.identifying.set(true);
    this.identifyError.set(null);
    this.api.linkContact(c.id, { name: v.name, email: v.email || null, phone: v.phone || null }).subscribe({
      next: (detail) => {
        this.identifying.set(false);
        this.contact.set(detail.contact);
        this.upsert(detail.summary);
        this.identifyForm.reset();
        this.notify.success('conversations.contactLinked');
      },
      error: (err) => {
        this.identifying.set(false);
        this.identifyError.set(errorMessage(err));
      }
    });
  }

  private act(request: Observable<ConversationSummary>, successKey: string): void {
    this.acting.set(true);
    request.subscribe({
      next: (summary) => {
        this.acting.set(false);
        this.upsert(summary);
        this.loadEvents(summary.id);
        this.notify.success(successKey);
      },
      error: (err) => {
        this.acting.set(false);
        this.notify.error(err);
      }
    });
  }

  private refreshSide(id: string): void {
    this.api.get(id).subscribe({ next: (d) => this.contact.set(d.contact), error: () => undefined });
    this.loadEvents(id);
  }

  private loadEvents(id: string): void {
    this.api.events(id).subscribe({ next: (e) => this.events.set(e), error: () => this.events.set([]) });
  }

  /** Atualiza (ou insere no topo) a conversa recebida em tempo real, respeitando o filtro atual. */
  private upsert(c: ConversationSummary): void {
    if (this.selected()?.id === c.id) this.selected.set(c);
    const s = this.status();
    const matches = !s || (s === 'mine' ? c.assignedUserId === this.auth.user()?.id : s === c.status);
    this.conversations.update((list) => {
      const rest = list.filter((x) => x.id !== c.id);
      if (!matches) return rest;
      if (this.search.value && !list.some((x) => x.id === c.id)) return list;
      return [c, ...rest].sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt));
    });
  }

  initials(name: string): string {
    return name.split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');
  }
}

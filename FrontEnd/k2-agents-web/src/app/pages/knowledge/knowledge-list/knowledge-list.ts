import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, filter, forkJoin, merge, of } from 'rxjs';
import { AgentsApi } from '../../../core/api/agents.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { KnowledgeApi } from '../../../core/api/knowledge.api';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/language.service';
import {
  AGENT_MANAGER_ROLES,
  AgentListItem,
  Company,
  KNOWLEDGE_TYPES,
  KnowledgeDocument,
  KnowledgeListItem,
  KnowledgeSearchHit,
  KnowledgeSourceType,
  KnowledgeStatus
} from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { KnowledgeDialog, KnowledgeDialogData } from '../knowledge-dialog/knowledge-dialog';
import { KnowledgeScopeData, formatBytes } from '../knowledge-scope';
import { UploadDialog } from '../upload-dialog/upload-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

export const KNOWLEDGE_ICONS: Record<KnowledgeSourceType, string> = {
  Text: 'article',
  Faq: 'quiz',
  Pdf: 'picture_as_pdf',
  Txt: 'text_snippet',
  Docx: 'description'
};

/** Valor especial do filtro de agente: documentos gerais (sem agente específico). */
const GENERAL = '__general__';

@Component({
  selector: 'app-knowledge-list',
  imports: [ModuleGuide, 
    DatePipe, DecimalPipe, FormsModule, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatMenuModule, MatPaginatorModule, MatProgressBarModule, MatTooltipModule, TranslocoPipe
  ],
  templateUrl: './knowledge-list.html',
  styleUrl: './knowledge-list.scss'
})
export class KnowledgeList implements OnInit {
  private readonly api = inject(KnowledgeApi);
  private readonly agentsApi = inject(AgentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  protected readonly locale = inject(LanguageService).locale;

  /** Query param opcional vindo do detalhe da empresa. */
  readonly companyId = input<string>();

  protected readonly types = KNOWLEDGE_TYPES;
  protected readonly icons = KNOWLEDGE_ICONS;
  protected readonly general = GENERAL;
  protected readonly formatBytes = formatBytes;
  protected readonly canManage = computed(() => this.auth.hasRole(...AGENT_MANAGER_ROLES));

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly agent = new FormControl('', { nonNullable: true });
  protected readonly type = new FormControl<KnowledgeSourceType | ''>('', { nonNullable: true });
  protected readonly status = new FormControl<KnowledgeStatus | ''>('', { nonNullable: true });
  protected readonly company = new FormControl('', { nonNullable: true });

  protected readonly docs = signal<KnowledgeListItem[]>([]);
  protected readonly agents = signal<AgentListItem[]>([]);
  protected readonly companies = signal<Company[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);

  // Testar busca
  protected readonly testQuery = signal('');
  protected readonly testAgent = signal('');
  protected readonly testing = signal(false);
  protected readonly hits = signal<KnowledgeSearchHit[] | null>(null);

  protected readonly filterAgents = computed(() => {
    const company = this.selectedCompany();
    return company ? this.agents().filter((a) => a.companyId === company) : this.agents();
  });
  private readonly selectedCompany = signal('');

  ngOnInit(): void {
    this.company.setValue(this.companyId() ?? '', { emitEvent: false });
    this.selectedCompany.set(this.company.value);
    forkJoin({
      agents: this.agentsApi.list({ pageSize: 100 }),
      companies: this.auth.isSuperAdmin() ? this.companiesApi.list({ pageSize: 100 }) : of(null)
    }).subscribe({
      next: ({ agents, companies }) => {
        this.agents.set(agents.items);
        this.companies.set(companies?.items ?? []);
      },
      error: (e) => this.notify.error(e)
    });

    this.company.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((c) => {
      this.selectedCompany.set(c);
      this.agent.setValue('', { emitEvent: false });
      this.testAgent.set('');
    });
    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.agent.valueChanges,
      this.type.valueChanges,
      this.status.valueChanges,
      this.company.valueChanges,
      of(null)
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => this.reload(value !== null));
  }

  reload(resetPage = false): void {
    if (resetPage) this.page.set(0);
    this.loading.set(true);
    const agent = this.agent.value;
    this.api
      .list({
        search: this.search.value,
        sourceType: this.type.value,
        status: this.status.value,
        companyId: this.company.value || undefined,
        agentId: agent && agent !== GENERAL ? agent : undefined,
        generalOnly: agent === GENERAL ? true : undefined,
        page: this.page() + 1,
        pageSize: this.pageSize()
      })
      .subscribe({
        next: (r) => {
          this.docs.set(r.items);
          this.total.set(r.total);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.notify.error(err);
        }
      });
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload();
  }

  private scope(): KnowledgeScopeData {
    return {
      agents: this.agents(),
      companies: this.companies().filter((c) => c.status === 'Active'),
      isSuperAdmin: this.auth.isSuperAdmin(),
      defaultCompanyId: this.company.value || null
    };
  }

  create(mode: 'Text' | 'Faq'): void {
    this.openEditor({ ...this.scope(), mode, document: null, readOnly: false }, 'knowledge.created');
  }

  open(item: KnowledgeListItem): void {
    this.api.get(item.id).subscribe({
      next: (document) =>
        this.openEditor(
          { ...this.scope(), mode: document.sourceType === 'Faq' ? 'Faq' : 'Text', document, readOnly: !this.canManage() },
          'knowledge.updated'
        ),
      error: (err) => this.notify.error(err)
    });
  }

  private openEditor(data: KnowledgeDialogData, successKey: string): void {
    this.dialog
      .open<KnowledgeDialog, KnowledgeDialogData, KnowledgeDocument>(KnowledgeDialog, { data, width: '760px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success(successKey);
        this.reload();
      });
  }

  upload(): void {
    this.dialog
      .open<UploadDialog, KnowledgeScopeData, KnowledgeDocument>(UploadDialog, { data: this.scope(), width: '560px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((doc) => {
        this.notify.success('knowledge.uploaded', { title: doc.title, chunks: doc.chunkCount });
        this.reload();
      });
  }

  toggleArchive(item: KnowledgeListItem): void {
    const archiving = item.status !== 'Archived';
    this.api.setStatus(item.id, archiving ? 'Archived' : 'Ready').subscribe({
      next: () => {
        this.notify.success(archiving ? 'knowledge.archived' : 'knowledge.restored');
        this.reload();
      },
      error: (err) => this.notify.error(err)
    });
  }

  remove(item: KnowledgeListItem): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('knowledge.deleteTitle'),
      message: this.transloco.translate('knowledge.deleteMessage', { title: item.title }),
      confirmLabel: this.transloco.translate('knowledge.delete'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.delete(item.id).subscribe({
          next: () => {
            this.notify.success('knowledge.deleted');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }

  download(item: KnowledgeListItem): void {
    this.api.download(item.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = item.originalFileName ?? item.title;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.notify.error(err)
    });
  }

  runSearch(): void {
    const query = this.testQuery().trim();
    if (!query) return;
    this.testing.set(true);
    this.api.search(query, this.testAgent() || null, this.company.value || null).subscribe({
      next: (hits) => {
        this.hits.set(hits);
        this.testing.set(false);
      },
      error: (err) => {
        this.testing.set(false);
        this.notify.error(err);
      }
    });
  }

  isFile(item: KnowledgeListItem): boolean {
    return item.sourceType === 'Pdf' || item.sourceType === 'Txt' || item.sourceType === 'Docx';
  }
}

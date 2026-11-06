import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSliderModule } from '@angular/material/slider';
import { StepperSelectionEvent } from '@angular/cdk/stepper';
import { MatStepperModule } from '@angular/material/stepper';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { BreakpointObserver } from '@angular/cdk/layout';
import { filter, forkJoin, map, of, startWith } from 'rxjs';
import { AgentsApi } from '../../../core/api/agents.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { errorMessage } from '../../../core/http-error';
import { LanguageService } from '../../../core/language.service';
import {
  AGENT_MANAGER_ROLES,
  AIModelOption,
  Agent,
  AgentBehavior,
  AgentTemplate,
  BUSINESS_TYPES,
  BusinessType,
  Company,
  Language,
  SaveAgent
} from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { AgentActions } from '../agent-actions.service';
import { BUSINESS_TYPE_ICONS } from '../agent-ui';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';
import { Term } from '../../../shared/term/term';

type FaqGroup = FormGroup<{ question: FormControl<string>; answer: FormControl<string> }>;

const REVIEW_STEP = 4;

@Component({
  selector: 'app-agent-editor',
  imports: [ModuleGuide, Term, 
    DatePipe, DecimalPipe, RouterLink, ReactiveFormsModule, MatStepperModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatSliderModule, MatProgressBarModule, MatTooltipModule, TranslocoPipe
  ],
  templateUrl: './agent-editor.html',
  styleUrl: './agent-editor.scss'
})
export class AgentEditor implements OnInit {
  private readonly api = inject(AgentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly actions = inject(AgentActions);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly auth = inject(AuthService);
  protected readonly locale = inject(LanguageService).locale;

  /** Parâmetro de rota (ausente em /agents/new). */
  readonly id = input<string>();
  /** ?type=Scheduling — vem dos exemplos da central de aprendizado. */
  readonly type = input<string>();
  /** Query param opcional em /agents/new. */
  readonly companyId = input<string>();

  protected readonly businessTypes = BUSINESS_TYPES;
  protected readonly icons = BUSINESS_TYPE_ICONS;
  protected readonly languages: Language[] = ['pt-BR', 'en'];

  protected readonly agent = signal<Agent | null>(null);
  protected readonly models = signal<AIModelOption[]>([]);
  protected readonly companies = signal<Company[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly generating = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly platformRules = signal<string>('');
  /** Prompt alterado à mão: não sobrescrever automaticamente. */
  protected readonly promptEdited = signal(false);

  protected readonly isMobile = toSignal(
    inject(BreakpointObserver).observe('(max-width: 760px)').pipe(map((s) => s.matches)),
    { initialValue: false }
  );
  protected readonly isNew = computed(() => !this.id());
  protected readonly canManage = computed(() => this.auth.hasRole(...AGENT_MANAGER_ROLES));

  protected readonly basics = this.fb.group({
    companyId: [''],
    name: ['', [Validators.required, Validators.maxLength(80)]],
    description: ['', Validators.maxLength(500)],
    businessType: ['Sales' as BusinessType, Validators.required],
    responseLanguage: ['pt-BR' as Language, Validators.required]
  });

  protected readonly behavior = this.fb.group({
    companyDisplayName: ['', Validators.maxLength(160)],
    objective: ['', [Validators.required, Validators.maxLength(1000)]],
    personality: ['', Validators.maxLength(300)],
    tone: ['', Validators.maxLength(200)],
    businessHours: ['', Validators.maxLength(500)]
  });

  protected readonly rules = this.fb.group({
    faqs: this.fb.array<FaqGroup>([]),
    restrictions: this.fb.array<FormControl<string>>([]),
    handoffRules: this.fb.array<FormControl<string>>([])
  });

  protected readonly modelSettings = this.fb.group({
    model: ['', Validators.required],
    temperature: [0.4, [Validators.min(0), Validators.max(1)]],
    maxOutputTokens: [600, [Validators.required, Validators.min(50), Validators.max(4000)]],
    welcomeMessage: ['', Validators.maxLength(1000)]
  });

  protected readonly review = this.fb.group({
    systemPrompt: ['', [Validators.required, Validators.maxLength(20000)]],
    changeNote: ['', Validators.maxLength(300)]
  });

  private readonly selectedModelId = toSignal(
    this.modelSettings.controls.model.valueChanges.pipe(startWith(this.modelSettings.controls.model.value))
  );
  protected readonly selectedModel = computed(() => this.models().find((m) => m.id === this.selectedModelId()) ?? null);
  protected readonly temperature = toSignal(
    this.modelSettings.controls.temperature.valueChanges.pipe(startWith(this.modelSettings.controls.temperature.value)),
    { initialValue: 0.4 }
  );

  get faqs(): FormArray<FaqGroup> {
    return this.rules.controls.faqs;
  }
  get restrictions(): FormArray<FormControl<string>> {
    return this.rules.controls.restrictions;
  }
  get handoffRules(): FormArray<FormControl<string>> {
    return this.rules.controls.handoffRules;
  }

  ngOnInit(): void {
    const id = this.id();
    forkJoin({
      models: this.api.models(),
      agent: id ? this.api.get(id) : of(null),
      companies: this.auth.isSuperAdmin() && !id ? this.companiesApi.list({ status: 'Active', pageSize: 100 }) : of(null)
    }).subscribe({
      next: ({ models, agent, companies }) => {
        this.models.set(models);
        this.companies.set(companies?.items ?? []);
        if (agent) this.loadAgent(agent);
        else this.initNew(models);
        if (!this.canManage()) this.disableAll();
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
        this.router.navigate(['/agents']);
      }
    });
  }

  // ---------- Inicialização ----------

  private initNew(models: AIModelOption[]): void {
    this.basics.controls.companyId.setValue(this.companyId() ?? '');
    if (this.auth.isSuperAdmin()) this.basics.controls.companyId.addValidators(Validators.required);
    this.modelSettings.controls.model.setValue(models.find((m) => m.isDefault)?.id ?? models[0]?.id ?? '');
    const type = this.type();
    if (type && (BUSINESS_TYPES as string[]).includes(type)) this.basics.controls.businessType.setValue(type as BusinessType);
    this.applyTemplate(false);
  }

  private loadAgent(agent: Agent): void {
    this.agent.set(agent);
    this.basics.patchValue({
      companyId: agent.companyId,
      name: agent.name,
      description: agent.description ?? '',
      businessType: agent.businessType,
      responseLanguage: agent.responseLanguage
    });
    this.patchBehavior(agent.behavior);
    this.modelSettings.patchValue({
      model: agent.model,
      temperature: agent.temperature,
      maxOutputTokens: agent.maxOutputTokens,
      welcomeMessage: agent.welcomeMessage ?? ''
    });
    this.review.patchValue({ systemPrompt: agent.systemPrompt, changeNote: '' });
    this.promptEdited.set(true);
    this.markAllPristine();
  }

  private patchBehavior(b: AgentBehavior): void {
    this.behavior.patchValue({
      companyDisplayName: b.companyDisplayName ?? '',
      objective: b.objective ?? '',
      personality: b.personality ?? '',
      tone: b.tone ?? '',
      businessHours: b.businessHours ?? ''
    });
    this.faqs.clear();
    for (const f of b.faqs ?? []) this.addFaq(f.question, f.answer);
    this.restrictions.clear();
    for (const r of b.restrictions ?? []) this.addRule(this.restrictions, r);
    this.handoffRules.clear();
    for (const r of b.handoffRules ?? []) this.addRule(this.handoffRules, r);
  }

  // ---------- Modelos prontos ----------

  selectType(type: BusinessType): void {
    if (!this.canManage()) return;
    this.basics.controls.businessType.setValue(type);
    // Em agentes novos, troca o modelo pronto enquanto o usuário ainda não personalizou o comportamento.
    if (this.isNew() && !this.behavior.dirty && !this.rules.dirty) this.applyTemplate(false);
  }

  /** Preenche comportamento, regras e boas-vindas a partir do modelo pronto do tipo/idioma escolhidos. */
  applyTemplate(confirm = true): void {
    const apply = () => {
      const { businessType, responseLanguage } = this.basics.getRawValue();
      this.api.templates(responseLanguage).subscribe({
        next: (templates: AgentTemplate[]) => {
          const t = templates.find((x) => x.businessType === businessType);
          if (!t) return;
          this.patchBehavior(t.behavior);
          if (!this.basics.controls.description.value || !confirm) this.basics.controls.description.setValue(t.description);
          this.modelSettings.controls.welcomeMessage.setValue(t.welcomeMessage);
          this.behavior.markAsPristine();
          this.rules.markAsPristine();
          if (!this.promptEdited()) this.review.controls.systemPrompt.setValue('');
        },
        error: (err) => this.notify.error(err)
      });
    };
    if (!confirm) return apply();
    confirmAction(this.dialog, {
      title: this.transloco.translate('agents.applyTemplateTitle'),
      message: this.transloco.translate('agents.applyTemplateMessage')
    })
      .pipe(filter(Boolean))
      .subscribe(apply);
  }

  // ---------- Listas dinâmicas ----------

  addFaq(question = '', answer = ''): void {
    this.faqs.push(
      this.fb.group({
        question: [question, [Validators.required, Validators.maxLength(300)]],
        answer: [answer, [Validators.required, Validators.maxLength(2000)]]
      })
    );
    this.rules.markAsDirty();
  }

  addRule(list: FormArray<FormControl<string>>, value = ''): void {
    list.push(this.fb.control(value, [Validators.required, Validators.maxLength(300)]));
    this.rules.markAsDirty();
  }

  remove(list: FormArray, index: number): void {
    list.removeAt(index);
    this.rules.markAsDirty();
  }

  // ---------- Prompt ----------

  onStepChange(event: StepperSelectionEvent): void {
    if (event.selectedIndex === REVIEW_STEP && (!this.promptEdited() || !this.review.controls.systemPrompt.value)) {
      this.generatePrompt();
    }
  }

  regeneratePrompt(): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('agents.regenerateTitle'),
      message: this.transloco.translate('agents.regenerateMessage')
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.generatePrompt());
  }

  private generatePrompt(): void {
    if (this.basics.invalid || this.behavior.invalid) {
      this.basics.markAllAsTouched();
      this.behavior.markAllAsTouched();
      return;
    }
    const { name, businessType, responseLanguage, companyId } = this.basics.getRawValue();
    this.generating.set(true);
    this.api
      .previewPrompt({ name, businessType, responseLanguage, behavior: this.buildBehavior(), companyId: companyId || null })
      .subscribe({
        next: (preview) => {
          this.review.controls.systemPrompt.setValue(preview.prompt);
          this.review.controls.systemPrompt.markAsDirty();
          this.platformRules.set(preview.platformRules);
          this.promptEdited.set(false);
          this.generating.set(false);
        },
        error: (err) => {
          this.generating.set(false);
          this.notify.error(err);
        }
      });
  }

  onPromptInput(): void {
    this.promptEdited.set(true);
  }

  // ---------- Salvar ----------

  save(): void {
    const groups = [this.basics, this.behavior, this.rules, this.modelSettings, this.review];
    if (groups.some((g) => g.invalid)) {
      groups.forEach((g) => g.markAllAsTouched());
      this.serverError.set(this.transloco.translate('agents.fixErrors'));
      return;
    }
    const b = this.basics.getRawValue();
    const m = this.modelSettings.getRawValue();
    const r = this.review.getRawValue();
    const body: SaveAgent = {
      companyId: b.companyId || null,
      name: b.name,
      description: b.description || null,
      businessType: b.businessType,
      responseLanguage: b.responseLanguage,
      behavior: this.buildBehavior(),
      systemPrompt: r.systemPrompt || null,
      welcomeMessage: m.welcomeMessage || null,
      model: m.model,
      temperature: Number(m.temperature),
      maxOutputTokens: Number(m.maxOutputTokens),
      changeNote: r.changeNote || null
    };

    this.saving.set(true);
    this.serverError.set(null);
    const current = this.agent();
    const request = current ? this.api.update(current.id, body) : this.api.create(body);
    request.subscribe({
      next: (saved) => {
        this.saving.set(false);
        if (!current) {
          this.notify.success('agents.created');
          this.router.navigate(['/agents', saved.id]);
          return;
        }
        this.notify.success(
          saved.currentVersion > current.currentVersion ? 'agents.savedNewVersion' : 'agents.saved',
          { version: saved.currentVersion }
        );
        this.loadAgent(saved);
      },
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }

  // ---------- Ações do agente existente ----------

  toggleStatus(): void {
    const a = this.agent();
    if (a) this.actions.toggleStatus(a).subscribe((updated) => this.agent.set({ ...a, status: updated.status }));
  }

  duplicate(): void {
    const a = this.agent();
    if (a) this.actions.duplicate(a).subscribe((copy) => this.router.navigate(['/agents', copy.id]));
  }

  versions(): void {
    const a = this.agent();
    if (a) this.actions.versions(a, this.canManage()).subscribe((restored) => this.loadAgent(restored));
  }

  // ---------- Utilitários ----------

  private buildBehavior(): AgentBehavior {
    const v = this.behavior.getRawValue();
    const r = this.rules.getRawValue();
    return {
      companyDisplayName: v.companyDisplayName || null,
      objective: v.objective,
      personality: v.personality || null,
      tone: v.tone || null,
      businessHours: v.businessHours || null,
      faqs: r.faqs.filter((f) => f.question.trim() && f.answer.trim()),
      restrictions: r.restrictions.filter((x) => x.trim()),
      handoffRules: r.handoffRules.filter((x) => x.trim())
    };
  }

  private markAllPristine(): void {
    [this.basics, this.behavior, this.rules, this.modelSettings, this.review].forEach((g) => g.markAsPristine());
  }

  private disableAll(): void {
    [this.basics, this.behavior, this.rules, this.modelSettings, this.review].forEach((g) => g.disable({ emitEvent: false }));
  }

  /** Custo estimado de uma troca típica (≈1.500 tokens de entrada e 300 de saída). */
  estimateCost(model: AIModelOption): number {
    return (1500 * model.inputPricePer1M + 300 * model.outputPricePer1M) / 1_000_000;
  }
}

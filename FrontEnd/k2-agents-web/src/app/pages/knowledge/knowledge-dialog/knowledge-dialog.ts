import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { startWith } from 'rxjs';
import { KnowledgeApi } from '../../../core/api/knowledge.api';
import { errorMessage } from '../../../core/http-error';
import { KnowledgeDocument } from '../../../core/models';
import { KnowledgeScopeData, agentsForCompany, formatBytes } from '../knowledge-scope';

export interface KnowledgeDialogData extends KnowledgeScopeData {
  /** Novo texto/FAQ ou documento existente (de qualquer tipo) para edição. */
  mode: 'Text' | 'Faq';
  document: KnowledgeDocument | null;
  readOnly: boolean;
}

@Component({
  selector: 'app-knowledge-dialog',
  imports: [DecimalPipe, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  templateUrl: './knowledge-dialog.html',
  styleUrl: './knowledge-dialog.scss'
})
export class KnowledgeDialog {
  private readonly api = inject(KnowledgeApi);
  private readonly ref = inject(MatDialogRef<KnowledgeDialog, KnowledgeDocument>);
  protected readonly data = inject<KnowledgeDialogData>(MAT_DIALOG_DATA);

  protected readonly doc = this.data.document;
  protected readonly isFaq = (this.doc?.sourceType ?? this.data.mode) === 'Faq';
  protected readonly isFile = !!this.doc && ['Pdf', 'Txt', 'Docx'].includes(this.doc.sourceType);
  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly formatBytes = formatBytes;

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyId: [this.doc?.companyId ?? this.data.defaultCompanyId ?? ''],
    title: [this.doc?.title ?? '', [Validators.required, Validators.maxLength(this.isFaq ? 300 : 200)]],
    content: [this.doc?.content ?? '', [Validators.required, Validators.maxLength(200_000)]],
    agentId: [this.doc?.agentId ?? '']
  });

  private readonly companyId = toSignal(this.form.controls.companyId.valueChanges.pipe(startWith(this.form.controls.companyId.value)));
  protected readonly agents = agentsForCompany(this.data, this.companyId);
  protected readonly showCompany = this.data.isSuperAdmin && !this.doc;

  constructor() {
    if (this.showCompany) this.form.controls.companyId.addValidators(Validators.required);
    this.form.controls.companyId.valueChanges.subscribe(() => this.form.controls.agentId.setValue(''));
    if (this.data.readOnly) this.form.disable();
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    const v = this.form.getRawValue();
    const request = this.doc
      ? this.api.update(this.doc.id, { title: v.title, content: v.content, agentId: v.agentId || null })
      : this.api.create({
          title: v.title,
          content: v.content,
          sourceType: this.data.mode,
          agentId: v.agentId || null,
          companyId: this.showCompany ? v.companyId : null
        });
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }
}

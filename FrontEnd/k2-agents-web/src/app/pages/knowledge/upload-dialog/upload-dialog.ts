import { HttpEventType } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Subscription, startWith } from 'rxjs';
import { KnowledgeApi } from '../../../core/api/knowledge.api';
import { errorMessage } from '../../../core/http-error';
import { KnowledgeDocument } from '../../../core/models';
import { ALLOWED_EXTENSIONS, KnowledgeScopeData, MAX_FILE_BYTES, agentsForCompany, formatBytes } from '../knowledge-scope';

@Component({
  selector: 'app-upload-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatProgressBarModule, TranslocoPipe
  ],
  templateUrl: './upload-dialog.html',
  styleUrl: './upload-dialog.scss'
})
export class UploadDialog {
  private readonly api = inject(KnowledgeApi);
  private readonly transloco = inject(TranslocoService);
  private readonly ref = inject(MatDialogRef<UploadDialog, KnowledgeDocument>);
  protected readonly data = inject<KnowledgeScopeData>(MAT_DIALOG_DATA);

  protected readonly accept = ALLOWED_EXTENSIONS.join(',');
  protected readonly formatBytes = formatBytes;
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly serverError = signal<string | null>(null);
  protected readonly progress = signal<number | null>(null);
  protected readonly processing = signal(false);
  protected readonly dragging = signal(false);
  private upload?: Subscription;

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyId: [this.data.defaultCompanyId ?? ''],
    title: ['', Validators.maxLength(200)],
    agentId: ['']
  });

  private readonly companyId = toSignal(this.form.controls.companyId.valueChanges.pipe(startWith(this.form.controls.companyId.value)));
  protected readonly agents = agentsForCompany(this.data, this.companyId);

  constructor() {
    if (this.data.isSuperAdmin) this.form.controls.companyId.addValidators(Validators.required);
    this.form.controls.companyId.valueChanges.subscribe(() => this.form.controls.agentId.setValue(''));
    this.ref.beforeClosed().subscribe(() => this.upload?.unsubscribe());
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.select(input.files?.[0] ?? null);
    input.value = '';
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.select(event.dataTransfer?.files?.[0] ?? null);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  /** Validação prévia só para feedback rápido; a API valida tipo real, tamanho e conteúdo. */
  private select(file: File | null): void {
    this.serverError.set(null);
    this.fileError.set(null);
    if (!file) return;
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_EXTENSIONS.includes(extension)) {
      this.fileError.set(this.transloco.translate('knowledge.invalidType'));
      return;
    }
    if (file.size > MAX_FILE_BYTES) {
      this.fileError.set(this.transloco.translate('knowledge.tooLarge'));
      return;
    }
    this.file.set(file);
  }

  submit(): void {
    const file = this.file();
    if (!file || this.form.invalid) {
      this.form.markAllAsTouched();
      if (!file) this.fileError.set(this.transloco.translate('knowledge.chooseFile'));
      return;
    }
    const v = this.form.getRawValue();
    this.serverError.set(null);
    this.progress.set(0);
    this.upload = this.api
      .upload({ file, title: v.title || null, agentId: v.agentId || null, companyId: this.data.isSuperAdmin ? v.companyId : null })
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            const percent = Math.round((event.loaded / event.total) * 100);
            this.progress.set(percent);
            // Envio concluído: o servidor agora extrai e indexa o texto.
            if (percent === 100) this.processing.set(true);
          } else if (event.type === HttpEventType.Response && event.body) {
            this.ref.close(event.body);
          }
        },
        error: (err) => {
          this.progress.set(null);
          this.processing.set(false);
          this.serverError.set(errorMessage(err));
        }
      });
  }
}

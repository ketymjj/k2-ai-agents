import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { Observable } from 'rxjs';

export interface ConfirmData {
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, TranslocoPipe],
  templateUrl: './confirm-dialog.html',
  styleUrl: './confirm-dialog.scss'
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

/** Abre a confirmação e emite true quando o usuário confirma. */
export function confirmAction(dialog: MatDialog, data: ConfirmData): Observable<boolean | undefined> {
  return dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data, width: '420px' }).afterClosed();
}

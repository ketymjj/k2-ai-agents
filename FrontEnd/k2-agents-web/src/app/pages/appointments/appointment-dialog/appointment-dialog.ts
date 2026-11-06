import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { AppointmentsApi } from '../../../core/api/appointments.api';
import { ContactsApi } from '../../../core/api/contacts.api';
import { errorMessage } from '../../../core/http-error';
import { LanguageService } from '../../../core/language.service';
import { ACTIVE_APPOINTMENT, Appointment, AppointmentStatus, Contact, SchedulingSettings, Slot } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';

export interface AppointmentDialogData {
  /** Existente = detalhes/ações; nulo = novo agendamento. */
  appointment: Appointment | null;
  settings: SchedulingSettings;
  date: string;
  canEdit: boolean;
  companyId?: string;
}

/** Data local (YYYY-MM-DD) a partir de um Date. */
export function localDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

@Component({
  selector: 'app-appointment-dialog',
  imports: [
    DatePipe, RouterLink, FormsModule, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatAutocompleteModule, MatProgressBarModule, TranslocoPipe
  ],
  templateUrl: './appointment-dialog.html',
  styleUrl: './appointment-dialog.scss'
})
export class AppointmentDialog implements OnInit {
  private readonly api = inject(AppointmentsApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly ref = inject(MatDialogRef<AppointmentDialog, boolean>);
  protected readonly data = inject<AppointmentDialogData>(MAT_DIALOG_DATA);
  protected readonly formatPhone = ContactsApi.formatPhone;
  protected readonly locale = inject(LanguageService).locale;

  protected readonly appointment = signal<Appointment | null>(this.data.appointment);
  protected readonly isNew = !this.data.appointment;
  protected readonly rescheduling = signal(false);

  protected readonly service = signal(this.data.appointment?.serviceName ?? this.data.settings.services[0]?.name ?? '');
  protected readonly date = signal(this.data.date);
  protected readonly slots = signal<Slot[]>([]);
  protected readonly loadingSlots = signal(false);
  protected readonly selectedSlot = signal<string | null>(null);
  protected readonly manualTime = signal('');
  protected readonly notes = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly contactSearch = new FormControl<string | Contact>('', { nonNullable: true });
  protected readonly contactOptions = signal<Contact[]>([]);
  protected readonly contact = signal<Contact | null>(null);

  protected readonly isActive = computed(() => !!this.appointment() && ACTIVE_APPOINTMENT.includes(this.appointment()!.status));
  protected readonly showPicker = computed(() => this.isNew || this.rescheduling());

  ngOnInit(): void {
    if (this.showPicker()) this.loadSlots();
    this.contactSearch.valueChanges
      .pipe(
        filter((v): v is string => typeof v === 'string' && v.trim().length >= 2),
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((term) => this.contactsApi.list({ search: term, pageSize: 8 }))
      )
      .subscribe({ next: (r) => this.contactOptions.set(r.items.filter((c) => !c.isAnonymized)), error: () => undefined });
  }

  loadSlots(): void {
    if (!this.service() || !this.date()) return;
    this.loadingSlots.set(true);
    this.selectedSlot.set(null);
    this.api.availability(this.date(), this.service(), this.appointment()?.id, this.data.companyId).subscribe({
      next: (a) => {
        this.slots.set(a.slots);
        this.loadingSlots.set(false);
      },
      error: (err) => {
        this.slots.set([]);
        this.loadingSlots.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }

  displayContact = (c: Contact | string | null): string => (typeof c === 'string' ? c : c?.name ?? '');

  pickContact(c: Contact): void {
    this.contact.set(c);
  }

  clearContact(): void {
    this.contact.set(null);
    this.contactSearch.setValue('');
  }

  /** Início escolhido: um horário livre da grade, ou horário manual (exceção da equipe, ainda checado contra conflitos). */
  private chosenStart(): string | null {
    if (this.selectedSlot()) return this.selectedSlot();
    if (this.manualTime()) return new Date(`${this.date()}T${this.manualTime()}:00`).toISOString();
    return null;
  }

  save(): void {
    const start = this.chosenStart();
    if (!start) {
      this.error.set(this.transloco.translate('appointments.chooseTime'));
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const current = this.appointment();
    const request = current
      ? this.api.reschedule(current.id, start)
      : this.api.create({
          companyId: this.data.companyId ?? null,
          contactId: this.contact()?.id ?? null,
          serviceName: this.service(),
          startDateTime: start,
          durationMinutes: null,
          notes: this.notes() || null
        });
    request.subscribe({
      next: () => {
        this.notify.success(current ? 'appointments.rescheduled' : 'appointments.created');
        this.ref.close(true);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err));
        if (err?.error?.code === 'slot_unavailable') this.loadSlots();
      }
    });
  }

  setStatus(status: AppointmentStatus): void {
    const a = this.appointment();
    if (!a) return;
    this.saving.set(true);
    this.api.setStatus(a.id, status).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.appointment.set(updated);
        this.notify.success('appointments.statusChanged', { status: this.transloco.translate('appointmentStatus.' + status) });
        this.ref.close(true);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }

  startReschedule(): void {
    this.rescheduling.set(true);
    this.loadSlots();
  }
}

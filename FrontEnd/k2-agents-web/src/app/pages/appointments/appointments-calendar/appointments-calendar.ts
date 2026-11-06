import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { AppointmentsApi } from '../../../core/api/appointments.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/language.service';
import { Appointment, AppointmentStatus, Company, SchedulingSettings } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { AppointmentDialog, AppointmentDialogData, localDate } from '../appointment-dialog/appointment-dialog';
import { ScheduleSettingsData, ScheduleSettingsDialog } from '../schedule-settings-dialog/schedule-settings-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

const STATUSES: AppointmentStatus[] = ['Scheduled', 'Confirmed', 'Completed', 'NoShow', 'Cancelled'];

interface DayColumn {
  date: Date;
  key: string;
  isToday: boolean;
  isPast: boolean;
  items: Appointment[];
}

/** Segunda-feira 00:00 (local) da semana que contém a data. */
function weekStart(d: Date): Date {
  const start = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
  return start;
}

@Component({
  selector: 'app-appointments-calendar',
  imports: [ModuleGuide, DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule, MatTooltipModule, TranslocoPipe],
  templateUrl: './appointments-calendar.html',
  styleUrl: './appointments-calendar.scss'
})
export class AppointmentsCalendar implements OnInit {
  private readonly api = inject(AppointmentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  protected readonly auth = inject(AuthService);
  protected readonly locale = inject(LanguageService).locale;

  protected readonly statuses = STATUSES;
  protected readonly canBook = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager', 'Attendant'));
  protected readonly canConfigure = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager'));

  protected readonly companies = signal<Company[]>([]);
  protected readonly companyId = signal<string>('');
  protected readonly start = signal(weekStart(new Date()));
  protected readonly status = signal<AppointmentStatus | ''>('');
  protected readonly appointments = signal<Appointment[]>([]);
  protected readonly settings = signal<SchedulingSettings | null>(null);
  protected readonly loading = signal(false);

  protected readonly end = computed(() => {
    const e = new Date(this.start());
    e.setDate(e.getDate() + 7);
    return e;
  });
  protected readonly lastDay = computed(() => new Date(this.end().getTime() - 1));
  protected readonly needsCompany = computed(() => this.auth.isSuperAdmin() && !this.companyId());
  protected readonly hasServices = computed(() => (this.settings()?.services.length ?? 0) > 0);

  protected readonly days = computed<DayColumn[]>(() => {
    const today = localDate(new Date());
    const list = this.appointments();
    return Array.from({ length: 7 }, (_, i) => {
      const date = new Date(this.start());
      date.setDate(date.getDate() + i);
      const key = localDate(date);
      return {
        date,
        key,
        isToday: key === today,
        isPast: key < today,
        items: list.filter((a) => localDate(new Date(a.startDateTime)) === key)
      };
    });
  });

  protected readonly counts = computed(() => {
    const list = this.appointments();
    return {
      total: list.filter((a) => a.status !== 'Cancelled').length,
      byAgent: list.filter((a) => a.source === 'Agent' && a.status !== 'Cancelled').length
    };
  });

  ngOnInit(): void {
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ status: 'Active', pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: () => undefined });
      return;
    }
    this.loadSettings();
    this.reload();
  }

  selectCompany(id: string): void {
    this.companyId.set(id);
    this.settings.set(null);
    this.loadSettings();
    this.reload();
  }

  move(weeks: number): void {
    const next = new Date(this.start());
    next.setDate(next.getDate() + weeks * 7);
    this.start.set(next);
    this.reload();
  }

  today(): void {
    this.start.set(weekStart(new Date()));
    this.reload();
  }

  setStatus(status: AppointmentStatus | ''): void {
    this.status.set(status);
    this.reload();
  }

  reload(): void {
    if (this.needsCompany()) return;
    this.loading.set(true);
    this.api
      .list({
        companyId: this.companyId() || undefined,
        from: this.start().toISOString(),
        to: this.end().toISOString(),
        status: this.status()
      })
      .subscribe({
        next: (list) => {
          this.appointments.set(list);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.notify.error(err);
        }
      });
  }

  open(appointment: Appointment | null, date?: string): void {
    const settings = this.settings();
    if (!settings) return;
    this.dialog
      .open<AppointmentDialog, AppointmentDialogData, boolean>(AppointmentDialog, {
        data: {
          appointment,
          settings,
          date: date ?? (appointment ? localDate(new Date(appointment.startDateTime)) : localDate(new Date())),
          canEdit: this.canBook(),
          companyId: this.companyId() || appointment?.companyId || undefined
        },
        width: '640px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => this.reload());
  }

  configure(): void {
    const settings = this.settings();
    if (!settings) return;
    this.dialog
      .open<ScheduleSettingsDialog, ScheduleSettingsData, SchedulingSettings>(ScheduleSettingsDialog, {
        data: { settings, companyId: this.companyId() || undefined },
        width: '760px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((saved) => {
        this.settings.set(saved);
        this.notify.success('appointments.settingsSaved');
      });
  }

  private loadSettings(): void {
    this.api.settings(this.companyId() || undefined).subscribe({
      next: (s) => this.settings.set(s),
      error: (err) => this.notify.error(err)
    });
  }
}

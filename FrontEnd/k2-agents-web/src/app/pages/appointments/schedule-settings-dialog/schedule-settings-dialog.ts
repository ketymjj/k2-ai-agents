import { Component, inject, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { AppointmentsApi } from '../../../core/api/appointments.api';
import { errorMessage } from '../../../core/http-error';
import { SchedulingSettings } from '../../../core/models';

const DAYS = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

type DayGroup = FormGroup<{
  open: FormControl<boolean>;
  start1: FormControl<string>;
  end1: FormControl<string>;
  split: FormControl<boolean>;
  start2: FormControl<string>;
  end2: FormControl<string>;
}>;

export interface ScheduleSettingsData {
  settings: SchedulingSettings;
  companyId?: string;
}

@Component({
  selector: 'app-schedule-settings-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoPipe],
  templateUrl: './schedule-settings-dialog.html',
  styleUrl: './schedule-settings-dialog.scss'
})
export class ScheduleSettingsDialog {
  private readonly api = inject(AppointmentsApi);
  private readonly ref = inject(MatDialogRef<ScheduleSettingsDialog, SchedulingSettings>);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly data = inject<ScheduleSettingsData>(MAT_DIALOG_DATA);

  protected readonly days = DAYS;
  protected readonly timeZones = ['America/Sao_Paulo', 'America/Manaus', 'America/Recife', 'America/Cuiaba', 'America/Rio_Branco', 'America/Noronha'];
  protected readonly slotOptions = [10, 15, 20, 30, 45, 60];
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly s = this.data.settings;

  protected readonly general = this.fb.group({
    timeZone: [this.s.timeZone || 'America/Sao_Paulo'],
    slotMinutes: [this.s.slotMinutes || 30],
    minAdvanceHours: [Math.round((this.s.minAdvanceMinutes ?? 60) / 60 * 10) / 10, [Validators.min(0), Validators.max(168)]],
    maxDaysAhead: [this.s.maxDaysAhead || 30, [Validators.required, Validators.min(1), Validators.max(365)]]
  });

  protected readonly services = this.fb.array(
    (this.s.services.length ? this.s.services : [{ name: '', durationMinutes: 30 }]).map((sv) =>
      this.fb.group({
        name: [sv.name, [Validators.required, Validators.maxLength(120)]],
        durationMinutes: [sv.durationMinutes, [Validators.required, Validators.min(5), Validators.max(480)]]
      })
    )
  );

  protected readonly week: FormArray<DayGroup> = this.fb.array(
    DAYS.map((day) => {
      const ranges = this.s.weeklyHours?.[day] ?? [];
      return this.fb.group({
        open: [ranges.length > 0],
        start1: [ranges[0]?.start ?? '09:00', Validators.pattern(TIME)],
        end1: [ranges[0]?.end ?? (ranges.length > 1 ? '12:00' : '18:00'), Validators.pattern(TIME)],
        split: [ranges.length > 1],
        start2: [ranges[1]?.start ?? '13:00', Validators.pattern(TIME)],
        end2: [ranges[1]?.end ?? '18:00', Validators.pattern(TIME)]
      });
    })
  );

  addService(): void {
    this.services.push(this.fb.group({ name: ['', [Validators.required, Validators.maxLength(120)]], durationMinutes: [30, [Validators.required, Validators.min(5), Validators.max(480)]] }));
  }

  removeService(i: number): void {
    this.services.removeAt(i);
  }

  save(): void {
    if (this.general.invalid || this.services.invalid || this.week.invalid) {
      [this.general, this.services, this.week].forEach((g) => g.markAllAsTouched());
      return;
    }
    const g = this.general.getRawValue();
    const weeklyHours: Record<string, { start: string; end: string }[]> = {};
    this.week.getRawValue().forEach((d, i) => {
      if (!d.open) return;
      weeklyHours[DAYS[i]] = d.split ? [{ start: d.start1, end: d.end1 }, { start: d.start2, end: d.end2 }] : [{ start: d.start1, end: d.end1 }];
    });
    const settings: SchedulingSettings = {
      timeZone: g.timeZone,
      slotMinutes: Number(g.slotMinutes),
      minAdvanceMinutes: Math.round(Number(g.minAdvanceHours) * 60),
      maxDaysAhead: Number(g.maxDaysAhead),
      services: this.services.getRawValue().map((s) => ({ name: s.name.trim(), durationMinutes: Number(s.durationMinutes) })),
      weeklyHours
    };
    this.saving.set(true);
    this.error.set(null);
    this.api.saveSettings(settings, this.data.companyId).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }
}

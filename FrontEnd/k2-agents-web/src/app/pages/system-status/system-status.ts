import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { SystemInfo, SystemService } from '../../core/system.service';

/** Saúde da plataforma a partir de dados reais da API. */
@Component({
  selector: 'app-system-status',
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslocoPipe],
  templateUrl: './system-status.html',
  styleUrl: './system-status.scss'
})
export class SystemStatus implements OnInit {
  private readonly system = inject(SystemService);

  protected readonly info = signal<SystemInfo | null>(null);
  protected readonly failed = signal(false);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.system.info().subscribe({
      next: (info) => {
        this.info.set(info);
        this.loading.set(false);
      },
      error: () => {
        this.info.set(null);
        this.failed.set(true);
        this.loading.set(false);
      }
    });
  }
}

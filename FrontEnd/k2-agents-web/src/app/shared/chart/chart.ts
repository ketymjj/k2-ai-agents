import { Component, DestroyRef, ElementRef, afterRenderEffect, inject, input, viewChild } from '@angular/core';
import {
  ArcElement, BarController, BarElement, CategoryScale, Chart, ChartConfiguration, DoughnutController, Filler, Legend,
  LineController, LineElement, LinearScale, PointElement, Tooltip
} from 'chart.js';
import { ThemeService } from '../../core/theme.service';

// Só o que os painéis usam (mantém o bundle enxuto).
Chart.register(LineController, BarController, DoughnutController, LineElement, BarElement, PointElement, ArcElement,
  CategoryScale, LinearScale, Tooltip, Legend, Filler);

/** Paleta categórica com contraste suficiente nos temas claro e escuro. Ordem fixa: a mesma série tem sempre a mesma cor. */
export const CHART_COLORS = {
  primary: '#7c3aed',
  blue: '#2e90fa',
  green: '#12b76a',
  amber: '#f79009',
  red: '#f04438',
  teal: '#0ba5ec',
  gray: '#98a2b3'
};

export const CATEGORICAL = [CHART_COLORS.primary, CHART_COLORS.blue, CHART_COLORS.green, CHART_COLORS.amber, CHART_COLORS.red, CHART_COLORS.teal, CHART_COLORS.gray];

/** Gráfico Chart.js recriado quando a configuração ou o tema mudam. */
@Component({
  selector: 'app-chart',
  templateUrl: './chart.html',
  styleUrl: './chart.scss'
})
export class ChartView {
  readonly config = input.required<ChartConfiguration>();
  readonly label = input('');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly theme = inject(ThemeService);
  private chart?: Chart;

  constructor() {
    afterRenderEffect(() => {
      const config = this.config();
      this.theme.theme();
      this.render(config);
    });
    inject(DestroyRef).onDestroy(() => this.chart?.destroy());
  }

  private render(config: ChartConfiguration): void {
    this.chart?.destroy();
    const css = getComputedStyle(document.documentElement);
    const token = (name: string, fallback: string) => css.getPropertyValue(name).trim() || fallback;
    Chart.defaults.color = token('--text-muted', '#667085');
    Chart.defaults.borderColor = token('--border', '#e4e7ec');
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.plugins.legend.labels.boxWidth = 12;
    Chart.defaults.plugins.legend.labels.usePointStyle = true;
    Chart.defaults.maintainAspectRatio = false;
    // Números dos eixos e tooltips no idioma da interface (o <html lang> acompanha a troca de idioma).
    Chart.defaults.locale = document.documentElement.lang === 'en' ? 'en-US' : 'pt-BR';
    this.chart = new Chart(this.canvas().nativeElement, config);
  }
}

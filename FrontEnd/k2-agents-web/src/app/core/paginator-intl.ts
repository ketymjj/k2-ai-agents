import { Injectable, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';

@Injectable()
export class TranslatedPaginatorIntl extends MatPaginatorIntl {
  private readonly transloco = inject(TranslocoService);

  constructor() {
    super();
    this.transloco.langChanges$.subscribe((lang) => {
      const pt = lang === 'pt-BR';
      this.itemsPerPageLabel = pt ? 'Itens por página' : 'Items per page';
      this.nextPageLabel = pt ? 'Próxima página' : 'Next page';
      this.previousPageLabel = pt ? 'Página anterior' : 'Previous page';
      this.firstPageLabel = pt ? 'Primeira página' : 'First page';
      this.lastPageLabel = pt ? 'Última página' : 'Last page';
      this.getRangeLabel = (page, pageSize, length) => {
        if (length === 0) return pt ? '0 de 0' : '0 of 0';
        const start = page * pageSize + 1;
        const end = Math.min(start + pageSize - 1, length);
        return `${start}–${end} ${pt ? 'de' : 'of'} ${length}`;
      };
      this.changes.next();
    });
  }
}

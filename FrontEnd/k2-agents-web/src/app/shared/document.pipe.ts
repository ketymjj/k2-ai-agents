import { Pipe, PipeTransform } from '@angular/core';

/** Formata CNPJ (14 dígitos) ou CPF (11 dígitos); demais valores passam inalterados. */
@Pipe({ name: 'document' })
export class DocumentPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    if (value.length === 14) return value.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5');
    if (value.length === 11) return value.replace(/^(\d{3})(\d{3})(\d{3})(\d{2})$/, '$1.$2.$3-$4');
    return value;
  }
}

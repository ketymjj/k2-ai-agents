import en from '../../../public/i18n/en.json';
import pt from '../../../public/i18n/pt-BR.json';

type Tree = { [key: string]: unknown };

function keys(obj: Tree, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    return v && typeof v === 'object' && !Array.isArray(v) ? keys(v as Tree, path) : [path];
  });
}

function values(obj: Tree, path: string): unknown {
  return path.split('.').reduce<unknown>((node, part) => (node as Tree | undefined)?.[part], obj);
}

describe('traduções', () => {
  const ptKeys = keys(pt as Tree);
  const enKeys = keys(en as Tree);

  it('pt-BR e en têm exatamente as mesmas chaves', () => {
    expect(ptKeys.filter((k) => !enKeys.includes(k))).withContext('faltando em en').toEqual([]);
    expect(enKeys.filter((k) => !ptKeys.includes(k))).withContext('faltando em pt-BR').toEqual([]);
  });

  it('nenhum texto está vazio e os parâmetros {{x}} coincidem entre os idiomas', () => {
    const params = (v: unknown) => (typeof v === 'string' ? (v.match(/\{\{\s*\w+\s*\}\}/g) ?? []).map((p) => p.replace(/\s/g, '')).sort() : []);
    for (const key of ptKeys) {
      const a = values(pt as Tree, key);
      const b = values(en as Tree, key);
      if (typeof a === 'string') expect(a.trim().length).withContext(`pt-BR vazio: ${key}`).toBeGreaterThan(0);
      if (typeof b === 'string') expect(b.trim().length).withContext(`en vazio: ${key}`).toBeGreaterThan(0);
      expect(params(b)).withContext(`parâmetros diferentes: ${key}`).toEqual(params(a));
    }
  });
});

import { computed, Signal } from '@angular/core';
import { AgentListItem, Company } from '../../core/models';

/** Dados compartilhados pelos diálogos da base de conhecimento para escolher empresa e agente. */
export interface KnowledgeScopeData {
  agents: AgentListItem[];
  companies: Company[];
  isSuperAdmin: boolean;
  defaultCompanyId: string | null;
}

/** Agentes da empresa escolhida (Super Admin) ou todos os visíveis (usuário de empresa). */
export function agentsForCompany(data: KnowledgeScopeData, companyId: Signal<string | undefined>) {
  return computed(() => {
    const company = companyId();
    return data.isSuperAdmin ? data.agents.filter((a) => a.companyId === company) : data.agents;
  });
}

export const ALLOWED_EXTENSIONS = ['.pdf', '.txt', '.docx'];
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export function formatBytes(bytes: number | null | undefined): string {
  if (!bytes) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

import { Routes } from '@angular/router';
import { authGuard, guestGuard, homeRedirectGuard, roleGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login').then((m) => m.Login)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell').then((m) => m.Shell),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirectGuard], children: [] },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardPage)
      },
      {
        path: 'learn',
        loadComponent: () => import('./pages/learn/learn').then((m) => m.LearnPage)
      },
      {
        path: 'costs',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin', 'CompanyAdmin'] },
        loadComponent: () => import('./pages/costs/costs-page/costs-page').then((m) => m.CostsPage)
      },
      {
        path: 'companies',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin'] },
        loadComponent: () => import('./pages/companies/companies-list/companies-list').then((m) => m.CompaniesList)
      },
      {
        path: 'companies/:id',
        loadComponent: () => import('./pages/companies/company-detail/company-detail').then((m) => m.CompanyDetail)
      },
      {
        path: 'agents',
        loadComponent: () => import('./pages/agents/agents-list/agents-list').then((m) => m.AgentsList)
      },
      {
        path: 'agents/new',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin', 'CompanyAdmin', 'AgentManager'] },
        loadComponent: () => import('./pages/agents/agent-editor/agent-editor').then((m) => m.AgentEditor)
      },
      {
        path: 'agents/:id',
        loadComponent: () => import('./pages/agents/agent-editor/agent-editor').then((m) => m.AgentEditor)
      },
      {
        path: 'conversations',
        loadComponent: () => import('./pages/conversations/inbox/inbox').then((m) => m.Inbox)
      },
      {
        path: 'contacts',
        loadComponent: () => import('./pages/contacts/contacts-list/contacts-list').then((m) => m.ContactsList)
      },
      {
        path: 'contacts/:id',
        loadComponent: () => import('./pages/contacts/contact-detail/contact-detail').then((m) => m.ContactDetail)
      },
      {
        path: 'appointments',
        loadComponent: () => import('./pages/appointments/appointments-calendar/appointments-calendar').then((m) => m.AppointmentsCalendar)
      },
      {
        path: 'automations',
        loadComponent: () => import('./pages/automations/automations-list/automations-list').then((m) => m.AutomationsList)
      },
      {
        path: 'channels',
        loadComponent: () => import('./pages/channels/channels-list/channels-list').then((m) => m.ChannelsList)
      },
      {
        path: 'knowledge',
        loadComponent: () => import('./pages/knowledge/knowledge-list/knowledge-list').then((m) => m.KnowledgeList)
      },
      {
        path: 'playground',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin', 'CompanyAdmin', 'AgentManager'] },
        loadComponent: () => import('./pages/playground/playground').then((m) => m.Playground)
      },
      {
        path: 'users',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin', 'CompanyAdmin'] },
        loadComponent: () => import('./pages/users/users-list/users-list').then((m) => m.UsersList)
      },
      {
        path: 'system',
        loadComponent: () => import('./pages/system-status/system-status').then((m) => m.SystemStatus)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];

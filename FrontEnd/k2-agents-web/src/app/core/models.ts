export type Role = 'SuperAdmin' | 'CompanyAdmin' | 'AgentManager' | 'Attendant' | 'Viewer';
export type Language = 'pt-BR' | 'en';
export type CompanyStatus = 'Active' | 'Inactive';

export const ALL_ROLES: Role[] = ['SuperAdmin', 'CompanyAdmin', 'AgentManager', 'Attendant', 'Viewer'];

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  role: Role;
  companyId: string | null;
  companyName: string | null;
  preferredLanguage: Language;
  beginnerMode: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: AuthUser;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Company {
  id: string;
  name: string;
  document: string | null;
  email: string | null;
  phone: string | null;
  industry: string | null;
  website: string | null;
  status: CompanyStatus;
  conversationRetentionDays: number | null;
  createdAt: string;
  agentCount: number;
  userCount: number;
}

export interface SaveCompany {
  name: string;
  document: string | null;
  email: string | null;
  phone: string | null;
  industry: string | null;
  website: string | null;
  conversationRetentionDays: number | null;
}

export interface CompanySummary {
  companyId: string;
  agents: number;
  activeAgents: number;
  users: number;
  contacts: number;
  leads: number;
  conversations: number;
  openConversations: number;
  waitingHumanConversations: number;
  appointments: number;
  knowledgeDocuments: number;
}

export interface User {
  id: string;
  name: string;
  email: string;
  role: Role;
  companyId: string | null;
  companyName: string | null;
  isActive: boolean;
  preferredLanguage: Language;
  lastLoginAt: string | null;
  isLocked: boolean;
  createdAt: string;
}

export interface RoleInfo {
  id: Role;
  name: string;
  description: string;
}

export interface CreateUser {
  name: string;
  email: string;
  password: string;
  role: Role;
  companyId: string | null;
  preferredLanguage: Language;
}

export type BusinessType = 'Sales' | 'Support' | 'LeadCapture' | 'Scheduling' | 'GeneralInfo';
export type AgentStatus = 'Draft' | 'Active' | 'Inactive';
export const BUSINESS_TYPES: BusinessType[] = ['Sales', 'Support', 'LeadCapture', 'Scheduling', 'GeneralInfo'];
export const AGENT_MANAGER_ROLES: Role[] = ['SuperAdmin', 'CompanyAdmin', 'AgentManager'];

export interface FaqItem {
  question: string;
  answer: string;
}

export interface AgentBehavior {
  companyDisplayName: string | null;
  objective: string;
  personality: string | null;
  tone: string | null;
  businessHours: string | null;
  faqs: FaqItem[];
  restrictions: string[];
  handoffRules: string[];
}

export interface AgentListItem {
  id: string;
  companyId: string;
  companyName: string | null;
  name: string;
  description: string | null;
  businessType: BusinessType;
  model: string;
  status: AgentStatus;
  responseLanguage: Language;
  currentVersion: number;
  updatedAt: string;
}

export interface Agent extends AgentListItem {
  systemPrompt: string;
  welcomeMessage: string | null;
  temperature: number;
  maxOutputTokens: number;
  behavior: AgentBehavior;
  createdAt: string;
}

export interface SaveAgent {
  companyId: string | null;
  name: string;
  description: string | null;
  businessType: BusinessType;
  responseLanguage: Language;
  behavior: AgentBehavior;
  systemPrompt: string | null;
  welcomeMessage: string | null;
  model: string;
  temperature: number;
  maxOutputTokens: number;
  changeNote: string | null;
}

export interface AgentVersion {
  id: string;
  versionNumber: number;
  systemPrompt: string;
  behavior: AgentBehavior;
  model: string;
  temperature: number;
  maxOutputTokens: number;
  welcomeMessage: string | null;
  changeNote: string | null;
  createdByName: string | null;
  createdAt: string;
  isCurrent: boolean;
}

export interface AIModelOption {
  id: string;
  label: string;
  provider: string;
  inputPricePer1M: number;
  outputPricePer1M: number;
  isDefault: boolean;
}

export interface AgentTemplate {
  businessType: BusinessType;
  language: Language;
  description: string;
  welcomeMessage: string;
  behavior: AgentBehavior;
}

export interface PromptPreview {
  prompt: string;
  platformRules: string;
}

export type SenderType = 'Contact' | 'AI' | 'Human' | 'System';
export type ConversationStatus = 'Open' | 'WaitingHuman' | 'HumanActive' | 'Closed';

export interface KnowledgeSourceRef {
  documentId: string;
  title: string;
}

export interface ChatMessage {
  id: string;
  senderType: SenderType;
  content: string;
  createdAt: string;
  inputTokens: number;
  outputTokens: number;
  responseTimeMs: number | null;
  isSimulated: boolean;
  sources: KnowledgeSourceRef[];
}

export type ChannelType = 'WebChat' | 'WhatsApp' | 'Playground';
export type ChannelStatus = 'Draft' | 'Active' | 'Inactive' | 'PendingCredentials';
export const CONVERSATION_STATUSES: ConversationStatus[] = ['Open', 'WaitingHuman', 'HumanActive', 'Closed'];

export interface ConversationSummary {
  id: string;
  companyId: string;
  companyName: string | null;
  agentId: string;
  agentName: string;
  channel: ChannelType;
  status: ConversationStatus;
  contactId: string | null;
  displayName: string;
  lastMessagePreview: string | null;
  lastMessageSender: SenderType | null;
  lastMessageAt: string;
  startedAt: string;
  closedAt: string | null;
  messageCount: number;
  isDemo: boolean;
  assignedUserId: string | null;
  assignedUserName: string | null;
}

export type ContactStatus = 'New' | 'InService' | 'Qualified' | 'Converted' | 'Lost';
export type InterestLevel = 'Unknown' | 'Low' | 'Medium' | 'High';
export const CONTACT_STATUSES: ContactStatus[] = ['New', 'InService', 'Qualified', 'Converted', 'Lost'];
export const INTEREST_LEVELS: InterestLevel[] = ['Unknown', 'Low', 'Medium', 'High'];

export interface ConversationContact {
  id: string;
  name: string;
  email: string | null;
  phone: string | null;
  status: ContactStatus;
  isLead: boolean;
  interest: InterestLevel;
}

export interface ConversationDetail {
  summary: ConversationSummary;
  messages: ChatMessage[];
  contact: ConversationContact | null;
}

export interface ConversationEvent {
  action: string;
  details: string | null;
  userName: string | null;
  createdAt: string;
}

export interface Contact {
  id: string;
  companyId: string;
  companyName: string | null;
  name: string;
  email: string | null;
  phone: string | null;
  source: string | null;
  status: ContactStatus;
  isLead: boolean;
  interest: InterestLevel;
  notes: string | null;
  isAnonymized: boolean;
  conversationCount: number;
  lastConversationAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ContactDetail {
  contact: Contact;
  conversations: ConversationSummary[];
}

export interface SaveContact {
  companyId: string | null;
  name: string;
  email: string | null;
  phone: string | null;
  source: string | null;
  status: ContactStatus;
  isLead: boolean;
  interest: InterestLevel;
  notes: string | null;
}

export interface Funnel {
  stages: { status: ContactStatus; count: number }[];
  total: number;
  leads: number;
}

export type AppointmentStatus = 'Scheduled' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow';
export const ACTIVE_APPOINTMENT: AppointmentStatus[] = ['Scheduled', 'Confirmed'];

export interface Appointment {
  id: string;
  companyId: string;
  companyName: string | null;
  contactId: string | null;
  contactName: string | null;
  contactPhone: string | null;
  agentId: string | null;
  agentName: string | null;
  serviceName: string;
  startDateTime: string;
  endDateTime: string;
  status: AppointmentStatus;
  notes: string | null;
  source: 'Manual' | 'Agent';
  conversationId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ServiceOption {
  name: string;
  durationMinutes: number;
}

export interface TimeRange {
  start: string;
  end: string;
}

export interface SchedulingSettings {
  timeZone: string;
  slotMinutes: number;
  minAdvanceMinutes: number;
  maxDaysAhead: number;
  services: ServiceOption[];
  weeklyHours: Record<string, TimeRange[]>;
}

export interface Slot {
  start: string;
  end: string;
  localLabel: string;
}

export interface Availability {
  date: string;
  service: string;
  durationMinutes: number;
  timeZone: string;
  slots: Slot[];
}

export type AutomationTrigger = 'LeadCreated' | 'ConversationTransferred' | 'AppointmentCreated' | 'ConversationClosed';
export type AutomationAction = 'CreateNotification' | 'NotifyAttendants' | 'CreateInternalConfirmation' | 'RecordMetric';

export interface AutomationRule {
  id: string;
  companyId: string;
  companyName: string | null;
  name: string;
  triggerType: AutomationTrigger;
  actionType: AutomationAction;
  configuration: { title: string | null; message: string | null };
  enabled: boolean;
  executionCount: number;
  lastExecutedAt: string | null;
  createdAt: string;
}

export interface AutomationCatalog {
  allowed: Record<AutomationTrigger, AutomationAction[]>;
  placeholders: string[];
}

export type NotificationType = 'Info' | 'NewLead' | 'Handoff' | 'Appointment' | 'System';

export interface AppNotification {
  id: string;
  companyId: string;
  userId: string | null;
  type: NotificationType;
  title: string;
  message: string;
  link: string | null;
  createdAt: string;
  isRead: boolean;
}

export interface WebChatSettings {
  title: string;
  subtitle: string | null;
  primaryColor: string;
  position: 'right' | 'left';
  welcomeMessage: string | null;
  allowSimulatedReplies: boolean;
  offlineMessage: string | null;
}

export interface Channel {
  id: string;
  companyId: string;
  companyName: string | null;
  agentId: string;
  agentName: string;
  channelType: ChannelType;
  name: string;
  status: ChannelStatus;
  publicToken: string | null;
  settings: WebChatSettings;
  allowedOrigins: string[];
  conversationCount: number;
  createdAt: string;
  updatedAt: string;
  /** Só em canais WhatsApp. As credenciais nunca vêm da API: apenas se estão configuradas. */
  whatsApp: WhatsAppInfo | null;
}

export interface WhatsAppInfo {
  phoneNumberId: string;
  businessAccountId: string | null;
  displayPhoneNumber: string | null;
  verifyToken: string;
  hasAccessToken: boolean;
  hasAppSecret: boolean;
}

export interface SaveWhatsApp {
  companyId: string | null;
  agentId: string;
  name: string;
  phoneNumberId: string;
  businessAccountId: string | null;
  displayPhoneNumber: string | null;
  /** Vazio = manter o valor salvo. */
  accessToken: string | null;
  appSecret: string | null;
}

export interface WhatsAppConnectionResult {
  ok: boolean;
  displayPhoneNumber: string | null;
  verifiedName: string | null;
  errorCode: string | null;
}

export interface SaveWebChat {
  companyId: string | null;
  agentId: string;
  name: string;
  settings: WebChatSettings;
  allowedOrigins: string[];
}

export type KnowledgeSourceType = 'Text' | 'Faq' | 'Pdf' | 'Txt' | 'Docx';
export type KnowledgeStatus = 'Processing' | 'Ready' | 'Failed' | 'Archived';
export const KNOWLEDGE_TYPES: KnowledgeSourceType[] = ['Text', 'Faq', 'Pdf', 'Txt', 'Docx'];

export interface KnowledgeListItem {
  id: string;
  companyId: string;
  companyName: string | null;
  agentId: string | null;
  agentName: string | null;
  title: string;
  sourceType: KnowledgeSourceType;
  status: KnowledgeStatus;
  originalFileName: string | null;
  fileSizeBytes: number | null;
  chunkCount: number;
  contentLength: number;
  createdAt: string;
  updatedAt: string;
}

export interface KnowledgeDocument extends Omit<KnowledgeListItem, 'contentLength'> {
  content: string;
  hasFile: boolean;
  errorMessage: string | null;
}

export interface KnowledgeSearchHit {
  documentId: string;
  title: string;
  content: string;
  score: number;
}

export interface SessionTotals {
  requests: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
  simulatedReplies: number;
}

export interface PlaygroundSession {
  id: string;
  agentId: string;
  agentName: string;
  status: ConversationStatus;
  startedAt: string;
  messages: ChatMessage[];
  totals: SessionTotals;
}

export interface PlaygroundSessionSummary {
  id: string;
  startedAt: string;
  lastMessageAt: string;
  messageCount: number;
  status: ConversationStatus;
}

export interface SendMessageResult {
  userMessage: ChatMessage;
  reply: ChatMessage;
  estimatedCostUsd: number;
  model: string;
  totals: SessionTotals;
  handoffRequested: boolean;
}

export interface PlaygroundStatus {
  aiConfigured: boolean;
  provider: string;
  dailyRequestLimit: number;
  dailyTokenLimit: number;
  requestsToday: number;
  tokensToday: number;
  maxMessageChars: number;
}

export interface UpdateUser {
  name: string;
  email: string;
  role: Role;
  password: string | null;
  preferredLanguage: Language;
}

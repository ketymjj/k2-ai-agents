namespace K2Agents.Domain.Enums;

public enum CompanyStatus { Active = 1, Inactive = 2 }

public enum UserRole { SuperAdmin = 1, CompanyAdmin = 2, AgentManager = 3, Attendant = 4, Viewer = 5 }

public enum AgentStatus { Draft = 1, Active = 2, Inactive = 3 }

public enum BusinessType { Sales = 1, Support = 2, LeadCapture = 3, Scheduling = 4, GeneralInfo = 5 }

public enum KnowledgeSourceType { Text = 1, Faq = 2, Pdf = 3, Txt = 4, Docx = 5 }

public enum KnowledgeStatus { Processing = 1, Ready = 2, Failed = 3, Archived = 4 }

public enum ChannelType { WebChat = 1, WhatsApp = 2, Playground = 3 }

public enum ConversationStatus { Open = 1, WaitingHuman = 2, HumanActive = 3, Closed = 4 }

public enum SenderType { Contact = 1, AI = 2, Human = 3, System = 4 }

public enum ContactStatus { New = 1, InService = 2, Qualified = 3, Converted = 4, Lost = 5 }

public enum InterestLevel { Unknown = 0, Low = 1, Medium = 2, High = 3 }

public enum AppointmentStatus { Scheduled = 1, Confirmed = 2, Cancelled = 3, Completed = 4, NoShow = 5 }

public enum AutomationTrigger { LeadCreated = 1, ConversationTransferred = 2, AppointmentCreated = 3, ConversationClosed = 4 }

public enum AutomationAction { CreateNotification = 1, NotifyAttendants = 2, CreateInternalConfirmation = 3, RecordMetric = 4 }

public enum ChannelStatus { Draft = 1, Active = 2, Inactive = 3, PendingCredentials = 4 }

public enum NotificationType { Info = 1, NewLead = 2, Handoff = 3, Appointment = 4, System = 5 }

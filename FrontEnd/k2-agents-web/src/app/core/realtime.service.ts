import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { AppNotification, ChatMessage, ConversationSummary } from './models';

export interface MessageCreatedEvent {
  conversationId: string;
  message: ChatMessage;
}

export type RealtimeState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * Conexão SignalR da central de atendimento. Uma única conexão compartilhada, aberta sob demanda;
 * o token vem do AuthService a cada (re)conexão, então acompanha a renovação da sessão.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private users = 0;

  readonly state = signal<RealtimeState>('disconnected');
  readonly messageCreated$ = new Subject<MessageCreatedEvent>();
  readonly conversationUpdated$ = new Subject<ConversationSummary>();
  readonly notification$ = new Subject<AppNotification>();

  /** Chamado pelas telas que precisam de eventos; a conexão fecha quando nenhuma tela usa. */
  acquire(): void {
    this.users++;
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/inbox', { accessTokenFactory: () => this.auth.token ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('messageCreated', (e: MessageCreatedEvent) => this.messageCreated$.next(e));
    connection.on('conversationUpdated', (c: ConversationSummary) => this.conversationUpdated$.next(c));
    connection.on('notification', (n: AppNotification) => this.notification$.next(n));
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => this.state.set('connected'));
    connection.onclose(() => this.state.set('disconnected'));

    this.connection = connection;
    this.state.set('connecting');
    connection
      .start()
      .then(() => this.state.set('connected'))
      .catch(() => this.state.set('disconnected'));
  }

  release(): void {
    this.users = Math.max(0, this.users - 1);
    if (this.users > 0 || !this.connection) return;
    const connection = this.connection;
    this.connection = null;
    if (connection.state !== HubConnectionState.Disconnected) connection.stop();
    this.state.set('disconnected');
  }
}

using K2Agents.Domain.Enums;

namespace K2Agents.Infrastructure.Persistence.Seed;

/// <summary>Conversas fictícias de demonstração (Conversation.IsDemo = true). Nenhuma chamada de IA é feita.</summary>
internal static class DemoConversations
{
    public record Line(SenderType Sender, string Text);
    public record Script(string CompanySlug, int DaysAgo, ConversationStatus Status, Line[] Lines);

    private static Line C(string t) => new(SenderType.Contact, t);
    private static Line A(string t) => new(SenderType.AI, t);
    private static Line S(string t) => new(SenderType.System, t);

    public static readonly Script[] All =
    [
        new("padaria", 6, ConversationStatus.Closed,
        [
            C("Bom dia! Vocês fazem bolo de aniversário?"),
            A("Bom dia! Fazemos sim 🎂 Bolos de festa são sob encomenda a partir de R$ 120,00 o quilo, com 48 horas de antecedência. Para quantas pessoas seria?"),
            C("Umas 20 pessoas, para sábado."),
            A("Perfeito! Para 20 pessoas recomendamos cerca de 2 kg. Vou registrar seu interesse e nossa equipe confirma os detalhes com você. Pode me informar seu nome e telefone?"),
            C("Mariana, (11) 98888-1234"),
            A("Obrigada, Mariana! Seu pedido de interesse foi registrado. Até sábado! 😊"),
            S("Conversa encerrada.")
        ]),
        new("padaria", 1, ConversationStatus.Open,
        [
            C("Que horas sai pão quentinho?"),
            A("Temos fornadas novas às 6h, 11h e 16h! O pão francês custa R$ 0,90 a unidade.")
        ]),
        new("padaria", 0, ConversationStatus.WaitingHuman,
        [
            C("Comprei um bolo ontem e veio com a cobertura derretida."),
            A("Sinto muito pelo ocorrido! Vou transferir você para um atendente, que vai resolver isso com prioridade."),
            S("Conversa transferida para atendimento humano.")
        ]),

        new("studiobella", 3, ConversationStatus.Closed,
        [
            C("Oi! Quanto custa design de sobrancelha com henna?"),
            A("Oi! O design com henna custa R$ 60,00 💅 Quer que eu veja um horário para você?"),
            C("Quero sim, sexta à tarde."),
            A("Ótimo! Vou registrar sua preferência e a equipe confirma o horário disponível na sexta à tarde."),
            S("Conversa encerrada.")
        ]),
        new("studiobella", 0, ConversationStatus.Open,
        [
            C("Vocês trabalham com pacote para noivas?"),
            A("Trabalhamos sim! Os valores do pacote noiva são sob consulta. Posso pedir para uma especialista entrar em contato?")
        ]),

        new("horizonte", 9, ConversationStatus.Closed,
        [
            C("Procuro apartamento de 2 quartos para alugar no centro."),
            A("Temos um apartamento de 2 quartos no Centro, 58 m², com 1 vaga: aluguel de R$ 2.300,00 + condomínio de R$ 450,00. Gostaria de agendar uma visita com um corretor?"),
            C("Sim, por favor."),
            A("Perfeito! Vou encaminhar seu contato para um corretor agendar a visita."),
            S("Conversa encerrada.")
        ]),
        new("horizonte", 2, ConversationStatus.WaitingHuman,
        [
            C("Quero fazer uma proposta na casa do Jardim Europa."),
            A("Que ótimo! Propostas são tratadas diretamente por um corretor. Vou transferir você agora."),
            S("Conversa transferida para atendimento humano.")
        ]),

        new("petvida", 4, ConversationStatus.Closed,
        [
            C("Meu filhote tem 2 meses, já pode vacinar?"),
            A("Pode sim! A vacinação começa a partir de 45 dias de vida. A V10 para cães custa R$ 110,00. Traga a carteirinha de vacinação, se já tiver."),
            S("Conversa encerrada.")
        ]),
        new("petvida", 0, ConversationStatus.Open,
        [
            C("Qual o horário de vocês no sábado?"),
            A("Aos sábados atendemos das 8h às 20h, e temos plantão de emergência 24 horas 🐾")
        ]),

        new("energia", 5, ConversationStatus.Closed,
        [
            C("Quanto custa o plano anual?"),
            A("O plano anual custa R$ 1.199,00 — equivale a R$ 99,92 por mês 💪 Inclui musculação e todas as aulas coletivas. Quer agendar uma aula experimental gratuita?"),
            C("Quero! Amanhã de manhã."),
            A("Show! Vou registrar seu interesse para amanhã de manhã e a recepção confirma o horário."),
            S("Conversa encerrada.")
        ]),
        new("energia", 1, ConversationStatus.Open,
        [
            C("Tem aula de pilates?"),
            A("Temos sim! Pilates solo está incluído em todos os planos, junto com funcional, spinning e dança.")
        ])
    ];
}

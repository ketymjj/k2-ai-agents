using K2Agents.Domain.Enums;

namespace K2Agents.Infrastructure.Persistence.Seed;

/// <summary>Contatos fictícios de demonstração (dados inventados, telefones e e-mails em domínio .demo).</summary>
internal static class DemoContacts
{
    public record Def(string CompanySlug, string Name, string? Email, string? Phone, ContactStatus Status, InterestLevel Interest, string? Notes, int DaysAgo);

    public static readonly Def[] All =
    [
        new("padaria", "Mariana Alves", "mariana.alves@email.demo", "11988881234", ContactStatus.Converted, InterestLevel.High, "Bolo de aniversário para 20 pessoas (sábado).", 6),
        new("padaria", "Carlos Menezes", "carlos.m@email.demo", "11977772345", ContactStatus.Qualified, InterestLevel.Medium, "Interesse em salgados para evento corporativo.", 12),
        new("padaria", "Fernanda Lima", null, "11966663456", ContactStatus.New, InterestLevel.Unknown, null, 2),
        new("padaria", "Roberto Dias", "roberto.dias@email.demo", null, ContactStatus.Lost, InterestLevel.Low, "Buscava entrega fora da área atendida.", 20),

        new("studiobella", "Juliana Rocha", "juliana.rocha@email.demo", "11955554567", ContactStatus.Converted, InterestLevel.High, "Design de sobrancelha com henna.", 3),
        new("studiobella", "Patrícia Gomes", "paty.gomes@email.demo", "11944445678", ContactStatus.InService, InterestLevel.High, "Pacote noiva — aguardando orçamento.", 1),
        new("studiobella", "Amanda Silva", null, "11933336789", ContactStatus.New, InterestLevel.Medium, null, 4),

        new("horizonte", "Ricardo Souza", "ricardo.souza@email.demo", "11922227890", ContactStatus.Qualified, InterestLevel.High, "Apartamento 2 quartos no Centro, até R$ 2.500.", 9),
        new("horizonte", "Beatriz Nunes", "bia.nunes@email.demo", "11911118901", ContactStatus.InService, InterestLevel.High, "Proposta na casa do Jardim Europa.", 2),
        new("horizonte", "Thiago Martins", "thiago.m@email.demo", null, ContactStatus.New, InterestLevel.Low, null, 15),

        new("petvida", "Larissa Costa", "larissa.costa@email.demo", "11900009012", ContactStatus.Converted, InterestLevel.High, "Vacinação do filhote (V10).", 4),
        new("petvida", "Eduardo Ramos", null, "11989890123", ContactStatus.New, InterestLevel.Medium, null, 7),

        new("energia", "Gabriel Ferreira", "gabriel.f@email.demo", "11978781234", ContactStatus.Converted, InterestLevel.High, "Plano anual após aula experimental.", 5),
        new("energia", "Camila Ribeiro", "camila.r@email.demo", "11967672345", ContactStatus.Qualified, InterestLevel.Medium, "Interesse em pilates.", 1),
        new("energia", "Lucas Pereira", null, "11956563456", ContactStatus.Lost, InterestLevel.Low, "Achou o valor acima do orçamento.", 25)
    ];
}

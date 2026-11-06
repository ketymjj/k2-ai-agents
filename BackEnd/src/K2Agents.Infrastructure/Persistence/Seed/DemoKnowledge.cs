using K2Agents.Domain.Enums;

namespace K2Agents.Infrastructure.Persistence.Seed;

/// <summary>Conteúdo fictício da base de conhecimento das empresas de demonstração.</summary>
internal static class DemoKnowledge
{
    public record Doc(string CompanySlug, string Title, string Content, KnowledgeSourceType Type = KnowledgeSourceType.Text);

    public static readonly Doc[] All =
    [
        new("padaria", "Cardápio e preços",
            """
            Pães
            Pão francês: R$ 0,90 a unidade. Sai fornada nova às 6h, 11h e 16h.
            Pão de forma integral: R$ 12,50.
            Pão de queijo: R$ 3,50 a unidade ou R$ 45,00 o quilo.

            Bolos
            Bolo de cenoura com cobertura de chocolate: R$ 38,00 (inteiro, serve 12 pessoas).
            Bolo de fubá: R$ 28,00.
            Bolos de festa sob encomenda a partir de R$ 120,00 o quilo, com 48 horas de antecedência.

            Salgados e bebidas
            Coxinha, esfiha e enroladinho: R$ 7,00 cada.
            Café coado: R$ 5,00. Cappuccino: R$ 9,00. Suco natural de laranja: R$ 10,00.
            """),
        new("padaria", "Endereço e estacionamento",
            "A Padaria Jardim Andrade fica na Rua das Acácias, 245, Jardim Andrade, São Paulo. Há estacionamento gratuito para clientes com 12 vagas."),
        new("padaria", "Vocês têm opções sem glúten?",
            "Sim. Temos pão sem glúten às terças e sextas, produzido em área separada. Para outras opções, consulte a disponibilidade no dia.",
            KnowledgeSourceType.Faq),

        new("studiobella", "Serviços e preços",
            """
            Cabelo: corte feminino R$ 90,00 (1 hora); escova R$ 60,00 (45 minutos); hidratação R$ 80,00.
            Unhas: manicure R$ 35,00; pedicure R$ 40,00; esmaltação em gel R$ 75,00.
            Sobrancelha: design R$ 45,00; design com henna R$ 60,00.
            Pacote noiva: valores sob consulta com a equipe.
            """),
        new("studiobella", "Política de cancelamento",
            "Cancelamentos e remarcações devem ser feitos com até 24 horas de antecedência. Faltas sem aviso podem gerar cobrança de 50% do serviço no próximo agendamento."),

        new("horizonte", "Imóveis em destaque",
            """
            Apartamento 2 quartos no Centro, 58 m², 1 vaga: aluguel R$ 2.300,00 + condomínio R$ 450,00.
            Casa 3 quartos no Jardim Europa, 140 m², quintal, 2 vagas: venda R$ 780.000,00.
            Studio mobiliado próximo ao metrô, 32 m²: aluguel R$ 1.900,00 com condomínio incluso.
            Visitas são agendadas com um corretor de segunda a sábado.
            """),
        new("horizonte", "Garantias para locação",
            "Aceitamos fiador com imóvel quitado, seguro-fiança ou título de capitalização equivalente a 6 meses de aluguel."),

        new("petvida", "Vacinas e consultas",
            """
            Consulta clínica geral: R$ 150,00. Retorno em até 15 dias sem custo.
            Vacina V10 para cães: R$ 110,00. Vacina antirrábica: R$ 80,00. Vacina V4 para gatos: R$ 120,00.
            Filhotes devem iniciar a vacinação a partir de 45 dias de vida.
            Banho e tosa com agendamento de terça a sábado.
            """),
        new("petvida", "Preparo para cirurgias",
            "Para cirurgias eletivas, o animal deve fazer jejum de alimento de 8 horas e de água de 4 horas. Exames pré-operatórios são obrigatórios."),

        new("energia", "Planos e modalidades",
            """
            Plano mensal: R$ 129,90. Plano trimestral: R$ 349,90. Plano anual: R$ 1.199,00 (equivale a R$ 99,92 por mês).
            Todos os planos incluem musculação e aulas coletivas: funcional, spinning, pilates solo e dança.
            Avaliação física gratuita na matrícula.
            """),
        new("energia", "Posso congelar meu plano?",
            "Planos trimestrais e anuais podem ser congelados uma vez por até 30 dias, mediante solicitação na recepção.",
            KnowledgeSourceType.Faq)
    ];
}

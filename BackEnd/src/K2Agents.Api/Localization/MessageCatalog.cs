using System.Globalization;
using System.Text.RegularExpressions;

namespace K2Agents.Api.Localization;

/// <summary>
/// Traduz, na saída da API, as mensagens de negócio escritas em português (idioma padrão) para o idioma pedido
/// pelo cliente (Accept-Language). Mensagens com parâmetros usam {0}, {1}... no modelo; o teste
/// MessageCatalogCompletenessTests garante que toda mensagem do código-fonte tem tradução aqui.
/// </summary>
public static class MessageCatalog
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // ----- Api -----
        ["Dados inválidos."] = "Invalid data.",
        ["Ocorreu um erro inesperado. Tente novamente."] = "An unexpected error occurred. Please try again.",
        ["Muitas requisições. Aguarde alguns instantes e tente novamente."] = "Too many requests. Please wait a moment and try again.",
        ["Acesso negado."] = "Access denied.",
        ["{0} não encontrado(a)."] = "{0} not found.",

        // ----- Autenticação e usuários -----
        ["A empresa deste usuário está desativada."] = "This user's company is deactivated.",
        ["A nova senha deve ser diferente da atual."] = "The new password must be different from the current one.",
        ["A senha deve conter um número."] = "The password must contain a number.",
        ["A senha deve conter uma letra maiúscula."] = "The password must contain an uppercase letter.",
        ["A senha deve conter uma letra minúscula."] = "The password must contain a lowercase letter.",
        ["A senha deve ter ao menos 8 caracteres."] = "The password must have at least 8 characters.",
        ["Apenas o Super Admin pode criar administradores da plataforma."] = "Only the Super Admin can create platform administrators.",
        ["Conta temporariamente bloqueada por excesso de tentativas. Tente novamente mais tarde."] = "Account temporarily locked after too many attempts. Please try again later.",
        ["E-mail inválido."] = "Invalid e-mail.",
        ["E-mail ou senha inválidos."] = "Invalid e-mail or password.",
        ["Idioma não suportado."] = "Unsupported language.",
        ["Informe a empresa do usuário."] = "Select the user's company.",
        ["Informe a senha atual."] = "Enter the current password.",
        ["Informe a senha."] = "Enter the password.",
        ["Informe o e-mail."] = "Enter the e-mail.",
        ["Já existe um usuário com este e-mail."] = "A user with this e-mail already exists.",
        ["Não autenticado."] = "Not authenticated.",
        ["Não é possível adicionar usuários a uma empresa desativada."] = "Users cannot be added to a deactivated company.",
        ["Não é possível converter entre perfil de plataforma e perfil de empresa."] = "A platform role cannot be converted into a company role (or vice versa).",
        ["Perfil inválido."] = "Invalid role.",
        ["Senha atual incorreta."] = "Incorrect current password.",
        ["Sessão expirada. Faça login novamente."] = "Session expired. Please sign in again.",
        ["Usuário desativado."] = "User deactivated.",
        ["Você não pode alterar o próprio perfil."] = "You cannot change your own role.",
        ["Você não pode desativar o próprio usuário."] = "You cannot deactivate your own user.",

        // ----- Empresas -----
        ["A empresa está desativada."] = "The company is deactivated.",
        ["Documento deve ser um CPF (11 dígitos) ou CNPJ (14 dígitos)."] = "Document must be a CPF (11 digits) or CNPJ (14 digits).",
        ["Informe a empresa."] = "Select the company.",
        ["Informe o nome da empresa."] = "Enter the company name.",
        ["Já existe uma empresa com este documento."] = "A company with this document already exists.",
        ["Operação entre empresas não permitida."] = "Cross-company operations are not allowed.",
        ["Retenção deve estar entre 1 e 3650 dias."] = "Retention must be between 1 and 3650 days.",
        ["Site deve ser uma URL http(s) válida."] = "Website must be a valid http(s) URL.",
        ["Telefone deve ter entre 8 e 15 dígitos."] = "Phone must have between 8 and 15 digits.",

        // ----- Agentes -----
        ["Configure o prompt do agente antes de ativá-lo."] = "Configure the agent's prompt before activating it.",
        ["Descreva o objetivo do agente."] = "Describe the agent's goal.",
        ["Escolha o agente."] = "Select the agent.",
        ["Informe a empresa do agente."] = "Select the agent's company.",
        ["Informe o nome do agente."] = "Enter the agent name.",
        ["Limite de resposta deve estar entre 50 e 4000 tokens."] = "Response limit must be between 50 and 4000 tokens.",
        ["Modelo não disponível."] = "Model not available.",
        ["Máximo de 20 regras de transferência."] = "Maximum of 20 handoff rules.",
        ["Máximo de 30 regras."] = "Maximum of 30 rules.",
        ["Máximo de 50 perguntas frequentes."] = "Maximum of 50 FAQs.",
        ["O agente pertence a outra empresa."] = "The agent belongs to another company.",
        ["Pergunta vazia."] = "Empty question.",
        ["Resposta vazia."] = "Empty answer.",
        ["Temperatura deve estar entre 0 e 1."] = "Temperature must be between 0 and 1.",

        // ----- IA -----
        ["A chave de API configurada foi recusada pelo provedor de IA."] = "The configured API key was rejected by the AI provider.",
        ["Nenhuma chave de API de IA está configurada. Use o modo demonstrativo ou configure OPENAI_API_KEY."] = "No AI API key is configured. Use demo mode or set OPENAI_API_KEY.",
        ["Não foi possível conectar ao provedor de IA."] = "Could not connect to the AI provider.",
        ["O limite diário de uso de IA da empresa foi atingido. Tente novamente amanhã."] = "The company's daily AI usage limit has been reached. Please try again tomorrow.",
        ["O provedor de IA demorou para responder. Tente novamente."] = "The AI provider took too long to respond. Please try again.",
        ["O provedor de IA está indisponível no momento."] = "The AI provider is currently unavailable.",
        ["O provedor de IA está limitando as requisições. Aguarde alguns segundos."] = "The AI provider is rate limiting requests. Please wait a few seconds.",
        ["O provedor de IA recusou a requisição. Verifique o modelo configurado no agente."] = "The AI provider rejected the request. Check the model configured for the agent.",
        ["O provedor de IA retornou uma resposta vazia. Tente reformular a mensagem."] = "The AI provider returned an empty response. Try rephrasing the message.",
        ["Os créditos da conta do provedor de IA se esgotaram."] = "The AI provider account has run out of credits.",

        // ----- Conversas, Web Chat e mensagens -----
        ["A conversa está encerrada. Reabra para continuar."] = "The conversation is closed. Reopen it to continue.",
        ["A conversa não está com a equipe."] = "The conversation is not with the team.",
        ["A mensagem está vazia."] = "The message is empty.",
        ["A mensagem excede o limite de {0} caracteres."] = "The message exceeds the {0}-character limit.",
        ["A mensagem excede {0} caracteres."] = "The message exceeds {0} characters.",
        ["Chat indisponível no momento."] = "Chat unavailable at the moment.",
        ["Esta conversa foi encerrada. Inicie uma nova conversa."] = "This conversation was closed. Start a new conversation.",
        ["Esta sessão foi encerrada. Inicie uma nova conversa."] = "This session was closed. Start a new conversation.",
        ["Este site não está autorizado a usar este chat."] = "This website is not authorized to use this chat.",
        ["Limite de mensagens desta conversa atingido."] = "Message limit for this conversation reached.",
        ["Só é possível transferir conversas que estão com a IA."] = "Only conversations handled by the AI can be transferred.",

        // ----- Canais -----
        ["Cor deve estar no formato #RRGGBB."] = "Color must use the #RRGGBB format.",
        ["Domínio inválido. Use o formato https://www.seusite.com.br"] = "Invalid domain. Use the format https://www.yoursite.com",
        ["Informe o nome do canal."] = "Enter the channel name.",
        ["Informe o título do chat."] = "Enter the chat title.",
        ["Máximo de 20 domínios."] = "Maximum of 20 domains.",
        ["Posição deve ser 'right' ou 'left'."] = "Position must be 'right' or 'left'.",

        // ----- WhatsApp -----
        ["Configure o access token e o App Secret antes de ativar o canal do WhatsApp."] = "Set the access token and App Secret before activating the WhatsApp channel.",
        ["Este número de WhatsApp já está conectado a outro canal."] = "This WhatsApp number is already connected to another channel.",
        ["A janela de 24 horas do WhatsApp expirou: aguarde uma nova mensagem do cliente para responder."] = "The WhatsApp 24-hour window has expired: wait for a new customer message before replying.",
        ["Não foi possível enviar a mensagem pelo WhatsApp. Verifique as credenciais do canal."] = "The message could not be sent via WhatsApp. Check the channel credentials.",
        ["Informe o Phone Number ID do WhatsApp."] = "Enter the WhatsApp Phone Number ID.",
        ["O Phone Number ID deve conter apenas números."] = "The Phone Number ID must contain digits only.",
        ["O WhatsApp Business Account ID deve conter apenas números."] = "The WhatsApp Business Account ID must contain digits only.",
        ["O access token não pode conter espaços."] = "The access token cannot contain spaces.",
        ["O App Secret deve ter de 16 a 128 letras e números."] = "The App Secret must have 16 to 128 letters and digits.",

        // ----- Base de conhecimento -----
        ["Digite o que deseja buscar."] = "Type what you want to search for.",
        ["Este documento não possui arquivo original."] = "This document has no original file.",
        ["Formato não suportado. Envie arquivos PDF, TXT ou DOCX."] = "Unsupported format. Upload PDF, TXT or DOCX files.",
        ["Formato não suportado."] = "Unsupported format.",
        ["Informe o conteúdo."] = "Enter the content.",
        ["Informe o título."] = "Enter the title.",
        ["O PDF tem {0} páginas; o limite é {1}."] = "The PDF has {0} pages; the limit is {1}.",
        ["O arquivo está vazio."] = "The file is empty.",
        ["O arquivo excede o limite de {0} MB."] = "The file exceeds the {0} MB limit.",
        ["O arquivo original não foi encontrado."] = "The original file was not found.",
        ["O conteúdo do DOCX é grande demais."] = "The DOCX content is too large.",
        ["Selecione um arquivo."] = "Select a file.",
        ["Use o envio de arquivo para PDF, TXT ou DOCX."] = "Use file upload for PDF, TXT or DOCX.",

        // ----- Contatos -----
        ["Contatos anonimizados não podem ser editados."] = "Anonymized contacts cannot be edited.",
        ["Informe o nome."] = "Enter the name.",
        ["Já existe um contato com este e-mail."] = "A contact with this e-mail already exists.",
        ["Já existe um contato com este telefone."] = "A contact with this phone already exists.",
        ["O contato pertence a outra empresa."] = "The contact belongs to another company.",
        ["Status inválido."] = "Invalid status.",
        ["Transição de status não permitida."] = "Status change not allowed.",

        // ----- Agendamentos -----
        ["A agenda está aberta para os próximos {0} dias."] = "The schedule is open for the next {0} days.",
        ["Agendamentos precisam de pelo menos {0} minutos de antecedência."] = "Appointments require at least {0} minutes' notice.",
        ["Dia da semana inválido."] = "Invalid weekday.",
        ["Duração entre 5 minutos e 8 horas."] = "Duration must be between 5 minutes and 8 hours.",
        ["Duração inválida."] = "Invalid duration.",
        ["Este horário já está ocupado. Escolha outro horário."] = "This time is already taken. Choose another time.",
        ["Fuso horário inválido."] = "Invalid time zone.",
        ["Horário fora do expediente."] = "Time outside business hours.",
        ["Horários inválidos: use HH:mm, início antes do fim e sem sobreposição."] = "Invalid hours: use HH:mm, start before end and no overlaps.",
        ["Há serviços com nomes repetidos."] = "There are services with duplicate names.",
        ["Informe a duração ou escolha um serviço cadastrado."] = "Enter the duration or choose a registered service.",
        ["Informe o nome do serviço."] = "Enter the service name.",
        ["Informe o serviço."] = "Select the service.",
        ["Intervalo deve ser 10, 15, 20, 30, 45 ou 60 minutos."] = "Interval must be 10, 15, 20, 30, 45 or 60 minutes.",
        ["Máximo de 50 serviços."] = "Maximum of 50 services.",
        ["Serviço não encontrado na agenda."] = "Service not found in the schedule.",
        ["Só é possível reagendar agendamentos ativos."] = "Only active appointments can be rescheduled.",

        // ----- Automações -----
        ["Esta ação não está disponível para o gatilho escolhido."] = "This action is not available for the selected trigger.",
        ["Informe o nome da automação."] = "Enter the automation name.",

        // ----- Relatórios -----
        ["A data inicial deve ser anterior à final."] = "The start date must be before the end date.",
        ["Consulte no máximo 36 meses."] = "Query at most 36 months.",
        ["Consulte no máximo {0} dias."] = "Query at most {0} days.",
        ["Informe o mês no formato AAAA-MM."] = "Enter the month in YYYY-MM format.",
        ["Informe um valor válido."] = "Enter a valid amount.",
        ["O mês inicial deve ser anterior ao final."] = "The start month must be before the end month."
    };

    /// <summary>Nomes de entidades usados em "{0} não encontrado(a).".</summary>
    public static readonly IReadOnlyDictionary<string, string> EnglishEntities = new Dictionary<string, string>
    {
        ["Agendamento"] = "Appointment", ["Agente"] = "Agent", ["Automação"] = "Automation", ["Canal"] = "Channel",
        ["Contato"] = "Contact", ["Conversa"] = "Conversation", ["Documento"] = "Document", ["Empresa"] = "Company",
        ["Lançamento"] = "Billing entry", ["Notificação"] = "Notification", ["Sessão"] = "Session", ["Usuário"] = "User",
        ["Versão"] = "Version"
    };

    private static readonly IReadOnlyList<(Regex Pattern, string Template)> Parameterized = English
        .Where(e => e.Key.Contains("{0}"))
        // Mais texto fixo primeiro: "excede o limite de {0}" antes de "excede {0}".
        .OrderByDescending(e => Regex.Replace(e.Key, @"\{\d+}", "").Length)
        .Select(e => (new Regex("^" + Regex.Replace(Regex.Escape(e.Key), @"\\\{(\d+)}", m => $"(?<p{m.Groups[1].Value}>.+?)") + "$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled), e.Value))
        .ToList();

    /// <summary>Traduz para o idioma da cultura informada; sem tradução conhecida, devolve o texto original.</summary>
    public static string Translate(string message, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(message) || culture.TwoLetterISOLanguageName != "en") return message;
        if (English.TryGetValue(message, out var exact)) return exact;

        foreach (var (pattern, template) in Parameterized)
        {
            var match = pattern.Match(message);
            if (!match.Success) continue;
            return Regex.Replace(template, @"\{(\d+)}", m =>
            {
                var value = match.Groups["p" + m.Groups[1].Value].Value;
                return EnglishEntities.GetValueOrDefault(value, value);
            });
        }
        return message;
    }
}

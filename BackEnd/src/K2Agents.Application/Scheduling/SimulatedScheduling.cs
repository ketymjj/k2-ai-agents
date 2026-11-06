using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using K2Agents.Application.Contacts;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Scheduling;

/// <summary>
/// Agendamento no modo demonstrativo (sem IA): oferece horários REAIS da agenda e agenda pelo número escolhido.
/// O texto é determinístico (marcado como simulado); a reserva passa pelas mesmas validações das ferramentas.
/// </summary>
public static partial class SimulatedScheduling
{
    private const string OfferMarkerPt = "Responda com o número da opção para agendar.";
    private const string OfferMarkerEn = "Reply with the option number to book.";

    public static async Task<string?> TryReplyAsync(
        AgentReplyRequest r, string message, SchedulingSettings settings, IAgentTools tools, AgentToolContext ctx, CancellationToken ct)
    {
        var en = r.Agent.ResponseLanguage == "en";
        var normalized = Normalize(message);

        // Escolha de uma opção oferecida anteriormente ("2", "opção 2").
        if (ChoicePattern().Match(normalized) is { Success: true } choice)
        {
            var offer = r.History.Where(m => m.SenderType == SenderType.AI).TakeLast(6).Reverse()
                .FirstOrDefault(m => m.Content.Contains(OfferMarkerPt) || m.Content.Contains(OfferMarkerEn));
            if (offer is null) return null;
            return await BookChoiceAsync(offer.Content, int.Parse(choice.Groups[1].Value), settings, tools, ctx, en, ct);
        }

        if (!IntentPattern().IsMatch(normalized)) return null;

        var service = settings.Services.FirstOrDefault(s => normalized.Contains(Normalize(s.Name))) ?? settings.Services[0];
        var slots = await tools.NextSlotsAsync(r.Agent.CompanyId, service, 3, ct);
        if (slots.Count == 0)
            return en ? $"I couldn't find free times for {service.Name} in the coming days. An attendant can help you."
                      : $"Não encontrei horários livres para {service.Name} nos próximos dias. Um atendente pode ajudar você.";

        var zone = Availability.Zone(settings);
        var culture = CultureInfo.GetCultureInfo(en ? "en-US" : "pt-BR");
        var sb = new StringBuilder();
        sb.AppendLine(en ? $"Available times for {service.Name} (checked live in the schedule):" : $"Horários disponíveis para {service.Name} (consultados agora na agenda):");
        for (var i = 0; i < slots.Count; i++)
        {
            var local = Availability.ToLocal(slots[i].Start, zone);
            sb.AppendLine($"{i + 1}) {local.ToString("ddd", culture).TrimEnd('.')} {local:dd/MM} {(en ? "at" : "às")} {local:HH:mm}");
        }
        sb.Append(en ? OfferMarkerEn : OfferMarkerPt);
        return sb.ToString();
    }

    private static async Task<string> BookChoiceAsync(
        string offer, int option, SchedulingSettings settings, IAgentTools tools, AgentToolContext ctx, bool en, CancellationToken ct)
    {
        var service = HeaderPattern().Match(offer) is { Success: true } h ? h.Groups["service"].Value.Trim() : null;
        var line = OptionPattern().Matches(offer).FirstOrDefault(m => m.Groups["n"].Value == option.ToString());
        if (service is null || line is null)
            return en ? "That option is not available. Please choose one of the listed numbers." : "Essa opção não existe. Escolha um dos números listados.";

        var zone = Availability.Zone(settings);
        var today = Availability.ToLocal(DateTime.UtcNow, zone).Date;
        var date = DateTime.ParseExact($"{line.Groups["d"].Value}/{today.Year} {line.Groups["t"].Value}", "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        if (date.Date < today) date = date.AddYears(1);

        // Nome só se o próprio cliente escreveu ("meu nome é ..."); sem nome e sem contato, pede antes de agendar.
        var name = ctx.CustomerTexts.Select(t => ContactExtractor.Extract(t).Name).LastOrDefault(n => n is not null);
        var (ok, msg, _) = await tools.BookAsync(ctx, service, date, name, null, ct);
        if (!ok && msg.StartsWith("Pergunte o nome"))
            return en ? "To book, please tell me your name (e.g. \"My name is Ana\") and then reply with the option number again."
                      : "Para agendar, me diga seu nome (por exemplo: \"Meu nome é Ana\") e depois responda novamente com o número da opção.";
        if (!ok)
            return en ? $"I couldn't book that time: {msg} Ask me for available times again." : $"Não consegui agendar esse horário: {msg} Peça os horários disponíveis novamente.";

        var when = en ? $"{date:MM/dd} at {date:HH:mm}" : $"{date:dd/MM} às {date:HH:mm}";
        if (ctx.IsPlayground)
            return en ? $"[Test] {service} on {when} is free — in a real channel it would be booked now."
                      : $"[Teste] {service} em {when} está livre — num canal real, o agendamento seria registrado agora.";
        return en ? $"Booked! {service} on {when}. Our team may contact you to confirm."
                  : $"Agendado! {service} em {when}. Nossa equipe pode entrar em contato para confirmar.";
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    // Intenção de agendar — não basta citar "horário" ("qual o horário de funcionamento?" não é pedido de agenda).
    [GeneratedRegex(@"\b(agendar|agendamento|marcar|reservar|book|booking|schedule|appointment)\b|\bhorarios? (livres?|disponive(l|is))\b|\btem (horario|vaga)\b|\bavailable (times|slots)\b")]
    private static partial Regex IntentPattern();

    [GeneratedRegex(@"^\s*(?:opcao|option|numero|number|n)?\s*([1-9])\s*[.!]?\s*$")]
    private static partial Regex ChoicePattern();

    [GeneratedRegex(@"(?:para|for) (?<service>.+?) \(")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?<n>\d)\) \S+ (?<d>\d{2}/\d{2}) (?:às|at) (?<t>\d{2}:\d{2})", RegexOptions.Multiline)]
    private static partial Regex OptionPattern();
}

using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace K2Agents.Api.Localization;

/// <summary>
/// Idioma da resposta a partir do Accept-Language (pt-BR padrão, en suportado). Altera apenas a CurrentUICulture
/// (mensagens, inclusive as padrão do FluentValidation); a CurrentCulture, usada para ler números e datas, não muda.
/// </summary>
public static class RequestLanguage
{
    public static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static CultureInfo From(HttpContext context)
    {
        var header = context.Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header)) return Portuguese;
        if (!StringWithQualityHeaderValue.TryParseList(header.Split(','), out var values)) return Portuguese;
        foreach (var value in values.OrderByDescending(v => v.Quality ?? 1))
        {
            var tag = value.Value.Value ?? string.Empty;
            if (tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return Portuguese;
            if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        }
        return Portuguese;
    }

    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            CultureInfo.CurrentUICulture = From(context);
            await next(context);
        });
}

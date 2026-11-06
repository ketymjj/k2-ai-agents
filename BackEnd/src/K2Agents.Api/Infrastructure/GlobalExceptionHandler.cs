using FluentValidation;
using K2Agents.Api.Localization;
using K2Agents.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Infrastructure;

/// <summary>Converte exceções em ProblemDetails. Erros inesperados nunca expõem detalhes internos ao cliente.</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        // O idioma vem do cabeçalho (a cultura definida mais adiante no pipeline não "volta" até aqui).
        var culture = RequestLanguage.From(context);
        string T(string message) => MessageCatalog.Translate(message, culture);

        switch (exception)
        {
            case ValidationException validation:
                problem = new ValidationProblemDetails(
                    validation.Errors.GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => T(e.ErrorMessage)).ToArray()))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = T("Dados inválidos.")
                };
                break;
            case AppException app:
                problem = new ProblemDetails { Status = app.StatusCode, Title = T(app.Message) };
                if (app.Code is not null) problem.Extensions["code"] = app.Code;
                break;
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return true;
            default:
                logger.LogError(exception, "Erro não tratado em {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = T("Ocorreu um erro inesperado. Tente novamente.")
                };
                break;
        }

        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? 500;
        // Tipo concreto: preserva o campo "errors" de ValidationProblemDetails.
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        return true;
    }
}

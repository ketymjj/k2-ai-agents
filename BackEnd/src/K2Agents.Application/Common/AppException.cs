namespace K2Agents.Application.Common;

/// <summary>Erro de negócio esperado; o middleware global converte em ProblemDetails com o status indicado.</summary>
public class AppException(string message, int statusCode = 400, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;

    public static AppException NotFound(string what) => new($"{what} não encontrado(a).", 404, "not_found");
    public static AppException Forbidden(string? message = null) => new(message ?? "Acesso negado.", 403, "forbidden");
    public static AppException Conflict(string message) => new(message, 409, "conflict");
}

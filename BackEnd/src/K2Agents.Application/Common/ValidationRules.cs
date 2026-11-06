using FluentValidation;

namespace K2Agents.Application.Common;

public static class ValidationRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Informe a senha.")
            .MinimumLength(8).WithMessage("A senha deve ter ao menos 8 caracteres.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("A senha deve conter uma letra maiúscula.")
            .Matches("[a-z]").WithMessage("A senha deve conter uma letra minúscula.")
            .Matches("[0-9]").WithMessage("A senha deve conter um número.");

    public static readonly string[] SupportedLanguages = ["pt-BR", "en"];

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string? DigitsOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsDigit).ToArray());

    public static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

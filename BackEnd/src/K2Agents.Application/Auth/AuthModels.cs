using FluentValidation;
using K2Agents.Application.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Auth;

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Atualização parcial: campos nulos não mudam.</summary>
public record UpdatePreferencesRequest(string? PreferredLanguage, bool? BeginnerMode = null);

public record AuthUserDto(
    Guid Id, string Name, string Email, UserRole Role, Guid? CompanyId, string? CompanyName, string PreferredLanguage, bool BeginnerMode);

/// <summary>Resultado interno: o refresh token bruto vai para cookie HttpOnly, nunca para o corpo da resposta.</summary>
public record AuthResult(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, AuthUserDto User);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, AuthUserDto User);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(160);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha.").MaximumLength(128);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Informe a senha atual.");
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("A nova senha deve ser diferente da atual.");
    }
}

public class UpdatePreferencesRequestValidator : AbstractValidator<UpdatePreferencesRequest>
{
    public UpdatePreferencesRequestValidator()
    {
        RuleFor(x => x.PreferredLanguage).Must(l => l is null || ValidationRules.SupportedLanguages.Contains(l))
            .WithMessage("Idioma não suportado.");
    }
}

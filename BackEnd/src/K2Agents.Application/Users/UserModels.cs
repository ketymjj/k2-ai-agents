using FluentValidation;
using K2Agents.Application.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Users;

public record UserDto(
    Guid Id, string Name, string Email, UserRole Role, Guid? CompanyId, string? CompanyName,
    bool IsActive, string PreferredLanguage, DateTime? LastLoginAt, bool IsLocked, DateTime CreatedAt);

public record RoleDto(UserRole Id, string Name, string Description);

public record CreateUserRequest(
    string Name, string Email, string Password, UserRole Role, Guid? CompanyId, string? PreferredLanguage);

/// <summary>Password opcional: quando informada, redefine a senha do usuário.</summary>
public record UpdateUserRequest(string Name, string Email, UserRole Role, string? Password, string? PreferredLanguage);

public record SetUserActiveRequest(bool IsActive);

public record UserQuery : PagedQuery
{
    public Guid? CompanyId { get; init; }
    public string? Search { get; init; }
    public UserRole? Role { get; init; }
    public bool? IsActive { get; init; }
}

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(160);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Role).IsInEnum().WithMessage("Perfil inválido.");
        RuleFor(x => x.PreferredLanguage).Must(l => l is null || ValidationRules.SupportedLanguages.Contains(l))
            .WithMessage("Idioma não suportado.");
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(160);
        RuleFor(x => x.Role).IsInEnum().WithMessage("Perfil inválido.");
        RuleFor(x => x.Password!).StrongPassword().When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.PreferredLanguage).Must(l => l is null || ValidationRules.SupportedLanguages.Contains(l))
            .WithMessage("Idioma não suportado.");
    }
}

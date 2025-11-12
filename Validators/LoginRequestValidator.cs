using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio")
            .EmailAddress()
            .WithMessage("El formato del correo electrónico no es válido")
            .MaximumLength(255)
            .WithMessage("El correo electrónico no puede exceder 255 caracteres");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("La contraseña es obligatoria")
            .MinimumLength(8)
            .WithMessage("La contraseña debe tener al menos 8 caracteres")
            .MaximumLength(128)
            .WithMessage("La contraseña no puede exceder 128 caracteres");
    }
}


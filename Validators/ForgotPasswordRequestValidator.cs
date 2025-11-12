using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio")
            .EmailAddress()
            .WithMessage("El formato del correo electrónico no es válido")
            .MaximumLength(255)
            .WithMessage("El correo electrónico no puede exceder 255 caracteres");
    }
}


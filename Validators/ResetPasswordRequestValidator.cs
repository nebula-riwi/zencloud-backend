using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio")
            .EmailAddress()
            .WithMessage("El formato del correo electrónico no es válido")
            .MaximumLength(255)
            .WithMessage("El correo electrónico no puede exceder 255 caracteres");

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("El token de restablecimiento es obligatorio")
            .MaximumLength(500)
            .WithMessage("El token no puede exceder 500 caracteres");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("La nueva contraseña es obligatoria")
            .MinimumLength(8)
            .WithMessage("La contraseña debe tener al menos 8 caracteres")
            .MaximumLength(128)
            .WithMessage("La contraseña no puede exceder 128 caracteres")
            .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).*$")
            .WithMessage("La contraseña debe contener al menos una letra minúscula, una letra mayúscula, un número y un carácter especial");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("La confirmación de la contraseña es obligatoria")
            .Equal(x => x.NewPassword)
            .WithMessage("La contraseña y la confirmación de la contraseña no coinciden");
    }
}


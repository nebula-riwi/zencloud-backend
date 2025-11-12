using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
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
            .WithMessage("La contraseña no puede exceder 128 caracteres")
            .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).*$")
            .WithMessage("La contraseña debe contener al menos una letra minúscula, una letra mayúscula, un número y un carácter especial");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("La confirmación de la contraseña es obligatoria")
            .Equal(x => x.Password)
            .WithMessage("La contraseña y la confirmación de la contraseña no coinciden");

        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("El nombre completo es obligatorio")
            .MinimumLength(2)
            .WithMessage("El nombre completo debe tener al menos 2 caracteres")
            .MaximumLength(200)
            .WithMessage("El nombre completo no puede exceder 200 caracteres")
            .Matches(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑ\s]+$")
            .WithMessage("El nombre completo solo puede contener letras y espacios");
    }
}


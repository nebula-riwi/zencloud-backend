using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class CreateDatabaseRequestDtoValidator : AbstractValidator<CreateDatabaseRequestDto>
{
    public CreateDatabaseRequestDtoValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("El UserId es requerido")
            .NotEqual(Guid.Empty)
            .WithMessage("El UserId no puede ser un GUID vacío");

        RuleFor(x => x.EngineId)
            .NotEmpty()
            .WithMessage("El EngineId es requerido")
            .NotEqual(Guid.Empty)
            .WithMessage("El EngineId no puede ser un GUID vacío");
    }
}


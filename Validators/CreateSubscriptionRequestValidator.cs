using FluentValidation;
using ZenCloud.DTOs;

namespace ZenCloud.Validators;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("El UserId es requerido")
            .NotEqual(Guid.Empty)
            .WithMessage("El UserId no puede ser un GUID vacío");

        RuleFor(x => x.PlanId)
            .GreaterThan(0)
            .WithMessage("El PlanId debe ser mayor que 0");
    }
}


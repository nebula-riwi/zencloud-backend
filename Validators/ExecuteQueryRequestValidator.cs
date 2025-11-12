using FluentValidation;
using ZenCloud.DTOs.DatabaseManagement;

namespace ZenCloud.Validators;

public class ExecuteQueryRequestValidator : AbstractValidator<ExecuteQueryRequest>
{
    public ExecuteQueryRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("La consulta SQL es requerida")
            .MaximumLength(5000)
            .WithMessage("La consulta SQL no puede exceder 5,000 caracteres")
            .Must(BeValidSqlQuery)
            .WithMessage("La consulta SQL contiene caracteres o comandos no permitidos");
    }

    private bool BeValidSqlQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        // Validar que no contenga comandos peligrosos
        var dangerousCommands = new[]
        {
            "DROP DATABASE",
            "DROP TABLE",
            "TRUNCATE",
            "CREATE USER",
            "GRANT ALL",
            "REVOKE ALL",
            "FLUSH PRIVILEGES",
            "SHUTDOWN",
            "KILL"
        };

        var upperQuery = query.ToUpperInvariant();
        return !dangerousCommands.Any(cmd => upperQuery.Contains(cmd));
    }
}


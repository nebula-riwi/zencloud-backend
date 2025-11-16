using System.ComponentModel.DataAnnotations;
namespace ZenCloud.DTOs;

public class CreateDatabaseRequestDto
{
    [Required(ErrorMessage = "El UserId es Requerido")]
    public Guid UserId { get; set; }
    [Required(ErrorMessage = "El EngineId es Requerido")]
    public Guid EngineId { get; set; }
    [MaxLength(100, ErrorMessage = "El nombre de la base de datos no puede exceder 100 caracteres")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "El nombre solo puede contener letras, números, guiones y guiones bajos")]
    public string? DatabaseName { get; set; }
}
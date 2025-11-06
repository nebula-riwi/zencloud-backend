using System.ComponentModel.DataAnnotations;
namespace ZenCloud.DTOs;

public class CreateDatabaseRequestDto
{
    [Required(ErrorMessage = "El UserId es Requerido")]
    public Guid UserId { get; set; }
    [Required(ErrorMessage = "El EngineId es Requerido")]
    public Guid EngineId { get; set; }
}
using System.ComponentModel.DataAnnotations;

namespace ZenCloud.DTOs;

public class DatabaseInstaceDto
{
    [Required(ErrorMessage = "El UserId es Requerido")]
    public Guid UserId { get; set; }
    [Required(ErrorMessage = "El EngineId es Requerido")]
    public Guid EngineId { get; set; }
}

public class DatabaseInstanceResponseDto
{
    public Guid InstanceId { get; set; }
    public string DatabaseName { get; set; } = null!;
    public string DatabaseUser { get; set; } = null!;
    public int AssignedPort { get; set; }
    public string ConnectionString { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string EngineName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
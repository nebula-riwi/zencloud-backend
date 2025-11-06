namespace ZenCloud.DTOs;

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
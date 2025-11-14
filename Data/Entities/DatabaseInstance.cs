namespace ZenCloud.Data.Entities;

public class DatabaseInstance
{
    public Guid InstanceId  { get; set; }
    public Guid UserId  { get; set; }
    public Guid EngineId  { get; set; }
    public string DatabaseName { get; set; } = null!;
    public string DatabaseUser { get; set; } = null!;
    public string DatabasePasswordHash { get; set; } = null!;
    public int AssignedPort { get; set; }
    public string ConnectionString  { get; set; } = null!;
    public DatabaseInstanceStatus Status { get; set; } = DatabaseInstanceStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public string ServerIpAddress { get; set; } = null!;

    // Navigation properties
    public User? User { get; set; }
    public DatabaseEngine? Engine { get; set; }
}
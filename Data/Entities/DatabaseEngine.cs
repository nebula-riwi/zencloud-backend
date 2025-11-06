namespace ZenCloud.Data.Entities;

public class DatabaseEngine
{
    public Guid EngineId { get; set; }
    public DatabaseEngineType EngineName { get; set; }
    public int DefaultPort { get; set; }
    public bool IsActive { get; set; } = true;
    public string? IconUrl { get; set; }
    public string? Description { get; set; }
    
    // Navegation property
    public ICollection<DatabaseInstance> DatabaseInstances { get; set; } = new List<DatabaseInstance>();
}
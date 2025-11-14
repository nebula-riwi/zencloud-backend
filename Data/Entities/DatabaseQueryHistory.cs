namespace ZenCloud.Data.Entities;

public class DatabaseQueryHistory
{
    public Guid QueryHistoryId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstanceId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public int? RowCount { get; set; }
    public double ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public DatabaseEngineType? EngineType { get; set; }

    public User User { get; set; } = null!;
    public DatabaseInstance Instance { get; set; } = null!;
}


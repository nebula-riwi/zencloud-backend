namespace ZenCloud.DTOs.DatabaseManagement;

public class QueryHistoryItemDto
{
    public Guid Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? RowCount { get; set; }
    public double ExecutionTimeMs { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
}


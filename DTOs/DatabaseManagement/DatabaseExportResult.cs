namespace ZenCloud.DTOs.DatabaseManagement;

public class DatabaseExportResult
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
    public string ContentType { get; init; } = "application/sql";
}


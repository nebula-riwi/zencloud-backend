namespace ZenCloud.Data.Entities;

public class ErrorLog
{
    public Guid ErrorId { get; set; }
    public string ErrorMessage { get; set; } = null!;
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public Guid? UserId { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? IpAddress { get; set; }
    public ErrorSeverity Severity { get; set; }
    public bool IsNotified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
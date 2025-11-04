namespace ZenCloud.Data.Entities;

public class AuditLog
{
    public Guid AuditId { get; set; }
    public Guid? UserId { get; set; }
    public AuditAction Action { get; set; }
    public AuditEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }

}
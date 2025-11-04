namespace ZenCloud.Data.Entities;

public class EmailLog
{
    public Guid EmailLogId { get; set; }
    public Guid? UserId { get; set; }
    public EmailType EmailType { get; set; }
    public string RecipientEmail { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
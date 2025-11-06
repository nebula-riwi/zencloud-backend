namespace ZenCloud.Data.Entities;

public class WebhookConfiguration
{
    public Guid WebhookId { get; set; }
    public Guid UserId { get; set; }
    public string WebhookUrl { get; set; } = null!;
    public WebhookEventType EventType { get; set; }
    public bool IsActive { get; set; } = true;
    public string SecretToken { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
    public ICollection<WebhookLog> WebhookLogs { get; set; } = new List<WebhookLog>();
}
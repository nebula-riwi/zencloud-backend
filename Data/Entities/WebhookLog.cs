namespace ZenCloud.Data.Entities;

public class WebhookLog
{
    public Guid WebhookLogId { get; set; }
    public Guid WebhookId { get; set; }
    public WebhookEventType EventType { get; set; }
    public string PayloadJson { get; set; } = null!;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public WebhookLogStatus Status { get; set; }
    public int AttemptCount { get; set; } = 1;
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public WebhookConfiguration WebhookConfiguration { get; set; } = null!;
}
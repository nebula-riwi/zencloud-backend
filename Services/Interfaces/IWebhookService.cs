using ZenCloud.Data.Entities;

namespace ZenCloud.Services.Interfaces;

public interface IWebhookService
{
    Task<WebhookConfiguration> CreateWebhookAsync(Guid userId, string name, string webhookUrl, WebhookEventType eventType);
    Task<WebhookConfiguration?> GetWebhookByIdAsync(Guid webhookId);
    Task<IEnumerable<WebhookConfiguration>> GetUserWebhooksAsync(Guid userId);
    Task<WebhookConfiguration> UpdateWebhookAsync(Guid webhookId, Guid userId, string? webhookUrl = null, WebhookEventType? eventType = null, bool? isActive = null, string? name = null);
    Task<bool> DeleteWebhookAsync(Guid webhookId, Guid userId);
    Task TriggerWebhookAsync(WebhookEventType eventType, object payload, Guid? userId = null);
}


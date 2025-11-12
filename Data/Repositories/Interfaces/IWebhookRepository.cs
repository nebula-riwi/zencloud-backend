using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface IWebhookRepository : IRepository<WebhookConfiguration>
{
    Task<IEnumerable<WebhookConfiguration>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<WebhookConfiguration>> GetActiveByUserIdAsync(Guid userId);
    Task<IEnumerable<WebhookConfiguration>> GetByEventTypeAsync(WebhookEventType eventType);
}


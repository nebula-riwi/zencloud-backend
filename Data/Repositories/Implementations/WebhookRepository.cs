using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class WebhookRepository : Repository<WebhookConfiguration>, IWebhookRepository
{
    private readonly PgDbContext _context;

    public WebhookRepository(PgDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<WebhookConfiguration>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WebhookConfiguration>> GetActiveByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(w => w.UserId == userId && w.IsActive)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WebhookConfiguration>> GetByEventTypeAsync(WebhookEventType eventType)
    {
        return await _dbSet
            .Where(w => w.EventType == eventType && w.IsActive)
            .ToListAsync();
    }
}


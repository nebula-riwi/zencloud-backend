using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class SubscriptionRepository : Repository<Subscription>, ISubscriptionRepository
{
    private readonly PgDbContext _context;

    public SubscriptionRepository(PgDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Subscription?> GetActiveByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Include(s => s.Plan)
            .Include(s => s.User)
            .Where(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Subscription?> GetByIdAsync(Guid subscriptionId)
    {
        return await _context.Subscriptions
            .Include(s => s.Plan)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);
    }

    public async Task<IEnumerable<Subscription>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Include(s => s.Plan)
            .Include(s => s.User)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<Subscription?> GetByMercadoPagoIdAsync(string mercadoPagoSubscriptionId)
    {
        return await _dbSet
            .Include(s => s.Plan)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.MercadoPagoSubscriptionId == mercadoPagoSubscriptionId);
    }

    public async Task AddAsync(Subscription subscription)
    {
        await _context.Subscriptions.AddAsync(subscription);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Subscription subscription)
    {
        _context.Subscriptions.Update(subscription);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid subscriptionId)
    {
        var subscription = await GetByIdAsync(subscriptionId);
        if (subscription == null) return false;

        _context.Subscriptions.Remove(subscription);
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task<IEnumerable<Subscription>> GetSubscriptionsExpiringInDaysAsync(int days)
    {
        var targetDate = DateTime.UtcNow.AddDays(days);
            
        return await _dbSet
            .Include(s => s.User)
            .Include(s => s.Plan)
            .Where(s => s.IsActive && 
                        s.EndDate.Date == targetDate.Date && 
                        s.PaymentStatus == PaymentStatus.Paid)
            .ToListAsync();
    }

    public async Task<IEnumerable<Subscription>> GetExpiredSubscriptionsAsync()
    {
        return await _dbSet
            .Include(s => s.User)
            .Include(s => s.Plan)
            .Where(s => s.IsActive && 
                        s.EndDate < DateTime.UtcNow && 
                        s.PaymentStatus == PaymentStatus.Paid)
            .ToListAsync();
    }
}
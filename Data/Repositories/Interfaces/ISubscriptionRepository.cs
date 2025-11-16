using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface ISubscriptionRepository : IRepository<Subscription>
{
    Task<Subscription?> GetActiveByUserIdAsync(Guid userId);
    Task<Subscription?> GetByIdAsync(Guid subscriptionId);
    Task<IEnumerable<Subscription>> GetByUserIdAsync(Guid userId);
    Task<Subscription?> GetByMercadoPagoIdAsync(string mercadoPagoSubscriptionId);
    Task<IEnumerable<Subscription>> GetSubscriptionsExpiringInDaysAsync(int days, int skip = 0, int take = 100);
    Task<IEnumerable<Subscription>> GetExpiredSubscriptionsAsync(int skip = 0, int take = 100);
    Task<int> CountSubscriptionsExpiringInDaysAsync(int days);
    Task<int> CountExpiredSubscriptionsAsync();
    Task AddAsync(Subscription subscription);
    Task UpdateAsync(Subscription subscription);
    Task<bool> DeleteAsync(Guid subscriptionId);
}
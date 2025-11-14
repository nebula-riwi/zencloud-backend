using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces
{
    public interface IPaymentRepository : IRepository<Payment>
    {
        Task<Payment?> GetByMercadoPagoIdAsync(string mercadoPagoId);
        Task<Payment?> GetByIdAsync(Guid paymentId);
        Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId);
        Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatusType status);
        Task<Payment?> GetLastApprovedBySubscriptionAsync(Guid subscriptionId);
        Task<Payment?> GetLastApprovedByUserAsync(Guid userId);
        Task AddAsync(Payment payment);
        Task UpdateAsync(Payment payment);
        Task<bool> DeleteAsync(Guid paymentId);
    }
}
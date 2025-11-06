using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces
{
    public interface IPaymentRepository
    {
        Task AddAsync(Payment payment);
        Task<Payment?> GetByMercadoPagoIdAsync(string mpPaymentId);
        Task UpdateAsync(Payment payment);
    }
}
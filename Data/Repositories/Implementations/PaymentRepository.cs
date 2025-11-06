using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly PgDbContext _context;

        public PaymentRepository(PgDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Payment payment)
        {
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
        }

        public async Task<Payment?> GetByMercadoPagoIdAsync(string mpPaymentId)
        {
            return await _context.Payments.FirstOrDefaultAsync(p => p.MercadoPagoPaymentId == mpPaymentId);
        }

        public async Task UpdateAsync(Payment payment)
        {
            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();
        }
    }
}
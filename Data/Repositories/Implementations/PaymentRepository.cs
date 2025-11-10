using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class PaymentRepository : Repository<Payment>, IPaymentRepository
{
    private readonly PgDbContext _context;

    public PaymentRepository(PgDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Payment?> GetByMercadoPagoIdAsync(string mercadoPagoId)
    {
        return await _dbSet
            .Include(p => p.User)
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.MercadoPagoPaymentId == mercadoPagoId);
    }

    public async Task<Payment?> GetByIdAsync(Guid paymentId)
    {
        return await _context.Payments
            .Include(p => p.User)
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public async Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Include(p => p.Subscription)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatusType status)
    {
        return await _dbSet
            .Include(p => p.User)
            .Where(p => p.PaymentStatus == status)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(Payment payment)
    {
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Payment payment)
    {
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid paymentId)
    {
        var payment = await GetByIdAsync(paymentId);
        if (payment == null) return false;

        _context.Payments.Remove(payment);
        await _context.SaveChangesAsync();
        return true;
    }
}
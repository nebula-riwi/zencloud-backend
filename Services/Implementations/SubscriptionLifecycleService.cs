using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class SubscriptionLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionLifecycleService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public SubscriptionLifecycleService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando el ciclo de vida de suscripciones");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Ignorado
            }
        }
    }

    private async Task ProcessAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var databaseRepository = scope.ServiceProvider.GetRequiredService<IDatabaseInstanceRepository>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var mercadoPagoService = scope.ServiceProvider.GetRequiredService<MercadoPagoService>();

        await HandleExpiringSubscriptionsAsync(subscriptionRepository, emailService);
        await HandleAutoRenewalsAsync(subscriptionRepository, mercadoPagoService);
        await HandleExpiredSubscriptionsAsync(subscriptionRepository, databaseRepository, emailService);
    }

    private async Task HandleExpiringSubscriptionsAsync(ISubscriptionRepository repository, IEmailService emailService)
    {
        var reminderDays = new[] { 5, 1 };
        foreach (var days in reminderDays)
        {
            var expiring = await repository.GetSubscriptionsExpiringInDaysAsync(days);
            foreach (var subscription in expiring)
            {
                if (subscription.LastExpirationReminderSentAt?.Date == DateTime.UtcNow.Date)
                {
                    continue;
                }

                try
                {
                    await emailService.SendSubscriptionExpiringEmailAsync(
                        subscription.User.Email,
                        subscription.User.FullName,
                        subscription.Plan.PlanName.ToString(),
                        subscription.EndDate);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo enviar el correo de suscripción próxima a expirar para {UserId}", subscription.UserId);
                }

                subscription.LastExpirationReminderSentAt = DateTime.UtcNow;
                subscription.ExpirationReminderCount++;
                subscription.UpdatedAt = DateTime.UtcNow;
                await repository.UpdateAsync(subscription);
            }
        }
    }

    private async Task HandleExpiredSubscriptionsAsync(
        ISubscriptionRepository subscriptionRepository,
        IDatabaseInstanceRepository databaseRepository,
        IEmailService emailService)
    {
        var expiredSubscriptions = await subscriptionRepository.GetExpiredSubscriptionsAsync();
        foreach (var subscription in expiredSubscriptions)
        {
            subscription.IsActive = false;
            subscription.PaymentStatus = PaymentStatus.Cancelled;
            subscription.AutoRenewEnabled = false;
            subscription.UpdatedAt = DateTime.UtcNow;
            await subscriptionRepository.UpdateAsync(subscription);

            var databases = await databaseRepository.GetByUserIdAsync(subscription.UserId);
            foreach (var database in databases.Where(db => db.Status == DatabaseInstanceStatus.Active))
            {
                database.Status = DatabaseInstanceStatus.Inactive;
                database.UpdatedAt = DateTime.UtcNow;
                await databaseRepository.UpdateAsync(database);
            }

            try
            {
                await emailService.SendSubscriptionExpiredEmailAsync(
                    subscription.User.Email,
                    subscription.User.FullName,
                    subscription.Plan.PlanName.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar el correo de expiración para {UserId}", subscription.UserId);
            }
        }
    }

    private async Task HandleAutoRenewalsAsync(ISubscriptionRepository repository, MercadoPagoService mercadoPagoService)
    {
        var expiringSoon = await repository.GetSubscriptionsExpiringInDaysAsync(1);
        foreach (var subscription in expiringSoon.Where(s => s.AutoRenewEnabled))
        {
            subscription.LastAutoRenewAttemptAt = DateTime.UtcNow;
            try
            {
                var success = await mercadoPagoService.TryAutoRenewSubscriptionAsync(subscription);
                subscription.LastAutoRenewError = success ? null : "La pasarela rechazó el cobro automático.";
            }
            catch (Exception ex)
            {
                subscription.LastAutoRenewError = ex.Message;
                _logger.LogError(ex, "Error intentando auto-renovar la suscripción {SubscriptionId}", subscription.SubscriptionId);
            }
            finally
            {
                subscription.UpdatedAt = DateTime.UtcNow;
                await repository.UpdateAsync(subscription);
            }
        }
    }
}


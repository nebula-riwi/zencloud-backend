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
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();

        await HandleExpiringSubscriptionsAsync(subscriptionRepository, emailService);
        await HandleAutoRenewalsAsync(subscriptionRepository, mercadoPagoService, webhookService);
        await HandleExpiredSubscriptionsAsync(subscriptionRepository, databaseRepository, emailService, webhookService);
    }

    private async Task HandleExpiringSubscriptionsAsync(ISubscriptionRepository repository, IEmailService emailService)
    {
        const int batchSize = 50; // Procesar en lotes de 50 suscripciones
        var reminderDays = new[] { 5, 1 };
        
        foreach (var days in reminderDays)
        {
            var totalCount = await repository.CountSubscriptionsExpiringInDaysAsync(days);
            _logger.LogInformation("Procesando {Count} suscripciones expirando en {Days} días", totalCount, days);
            
            var processed = 0;
            while (processed < totalCount)
            {
                var expiring = await repository.GetSubscriptionsExpiringInDaysAsync(days, skip: processed, take: batchSize);
                
                if (!expiring.Any())
                    break;
                
                // Procesar batch
                var updates = new List<Subscription>();
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
                        
                        subscription.LastExpirationReminderSentAt = DateTime.UtcNow;
                        subscription.ExpirationReminderCount++;
                        subscription.UpdatedAt = DateTime.UtcNow;
                        updates.Add(subscription);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo enviar el correo de suscripción próxima a expirar para {UserId}", subscription.UserId);
                    }
                }
                
                // Actualizar batch de suscripciones
                foreach (var subscription in updates)
                {
                    await repository.UpdateAsync(subscription);
                }
                
                processed += expiring.Count();
                _logger.LogDebug("Procesadas {Processed}/{Total} suscripciones expirando en {Days} días", processed, totalCount, days);
            }
        }
    }

    private async Task HandleExpiredSubscriptionsAsync(
        ISubscriptionRepository subscriptionRepository,
        IDatabaseInstanceRepository databaseRepository,
        IEmailService emailService,
        IWebhookService webhookService)
    {
        const int batchSize = 50; // Procesar en lotes de 50 suscripciones
        
        var totalCount = await subscriptionRepository.CountExpiredSubscriptionsAsync();
        _logger.LogInformation("Procesando {Count} suscripciones expiradas", totalCount);
        
        var processed = 0;
        while (processed < totalCount)
        {
            var expiredSubscriptions = await subscriptionRepository.GetExpiredSubscriptionsAsync(skip: processed, take: batchSize);
            
            if (!expiredSubscriptions.Any())
                break;
            
            foreach (var subscription in expiredSubscriptions)
            {
                subscription.IsActive = false;
                subscription.PaymentStatus = PaymentStatus.Cancelled;
                subscription.AutoRenewEnabled = false;
                subscription.UpdatedAt = DateTime.UtcNow;
                await subscriptionRepository.UpdateAsync(subscription);

                // Desactivar bases que excedan el límite del plan gratuito
                var databases = await databaseRepository.GetByUserIdAsync(subscription.UserId);
                var activeDatabases = databases.Where(db => db.Status == DatabaseInstanceStatus.Active).ToList();
                
                // Límites del plan gratuito: 2 por motor, máximo 5 globales
                var groupedByEngine = activeDatabases.GroupBy(db => db.EngineId).ToList();
                var databasesToDeactivate = new List<DatabaseInstance>();
                
                // Desactivar bases que excedan 2 por motor
                foreach (var engineGroup in groupedByEngine)
                {
                    var engineDatabases = engineGroup.OrderByDescending(db => db.CreatedAt).ToList();
                    if (engineDatabases.Count > 2) // Límite gratis por motor
                    {
                        databasesToDeactivate.AddRange(engineDatabases.Skip(2));
                    }
                }
                
                // Si hay más de 5 bases activas totales, desactivar las más antiguas
                var remainingActive = activeDatabases.Except(databasesToDeactivate).ToList();
                if (remainingActive.Count > 5) // Límite global gratis
                {
                    var excessGlobal = remainingActive.OrderByDescending(db => db.CreatedAt).Skip(5);
                    databasesToDeactivate.AddRange(excessGlobal);
                }
                
                // Desactivar bases excedentes
                foreach (var database in databasesToDeactivate)
                {
                    database.Status = DatabaseInstanceStatus.Inactive;
                    database.UpdatedAt = DateTime.UtcNow;
                    await databaseRepository.UpdateAsync(database);
                    _logger.LogInformation("Base de datos {DatabaseId} desactivada por expiración de suscripción", database.InstanceId);
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

                // Trigger webhook para subscription expired
                try
                {
                    await webhookService.TriggerWebhookAsync(
                        WebhookEventType.SubscriptionExpired,
                        new
                        {
                            subscriptionId = subscription.SubscriptionId,
                            planName = subscription.Plan.PlanName.ToString(),
                            endDate = subscription.EndDate,
                            expiredAt = DateTime.UtcNow,
                            databasesDeactivated = databasesToDeactivate.Count
                        },
                        subscription.UserId
                    );
                }
                catch (Exception webhookEx)
                {
                    _logger.LogWarning(webhookEx, "Error disparando webhook para SubscriptionExpired");
                }
            }
            
            processed += expiredSubscriptions.Count();
            _logger.LogDebug("Procesadas {Processed}/{Total} suscripciones expiradas", processed, totalCount);
        }
    }

    private async Task HandleAutoRenewalsAsync(ISubscriptionRepository repository, MercadoPagoService mercadoPagoService, IWebhookService webhookService)
    {
        const int batchSize = 50; // Procesar en lotes de 50 suscripciones
        
        var totalCount = await repository.CountSubscriptionsExpiringInDaysAsync(1);
        _logger.LogInformation("Procesando auto-renovaciones para suscripciones expirando en 1 día. Total: {Count}", totalCount);
        
        var processed = 0;
        while (processed < totalCount)
        {
            var expiringSoon = await repository.GetSubscriptionsExpiringInDaysAsync(1, skip: processed, take: batchSize);
            
            if (!expiringSoon.Any())
                break;
            
            var autoRenewSubscriptions = expiringSoon.Where(s => s.AutoRenewEnabled).ToList();
            
            foreach (var subscription in autoRenewSubscriptions)
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

                    // Trigger webhook para payment failed en auto-renovación
                    try
                    {
                        await webhookService.TriggerWebhookAsync(
                            WebhookEventType.PaymentFailed,
                            new
                            {
                                subscriptionId = subscription.SubscriptionId,
                                planName = subscription.Plan?.PlanName.ToString(),
                                attemptDate = DateTime.UtcNow,
                                errorMessage = ex.Message
                            },
                            subscription.UserId
                        );
                    }
                    catch (Exception webhookEx)
                    {
                        _logger.LogWarning(webhookEx, "Error disparando webhook para PaymentFailed");
                    }
                }
                finally
                {
                    subscription.UpdatedAt = DateTime.UtcNow;
                    await repository.UpdateAsync(subscription);
                }
            }
            
            processed += expiringSoon.Count();
            _logger.LogDebug("Procesadas {Processed}/{Total} suscripciones para auto-renovación", processed, totalCount);
        }
    }
}


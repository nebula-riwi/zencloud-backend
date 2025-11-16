using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.DTOs;
using ZenCloud.Services;

namespace ZenCloud.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly MercadoPagoService _mpService;
    private readonly IPlanRepository _planRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<PaymentsController> _logger;
    private readonly PgDbContext _context;

    public PaymentsController(
        MercadoPagoService mpService, 
        IPlanRepository planRepository, 
        ISubscriptionRepository subscriptionRepository,
        IPaymentRepository paymentRepository,
        ILogger<PaymentsController> logger,
        PgDbContext context)
    {
        _mpService = mpService;
        _planRepository = planRepository;
        _subscriptionRepository = subscriptionRepository;
        _paymentRepository = paymentRepository;
        _logger = logger;
        _context = context;
    }

    // Endpoint para crear pagos de suscripción
    [HttpPost("create")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request)
    {
        try
        {
        if (request == null || request.PlanId <= 0)
                return BadRequest(new { message = "PlanId inválido." });

            // Obtener userId del token si no viene en el request
            Guid userId = request.UserId != Guid.Empty ? request.UserId : GetUserIdFromClaims();

            // Verificar que el plan existe
            var plan = await _planRepository.GetByIdAsync(request.PlanId);
            if (plan == null)
                return BadRequest(new { message = "Plan no encontrado." });

            var currentSubscription = await _subscriptionRepository.GetActiveByUserIdAsync(userId);
            if (currentSubscription != null && currentSubscription.Plan != null)
            {
                if (currentSubscription.Plan.PlanId == plan.PlanId)
                {
                    return BadRequest(new { message = "Ya cuentas con este plan activo." });
                }

                if (currentSubscription.Plan.PriceInCOP > plan.PriceInCOP)
                {
                    return BadRequest(new { message = "No puedes cambiar a un plan inferior mientras tu suscripción esté activa." });
                }
            }

            var paymentUrl = await _mpService.CreateSubscriptionPreferenceAsync(
                userId,
                request.PlanId,
                successUrl: "https://nebula.andrescortes.dev/success",
                failureUrl: "https://nebula.andrescortes.dev/failure", 
                notificationUrl: "https://service.nebula.andrescortes.dev/api/Payments/webhook"
            );

            return Ok(new { 
                payment_url = paymentUrl,
                plan_name = plan.PlanName.ToString(),
                amount = plan.PriceInCOP
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creando pago: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, new { message = "Error al crear el pago: " + ex.Message });
        }
    }

    // Obtener planes disponibles
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        try
        {
            var plans = await _planRepository.GetAllAsync();
            var activePlans = plans.Where(p => p.IsActive).ToList();
            
            return Ok(activePlans.Select(p => new
            {
                p.PlanId,
                PlanName = p.PlanName.ToString(),
                p.MaxDatabasesPerEngine,
                p.PriceInCOP,
                p.DurationInDays,
                p.Description
            }));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Obtener suscripción actual del usuario
    [HttpGet("current-subscription")]
    [Authorize]
    public async Task<IActionResult> GetCurrentSubscription()
    {
        try
        {
            var userId = GetUserIdFromClaims();

            var subscription = await _subscriptionRepository.GetActiveByUserIdAsync(userId);
            
            if (subscription == null)
            {
                // Si no hay suscripción activa, devolver el plan gratuito
                var freePlan = await _planRepository.GetAllAsync();
                var defaultPlan = freePlan.FirstOrDefault(p => p.PlanName.ToString().ToLower() == "free");
                
                if (defaultPlan != null)
                {
                    return Ok(new
                    {
                        planId = defaultPlan.PlanId,
                        planName = defaultPlan.PlanName.ToString(),
                        maxDatabasesPerEngine = defaultPlan.MaxDatabasesPerEngine,
                        priceInCOP = defaultPlan.PriceInCOP,
                        durationInDays = defaultPlan.DurationInDays,
                        description = defaultPlan.Description,
                        isActive = true,
                        autoRenewEnabled = false
                    });
                }
                
                return Ok(new { message = "No hay suscripción activa" });
            }

            var daysRemaining = subscription.EndDate > DateTime.UtcNow 
                ? (int)(subscription.EndDate - DateTime.UtcNow).TotalDays 
                : 0;
            
            return Ok(new
            {
                planId = subscription.Plan.PlanId,
                planName = subscription.Plan.PlanName.ToString(),
                maxDatabasesPerEngine = subscription.Plan.MaxDatabasesPerEngine,
                priceInCOP = subscription.Plan.PriceInCOP,
                durationInDays = subscription.Plan.DurationInDays,
                description = subscription.Plan.Description,
                isActive = subscription.IsActive,
                startDate = subscription.StartDate,
                endDate = subscription.EndDate,
                daysRemaining = daysRemaining,
                paymentStatus = subscription.PaymentStatus.ToString(),
                autoRenewEnabled = subscription.AutoRenewEnabled,
                lastAutoRenewAttemptAt = subscription.LastAutoRenewAttemptAt,
                lastAutoRenewError = subscription.LastAutoRenewError
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error obteniendo suscripción actual: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Obtener historial de pagos del usuario
    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetPaymentHistory()
    {
        try
        {
            var userId = GetUserIdFromClaims();

            var payments = await _paymentRepository.GetByUserIdAsync(userId);
            
            var history = payments.Select(p => new PaymentHistoryResponseDto
            {
                PaymentId = p.PaymentId,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.PaymentStatus.ToString(),
                PaymentMethod = p.PaymentMethod,
                TransactionDate = p.TransactionDate,
                CreatedAt = p.CreatedAt,
                MercadoPagoPaymentId = p.MercadoPagoPaymentId,
                PlanName = p.Subscription?.Plan?.PlanName.ToString() ?? "N/A",
                SubscriptionId = p.SubscriptionId
            }).ToList();

            return Ok(new { data = history });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error obteniendo historial de pagos: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Get user database usage statistics for progress bars
    [HttpGet("usage-stats")]
    [Authorize]
    public async Task<IActionResult> GetUsageStats()
    {
        try
        {
            var userId = GetUserIdFromClaims();
            
            // Get current subscription to determine limits (optimized with projection)
            var subscriptionData = await _context.Subscriptions
                .Where(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { MaxDatabasesPerEngine = s.Plan.MaxDatabasesPerEngine })
                .FirstOrDefaultAsync();
            
            int maxPerEngine = subscriptionData?.MaxDatabasesPerEngine ?? 2; // Default free plan
            int maxGlobal = subscriptionData != null ? int.MaxValue : 5; // Free plan global limit
            
            // Get database counts per engine (optimized with projection)
            var databaseCounts = await _context.DatabaseInstances
                .Where(db => db.UserId == userId && db.Status == DatabaseInstanceStatus.Active)
                .Include(db => db.Engine) // Need Engine for name
                .GroupBy(db => db.EngineId)
                .Select(g => new
                {
                    EngineId = g.Key,
                    EngineName = g.First().Engine!.EngineName.ToString(),
                    Count = g.Count()
                })
                .ToListAsync();
            
            var totalActive = databaseCounts.Sum(d => d.Count);
            
            // Calculate usage percentages
            var usageByEngine = databaseCounts.Select(d => new
            {
                engineId = d.EngineId,
                engineName = d.EngineName,
                used = d.Count,
                limit = maxPerEngine,
                percentage = Math.Round((double)d.Count / maxPerEngine * 100, 1),
                canCreate = d.Count < maxPerEngine
            }).ToList();
            
            return Ok(new
            {
                totalActive = totalActive,
                totalLimit = maxGlobal == int.MaxValue ? null : (int?)maxGlobal,
                globalPercentage = maxGlobal == int.MaxValue ? 0 : Math.Round((double)totalActive / maxGlobal * 100, 1),
                byEngine = usageByEngine
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting usage stats");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Este endpoint ya no es necesario ya que los pagos se crean como "Rejected" por defecto
    // y solo se cambian a "Approved" cuando MercadoPago confirma el pago
    // Se mantiene solo para compatibilidad con código legacy pero no hace nada
    [HttpPost("cancel-expired-pending")]
    [Authorize]
    public async Task<IActionResult> CancelExpiredPendingPayments()
    {
        try
        {
            // Ya no hay pagos "Pending" en el sistema, todos se crean como "Rejected" hasta que se confirmen
            return Ok(new { 
                message = "Ya no hay pagos pendientes en el sistema. Los pagos se crean como rechazados hasta que MercadoPago los confirme.",
                cancelled = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en cancel-expired-pending");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPatch("auto-renew")]
    [Authorize]
    public async Task<IActionResult> UpdateAutoRenew([FromBody] UpdateAutoRenewRequest request)
    {
        try
        {
            var userId = GetUserIdFromClaims();
            var subscription = await _subscriptionRepository.GetActiveByUserIdAsync(userId);

            if (subscription == null)
            {
                return NotFound(new { message = "No hay una suscripción activa" });
            }

            subscription.AutoRenewEnabled = request.Enabled;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(subscription);

            var message = request.Enabled
                ? "Renovación automática activada."
                : "Renovación automática desactivada.";

            return Ok(new { message, autoRenewEnabled = subscription.AutoRenewEnabled });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating auto-renew setting");
            return StatusCode(500, new { message = "No se pudo actualizar la renovación automática" });
        }
    }

    // Webhook para procesar pagos
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            // Leer el body del request
            using var reader = new StreamReader(HttpContext.Request.Body);
            var body = await reader.ReadToEndAsync();
            
            Console.WriteLine("Webhook recibido:");
            Console.WriteLine(body);

            var data = JsonDocument.Parse(body).RootElement;
            await _mpService.ProcessWebhookAsync(data);
            
            return Ok(new { message = "Webhook procesado correctamente" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error procesando webhook: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }

    }

    private Guid GetUserIdFromClaims()
    {
        var userIdClaim = User.FindFirst("userId")?.Value ??
                          User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Usuario no autenticado");
        }

        return userId;
    }
}
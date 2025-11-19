using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services
{
    public class MercadoPagoService
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IEmailService _emailService;
        private readonly IUserRepository _userRepository;
        private readonly ISubscriptionRepository _subscriptionRepository;
        private readonly IPlanRepository _planRepository;
        private readonly IPlanValidationService _planValidationService;
        private readonly ILogger<MercadoPagoService> _logger;
        private readonly IWebhookService _webhookService;

        public MercadoPagoService(
            IConfiguration configuration, 
            IPaymentRepository paymentRepository, 
            IEmailService emailService,
            IUserRepository userRepository,
            ISubscriptionRepository subscriptionRepository,
            IPlanRepository planRepository,
            IPlanValidationService planValidationService,
            ILogger<MercadoPagoService> logger,
            IWebhookService webhookService)
        {
            _accessToken = configuration["MercadoPago:AccessToken"]!;
            _paymentRepository = paymentRepository;
            _userRepository = userRepository;
            _emailService = emailService;
            _subscriptionRepository = subscriptionRepository;
            _planRepository = planRepository;
            _planValidationService = planValidationService;
            _logger = logger;
            _webhookService = webhookService;

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.mercadopago.com/")
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        // Crear preferencia de pago para SUSCRIPCIÓN
        public async Task<string> CreateSubscriptionPreferenceAsync(Guid userId, int planId, string successUrl, string failureUrl, string notificationUrl)
        {
            // Obtener el plan
            var plan = await _planRepository.GetByIdAsync(planId);
            if (plan == null)
                throw new ArgumentException("Plan no encontrado");

            var body = new
            {
                items = new[]
                {
                    new {
                        title = $"Suscripción {plan.PlanName} - ZenCloud",
                        description = $"Plan {plan.PlanName} - {plan.MaxDatabasesPerEngine} bases de datos por motor",
                        quantity = 1,
                        currency_id = "COP",
                        unit_price = plan.PriceInCOP
                    }
                },
                back_urls = new
                {
                    success = successUrl,
                    failure = failureUrl,
                    pending = failureUrl
                },
                auto_return = "approved",
                notification_url = notificationUrl,
                metadata = new
                {
                    user_id = userId.ToString(),
                    plan_id = planId,
                    type = "subscription"
                }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("checkout/preferences", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"MercadoPago Error: {response.StatusCode}");
                Console.WriteLine(responseBody);
                throw new Exception($"MercadoPago Error: {response.StatusCode} - {responseBody}");
            }
            
            var jsonDoc = JsonDocument.Parse(responseBody).RootElement;
            var initPoint = jsonDoc.GetProperty("init_point").GetString()!;
            var preferenceId = jsonDoc.GetProperty("id").GetString()!;

            // Guardar el pago en la BD con PREFERENCE ID (no payment ID)
            // Se crea como "Rejected" por defecto, solo se cambia a "Approved" cuando MercadoPago confirme
            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                UserId = userId,
                Amount = plan.PriceInCOP,
                Currency = "COP",
                PaymentStatus = PaymentStatusType.Rejected, // Por defecto rechazado hasta que MercadoPago confirme
                PaymentMethod = "subscription",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                MercadoPagoPaymentId = preferenceId // Este es el PREFERENCE ID
            };

            await _paymentRepository.AddAsync(payment);

            Console.WriteLine($"Preference creada: {preferenceId}");
            Console.WriteLine($"Payment guardado en BD con MercadoPagoPaymentId: {preferenceId}");

            return initPoint;
        }

        // Procesar Webhook de Mercado Pago
        public async Task ProcessWebhookAsync(JsonElement data)
        {
            Console.WriteLine("Analizando webhook...");
            
            if (!data.TryGetProperty("topic", out var topic) || topic.GetString() != "payment")
            {
                Console.WriteLine("Webhook ignorado: no es topic 'payment'.");
                return;
            }

            // Leer paymentId del webhook
            if (!data.TryGetProperty("resource", out var resource) || string.IsNullOrEmpty(resource.GetString()))
            {
                Console.WriteLine("Resource no encontrado en webhook");
                return;
            }

            var paymentIdString = resource.GetString()!;
            Console.WriteLine($"Payment ID del webhook: {paymentIdString}");

            if (!long.TryParse(paymentIdString, out var paymentId))
            {
                Console.WriteLine($"Payment ID invalido: {paymentIdString}");
                return;
            }

            Console.WriteLine($"Consultando pago {paymentId} en Mercado Pago...");

            var response = await _httpClient.GetAsync($"v1/payments/{paymentId}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error consultando pago: {response.StatusCode}");
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            var paymentData = JsonDocument.Parse(body).RootElement;

            // Leer datos del pago
            string mpPaymentId = paymentData.GetProperty("id").ValueKind switch
            {
                JsonValueKind.Number => paymentData.GetProperty("id").GetInt64().ToString(),
                JsonValueKind.String => paymentData.GetProperty("id").GetString()!,
                _ => paymentId.ToString()
            };

            string status = paymentData.TryGetProperty("status", out var s) ? s.GetString() ?? "pending" : "pending";
            string? paymentMethodId = paymentData.TryGetProperty("payment_method_id", out var methodElement) ? methodElement.GetString() : null;
            string? paymentTypeId = paymentData.TryGetProperty("payment_type_id", out var typeElement) ? typeElement.GetString() : null;
            string? payerId = null;
            if (paymentData.TryGetProperty("payer", out var payerElement) && payerElement.ValueKind == JsonValueKind.Object)
            {
                payerId = payerElement.TryGetProperty("id", out var payerIdElement) ? payerIdElement.GetString() : null;
            }
            string? cardId = null;
            if (paymentData.TryGetProperty("card", out var cardElement) && cardElement.ValueKind == JsonValueKind.Object)
            {
                cardId = cardElement.TryGetProperty("id", out var cardIdElement) ? cardIdElement.GetString() : null;
            }

            // Leer metadata para obtener plan_id y user_id
            Guid userId = Guid.Empty;
            int planId = 1; // Default Free plan
            string? preferenceId = null;

            if (paymentData.TryGetProperty("metadata", out var metadata))
            {
                if (metadata.TryGetProperty("user_id", out var userIdProp) && 
                    Guid.TryParse(userIdProp.GetString(), out var parsedUserId))
                {
                    userId = parsedUserId;
                }

                if (metadata.TryGetProperty("plan_id", out var planIdProp))
                {
                    planId = planIdProp.GetInt32();
                }
            }

            // Leer preference_id del pago (si existe)
            if (paymentData.TryGetProperty("preference_id", out var prefId))
            {
                preferenceId = prefId.GetString();
                Console.WriteLine($"Preference ID encontrado en payment: {preferenceId}");
            }

            Console.WriteLine($"Estado: {status} | Payment ID: {mpPaymentId} | Preference ID: {preferenceId} | User: {userId} | Plan: {planId}");

            // BUSCAR POR PREFERENCE ID primero, luego por payment ID
            Payment? payment = null;
            
            if (!string.IsNullOrEmpty(preferenceId))
            {
                payment = await _paymentRepository.GetByMercadoPagoIdAsync(preferenceId);
                if (payment != null)
                {
                    Console.WriteLine($"Payment encontrado por Preference ID: {preferenceId}");
                }
            }
            
            // Si no se encontró por preference_id, buscar por user_id y plan_id en pagos recientes (últimos 5 minutos)
            // Buscamos en pagos rechazados recientes que puedan corresponder a este intento de pago
            if (payment == null && userId != Guid.Empty)
            {
                var recentThreshold = DateTime.UtcNow.AddMinutes(-5);
                var recentPayments = await _paymentRepository.GetByUserIdAsync(userId);
                payment = recentPayments
                    .Where(p => p.CreatedAt >= recentThreshold && p.PaymentStatus == PaymentStatusType.Rejected)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefault();
                if (payment != null)
                {
                    Console.WriteLine($"Payment encontrado por User ID (reciente): {userId}");
                }
            }
            
            if (payment == null)
            {
                Console.WriteLine("No se encontro el pago en la base de datos.");
                Console.WriteLine($"Payment ID: {mpPaymentId}, Preference ID: {preferenceId}, User ID: {userId}, Plan ID: {planId}");
                return;
            }
            
            if (!string.IsNullOrEmpty(paymentTypeId))
            {
                payment.PaymentMethod = paymentTypeId;
            }
            if (!string.IsNullOrEmpty(paymentMethodId))
            {
                payment.PaymentMethodId = paymentMethodId;
            }
            if (!string.IsNullOrEmpty(payerId))
            {
                payment.PayerId = payerId;
            }
            if (!string.IsNullOrEmpty(cardId))
            {
                payment.CardId = cardId;
            }

            // Actualizar estado del pago - solo dos estados: Approved o Rejected
            payment.PaymentStatus = status == "approved" 
                ? PaymentStatusType.Approved 
                : PaymentStatusType.Rejected; // Todos los demás estados (rejected, cancelled, refunded, charged_back, etc.) son Rejected
            payment.TransactionDate = DateTime.UtcNow;
            // Guardar también el payment ID real de Mercado Pago
            payment.MercadoPagoPaymentId = mpPaymentId;

            await _paymentRepository.UpdateAsync(payment);

            // Si el pago fue aprobado, actualizar suscripción y enviar correos
            if (status == "approved")
            {
                await ProcessApprovedPaymentAsync(payment, planId, userId);
            }
            else
            {
                // Trigger webhook para pago rechazado
                try
                {
                    await _webhookService.TriggerWebhookAsync(
                        WebhookEventType.PaymentRejected,
                        new
                        {
                            paymentId = payment.PaymentId,
                            amount = payment.Amount,
                            currency = payment.Currency,
                            status = status,
                            rejectedAt = DateTime.UtcNow
                        },
                        userId
                    );
                }
                catch (Exception webhookEx)
                {
                    _logger.LogWarning(webhookEx, "Error disparando webhook para PaymentRejected");
                }
            }
        }


        private async Task ProcessApprovedPaymentAsync(Payment payment, int planId, Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                Console.WriteLine($"Usuario no encontrado: {userId}");
                return;
            }

            var plan = await _planRepository.GetByIdAsync(planId);
            if (plan == null)
            {
                Console.WriteLine($"Plan no encontrado: {planId}");
                return;
            }

            Console.WriteLine($"Procesando pago aprobado para usuario: {user.Email}, plan: {plan.PlanName}");

            // Buscar suscripción activa del usuario
            var existingSubscription = await _subscriptionRepository.GetActiveByUserIdAsync(userId);
            Subscription subscription;
            
            if (existingSubscription != null)
            {
                // Verificar si hay downgrade (plan anterior tenía más límites)
                var oldPlan = existingSubscription.Plan;
                var isDowngrade = oldPlan != null && oldPlan.MaxDatabasesPerEngine > plan.MaxDatabasesPerEngine;
                
                // Actualizar suscripción existente
                existingSubscription.PlanId = plan.PlanId;
                existingSubscription.StartDate = DateTime.UtcNow;
                existingSubscription.EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays);
                existingSubscription.PaymentStatus = PaymentStatus.Paid;
                existingSubscription.UpdatedAt = DateTime.UtcNow;

                await _subscriptionRepository.UpdateAsync(existingSubscription);
                subscription = existingSubscription; // Guardar referencia
                Console.WriteLine($"Suscripcion actualizada: {existingSubscription.SubscriptionId}");
                
                // Si hay downgrade, hacer cumplir los límites del nuevo plan
                if (isDowngrade)
                {
                    await _planValidationService.EnforcePlanLimitsAsync(userId);
                    Console.WriteLine($"Límites del plan aplicados después de downgrade para usuario: {userId}");
                }
            }
            else
            {
                // Crear nueva suscripción
                subscription = new Subscription
                {
                    SubscriptionId = Guid.NewGuid(),
                    UserId = userId,
                    PlanId = plan.PlanId,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
                    IsActive = true,
                    PaymentStatus = PaymentStatus.Paid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _subscriptionRepository.AddAsync(subscription);
                Console.WriteLine($"Nueva suscripcion creada: {subscription.SubscriptionId}");

                // Trigger webhook para subscription created
                try
                {
                    await _webhookService.TriggerWebhookAsync(
                        WebhookEventType.SubscriptionCreated,
                        new
                        {
                            subscriptionId = subscription.SubscriptionId,
                            planId = plan.PlanId,
                            planName = plan.PlanName.ToString(),
                            startDate = subscription.StartDate,
                            endDate = subscription.EndDate,
                            price = plan.PriceInCOP
                        },
                        userId
                    );
                }
                catch (Exception webhookEx)
                {
                    _logger.LogWarning(webhookEx, "Error disparando webhook para SubscriptionCreated");
                }
            }

            // Vincular el pago con la suscripción
            payment.SubscriptionId = subscription.SubscriptionId;
            await _paymentRepository.UpdateAsync(payment);
            Console.WriteLine($"Pago vinculado con suscripcion: {payment.PaymentId} -> {subscription.SubscriptionId}");

            // Enviar correo de confirmación de pago
            try
            {
                await _emailService.SendPaymentConfirmationEmailAsync(
                    user.Email,
                    user.FullName,
                    plan.PlanName.ToString(),
                    payment.Amount,
                    "Aprobado",
                    payment.TransactionDate
                );
                Console.WriteLine($"Email de confirmacion enviado a: {user.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enviando email: {ex.Message}");
            }

            // Enviar correo de cambio de plan
            try
            {
                await _emailService.SendPlanChangeEmailAsync(
                    user.Email,
                    user.FullName,
                    plan.PlanName.ToString(),
                    payment.TransactionDate
                );
                Console.WriteLine($"Email de cambio de plan enviado a: {user.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enviando email de cambio de plan: {ex.Message}");
            }

            // Trigger webhook para payment received
            try
            {
                await _webhookService.TriggerWebhookAsync(
                    WebhookEventType.PaymentReceived,
                    new
                    {
                        paymentId = payment.PaymentId,
                        subscriptionId = subscription.SubscriptionId,
                        planName = plan.PlanName.ToString(),
                        amount = payment.Amount,
                        currency = payment.Currency,
                        paymentMethod = payment.PaymentMethod,
                        approvedAt = payment.TransactionDate
                    },
                    userId
                );
            }
            catch (Exception webhookEx)
            {
                _logger.LogWarning(webhookEx, "Error disparando webhook para PaymentReceived");
            }
        }

    public async Task<bool> TryAutoRenewSubscriptionAsync(Subscription subscription)
    {
        if (subscription.Plan == null)
        {
            throw new InvalidOperationException("El plan asociado a la suscripción no está disponible.");
        }

        var lastPayment = await _paymentRepository.GetLastApprovedBySubscriptionAsync(subscription.SubscriptionId);
        if (lastPayment == null || string.IsNullOrEmpty(lastPayment.PayerId) || string.IsNullOrEmpty(lastPayment.CardId))
        {
            _logger.LogWarning("No hay información suficiente para autorrenovar la suscripción {SubscriptionId}", subscription.SubscriptionId);
            return false;
        }

        var paymentMethodId = lastPayment.PaymentMethodId ?? lastPayment.PaymentMethod ?? "credit_card";

        var body = new
        {
            transaction_amount = subscription.Plan.PriceInCOP,
            description = $"Renovación automática plan {subscription.Plan.PlanName}",
            payment_method_id = paymentMethodId,
            installments = 1,
            binary_mode = true,
            payer = new
            {
                id = lastPayment.PayerId,
                type = "customer"
            },
            metadata = new
            {
                user_id = subscription.UserId.ToString(),
                plan_id = subscription.PlanId,
                auto_renew = true
            },
            card_id = lastPayment.CardId
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("v1/payments", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Auto-renovación fallida para suscripción {SubscriptionId}: {Status} - {Body}", subscription.SubscriptionId, response.StatusCode, responseBody);
            return false;
        }

        var json = JsonDocument.Parse(responseBody).RootElement;
        var status = json.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "pending" : "pending";
        var mpPaymentId = json.TryGetProperty("id", out var idElement) ? idElement.GetInt64().ToString() : Guid.NewGuid().ToString();

        var autoPayment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            UserId = subscription.UserId,
            SubscriptionId = subscription.SubscriptionId,
            Amount = subscription.Plan.PriceInCOP,
            Currency = "COP",
            PaymentStatus = status == "approved" ? PaymentStatusType.Approved : PaymentStatusType.Rejected,
            PaymentMethod = $"auto_{paymentMethodId}",
            PaymentMethodId = paymentMethodId,
            PayerId = lastPayment.PayerId,
            CardId = lastPayment.CardId,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            MercadoPagoPaymentId = mpPaymentId
        };

        await _paymentRepository.AddAsync(autoPayment);

        if (status == "approved")
        {
            await ProcessApprovedPaymentAsync(autoPayment, subscription.PlanId, subscription.UserId);
            _logger.LogInformation("Auto-renovación exitosa para suscripción {SubscriptionId}", subscription.SubscriptionId);
            return true;
            }

        _logger.LogWarning("Auto-renovación para suscripción {SubscriptionId} no fue aprobada (estado {Status})", subscription.SubscriptionId, status);
        return false;
        }
    }
}
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

        public MercadoPagoService(
            IConfiguration configuration, 
            IPaymentRepository paymentRepository, 
            IEmailService emailService,
            IUserRepository userRepository,
            ISubscriptionRepository subscriptionRepository,
            IPlanRepository planRepository)
        {
            _accessToken = configuration["MercadoPago:AccessToken"]!;
            _paymentRepository = paymentRepository;
            _userRepository = userRepository;
            _emailService = emailService;
            _subscriptionRepository = subscriptionRepository;
            _planRepository = planRepository;

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
            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                UserId = userId,
                Amount = plan.PriceInCOP,
                Currency = "COP",
                PaymentStatus = PaymentStatusType.Pending,
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
            
            // Si no se encontró por preference_id, buscar por user_id y plan_id en pagos pendientes
            if (payment == null && userId != Guid.Empty)
            {
                var pendingPayments = await _paymentRepository.GetByStatusAsync(PaymentStatusType.Pending);
                payment = pendingPayments.FirstOrDefault(p => p.UserId == userId);
                if (payment != null)
                {
                    Console.WriteLine($"Payment encontrado por User ID: {userId}");
                }
            }
            
            if (payment == null)
            {
                Console.WriteLine("No se encontro el pago en la base de datos.");
                Console.WriteLine($"Payment ID: {mpPaymentId}, Preference ID: {preferenceId}, User ID: {userId}, Plan ID: {planId}");
                return;
            }

            // Actualizar estado del pago
            payment.PaymentStatus = status switch
            {
                "approved" => PaymentStatusType.Approved,
                "rejected" => PaymentStatusType.Rejected,
                _ => PaymentStatusType.Pending
            };
            payment.TransactionDate = DateTime.UtcNow;
            // Guardar también el payment ID real de Mercado Pago
            payment.MercadoPagoPaymentId = mpPaymentId;

            await _paymentRepository.UpdateAsync(payment);

            // Si el pago fue aprobado, actualizar suscripción y enviar correos
            if (status == "approved")
            {
                await ProcessApprovedPaymentAsync(payment, planId, userId);
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
                // Actualizar suscripción existente
                existingSubscription.PlanId = plan.PlanId;
                existingSubscription.StartDate = DateTime.UtcNow;
                existingSubscription.EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays);
                existingSubscription.PaymentStatus = PaymentStatus.Paid;
                existingSubscription.UpdatedAt = DateTime.UtcNow;

                await _subscriptionRepository.UpdateAsync(existingSubscription);
                subscription = existingSubscription; // Guardar referencia
                Console.WriteLine($"Suscripcion actualizada: {existingSubscription.SubscriptionId}");
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
        }
    }
}
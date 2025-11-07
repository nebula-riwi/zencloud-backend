using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Implementations;
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
        public MercadoPagoService(IConfiguration configuration, IPaymentRepository paymentRepository, IEmailService emailService,IUserRepository  userRepository)
        {
            _accessToken = configuration["MercadoPago:AccessToken"]!;
            _paymentRepository = paymentRepository;
            _userRepository = userRepository;
            _emailService = emailService;

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.mercadopago.com/")
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        // ✅ Crear preferencia de pago en Mercado Pago
        public async Task<string> CrearPreferenciaAsync(Guid userId, decimal amount, string paymentType, string successUrl, string failureUrl, string notificationUrl)
        {
            var body = new
            {
                items = new[]
                {
                    new {
                        title = $"Pago {paymentType}",
                        quantity = 1,
                        currency_id = "COP",
                        unit_price = amount
                    }
                },
                back_urls = new
                {
                    success = successUrl,
                    failure = failureUrl
                },
                auto_return = "approved",
                notification_url = notificationUrl
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("checkout/preferences", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ MercadoPago Error: {response.StatusCode}");
                Console.WriteLine(responseBody);
                throw new Exception($"MercadoPago Error: {response.StatusCode} - {responseBody}");
            }
            
            var jsonDoc = JsonDocument.Parse(responseBody).RootElement;
            var initPoint = jsonDoc.GetProperty("init_point").GetString()!;
            var prefId = jsonDoc.GetProperty("id").GetString()!;

            // ✅ Guardar el pago en la BD como pendiente
            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                UserId = userId,
                Amount = amount,
                Currency = "COP",
                PaymentStatus = PaymentStatusType.Pending,
                PaymentMethod = paymentType,
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                MercadoPagoPaymentId = prefId // ID de la preferencia
            };

            await _paymentRepository.AddAsync(payment);

            return JsonSerializer.Serialize(new { init_point = initPoint });
        }

        // ✅ Procesar Webhook de Mercado Pago
        public async Task ProcesarWebhookAsync(JsonElement data)
        {
            if (!data.TryGetProperty("type", out var type) || type.GetString() != "payment")
            {
                Console.WriteLine("ℹ️ Webhook ignorado: no es tipo 'payment'.");
                return;
            }

            var paymentId = data.GetProperty("data").GetProperty("id").GetInt64();
            Console.WriteLine($"🔍 Consultando pago {paymentId} en Mercado Pago...");

            var response = await _httpClient.GetAsync($"v1/payments/{paymentId}");
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var paymentData = JsonDocument.Parse(body).RootElement;

            var status = paymentData.GetProperty("status").GetString() ?? "pending";
            var mpPaymentId = paymentData.GetProperty("id").ToString();
            var payerEmail = paymentData.GetProperty("payer").GetProperty("email").GetString();

            Console.WriteLine($"💰 Estado: {status} | ID: {mpPaymentId} | Email: {payerEmail}");

            var existingPayment = await _paymentRepository.GetByMercadoPagoIdAsync(mpPaymentId);
            if (existingPayment == null)
            {
                Console.WriteLine("⚠️ No se encontró el pago en la base de datos.");
                return;
            }

            existingPayment.PaymentStatus = status switch
            {
                "approved" => PaymentStatusType.Approved,
                "rejected" => PaymentStatusType.Rejected,
                _ => PaymentStatusType.Pending
            };
            existingPayment.TransactionDate = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(existingPayment);

            // Enviar correo si el pago fue aprobado
            var user = await _userRepository.GetByIdAsync(existingPayment.UserId);

            if (user != null && status == "approved")
            {
                await _emailService.SendPaymentConfirmationEmailAsync(
                    user.Email,
                    user.FullName,
                    existingPayment.PaymentMethod,
                    existingPayment.Amount,
                    "Aprobado",
                    existingPayment.TransactionDate
                );
            }
        }

    }
}

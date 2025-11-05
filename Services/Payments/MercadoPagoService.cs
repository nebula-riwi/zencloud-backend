using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Services
{
    public class MercadoPagoService
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;
        private readonly IPaymentRepository _paymentRepository;

        public MercadoPagoService(IConfiguration configuration, IPaymentRepository paymentRepository)
        {
            _accessToken = configuration["MercadoPago:AccessToken"]!;
            _paymentRepository = paymentRepository;

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.mercadopago.com/")
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        public async Task<string> CrearPreferenciaAsync(Payment payment, string successUrl, string failureUrl, string notificationUrl)
        {
            var body = new
            {
                items = new[]
                {
                    new
                    {
                        title = "Plan Premium",
                        quantity = 1,
                        currency_id = "COP",
                        unit_price = payment.Amount
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
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();

            // Guardar el pago en la BD
            await _paymentRepository.AddAsync(payment);

            return responseBody;
        }

        public async Task ProcesarWebhookAsync(JsonElement data)
        {
            if (!data.TryGetProperty("type", out var type) || type.GetString() != "payment")
                return;

            var paymentId = data.GetProperty("data").GetProperty("id").GetInt64();

            var response = await _httpClient.GetAsync($"v1/payments/{paymentId}");
            response.EnsureSuccessStatusCode();

            var paymentData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var status = paymentData.GetProperty("status").GetString() ?? "pending";

            var mpPaymentId = paymentData.GetProperty("id").ToString();

            var existingPayment = await _paymentRepository.GetByMercadoPagoIdAsync(mpPaymentId);

            if (existingPayment != null)
            {
                existingPayment.PaymentStatus = status switch
                {
                    "approved" => PaymentStatusType.Approved,
                    "rejected" => PaymentStatusType.Rejected,
                    _ => PaymentStatusType.Pending
                };

                await _paymentRepository.UpdateAsync(existingPayment);
            }
        }
    }
}

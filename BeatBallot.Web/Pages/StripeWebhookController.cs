using BeatBallot.Web.Services.Local;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BeatBallot.Web.LiveSession.TakeRequests;

namespace BeatBallot.Web.Pages
{
    [ApiController]
    [Route("api/[controller]")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly ITipService _tipService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly ISongRequestCacheService _songRequestCacheService;

        public StripeWebhookController(
            ITipService tipService,
            IConfiguration configuration,
            ILogger<StripeWebhookController> logger)
        {
            _tipService = tipService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> HandleWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var webhookSecret = _configuration["Stripe:WebhookSecret"];

            try
            {
                // Manual webhook signature verification (no Stripe.net package needed)
                var isValid = VerifyStripeSignature(json, Request.Headers["Stripe-Signature"], webhookSecret);

                if (!isValid)
                {
                    _logger.LogWarning("Invalid Stripe webhook signature");
                    return BadRequest("Invalid signature");
                }

                // Parse the event manually
                var stripeEvent = JsonSerializer.Deserialize<StripeWebhookEvent>(json);

                if (stripeEvent == null)
                {
                    _logger.LogWarning("Failed to parse Stripe webhook event");
                    return BadRequest("Invalid event data");
                }

                _logger.LogInformation("Received Stripe webhook: {EventType} with ID: {EventId}",
                    stripeEvent.type, stripeEvent.id);

                // Handle different event types using string constants
                switch (stripeEvent.type)
                {
                    case "payment_intent.succeeded":
                        await HandlePaymentIntentSucceeded(stripeEvent);
                        break;

                    case "payment_intent.payment_failed":
                        await HandlePaymentIntentFailed(stripeEvent);
                        break;

                    case "charge.succeeded":
                        await HandleChargeSucceeded(stripeEvent);
                        break;

                    default:
                        _logger.LogInformation("Unhandled Stripe webhook event type: {EventType}", stripeEvent.type);
                        break;
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Stripe webhook");
                return BadRequest("Webhook processing failed");
            }
        }

        private async Task HandlePaymentIntentSucceeded(StripeWebhookEvent stripeEvent)
        {
            try
            {
                var paymentIntent = JsonSerializer.Deserialize<StripePaymentIntentData>(
                    stripeEvent.data.@object.ToString() ?? "{}");

                if (paymentIntent != null && !string.IsNullOrEmpty(paymentIntent.id))
                {
                    var success = await _tipService.ConfirmTipPaymentAsync(
                        paymentIntent.id,
                        paymentIntent.latest_charge ?? ""
                    );

                    if (success)
                    {
                        _logger.LogInformation("Successfully confirmed tip payment: {PaymentIntentId}", paymentIntent.id);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to confirm tip payment: {PaymentIntentId}", paymentIntent.id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling payment_intent.succeeded event");
            }
        }

        private async Task HandlePaymentIntentFailed(StripeWebhookEvent stripeEvent)
        {
            try
            {
                var paymentIntent = JsonSerializer.Deserialize<StripePaymentIntentData>(
                    stripeEvent.data.@object.ToString() ?? "{}");

                if (paymentIntent != null && !string.IsNullOrEmpty(paymentIntent.id))
                {
                    // Update tip status to failed
                    await _songRequestCacheService.UpdateTipStatusAsync(paymentIntent.id, TipStatus.Failed);

                    _logger.LogInformation("Marked tip payment as failed: {PaymentIntentId}", paymentIntent.id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling payment_intent.payment_failed event");
            }
        }

        private async Task HandleChargeSucceeded(StripeWebhookEvent stripeEvent)
        {
            try
            {
                var charge = JsonSerializer.Deserialize<StripeChargeData>(
                    stripeEvent.data.@object.ToString() ?? "{}");

                if (charge != null && !string.IsNullOrEmpty(charge.payment_intent))
                {
                    // Additional processing if needed
                    _logger.LogInformation("Charge succeeded for payment intent: {PaymentIntentId}", charge.payment_intent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling charge.succeeded event");
            }
        }

        private bool VerifyStripeSignature(string payload, string signatureHeader, string webhookSecret)
        {
            try
            {
                if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(webhookSecret))
                    return false;

                // Parse the signature header
                var signatures = signatureHeader.Split(',');
                var timestamp = "";
                var signature = "";

                foreach (var sig in signatures)
                {
                    var parts = sig.Split('=');
                    if (parts.Length == 2)
                    {
                        if (parts[0] == "t")
                            timestamp = parts[1];
                        else if (parts[0] == "v1")
                            signature = parts[1];
                    }
                }

                if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
                    return false;

                // Check timestamp (should be within 5 minutes)
                if (long.TryParse(timestamp, out var timestampLong))
                {
                    var eventTime = DateTimeOffset.FromUnixTimeSeconds(timestampLong);
                    var timeDifference = DateTimeOffset.UtcNow - eventTime;

                    if (Math.Abs(timeDifference.TotalMinutes) > 5)
                    {
                        _logger.LogWarning("Stripe webhook timestamp too old: {TimeDifference} minutes", timeDifference.TotalMinutes);
                        return false;
                    }
                }

                // Verify signature
                var signedPayload = $"{timestamp}.{payload}";
                var computedHash = ComputeHmacSha256(signedPayload, webhookSecret);

                return signature.Equals(computedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Stripe signature");
                return false;
            }
        }

        private string ComputeHmacSha256(string message, string secret)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(messageBytes);

            return Convert.ToHexString(hashBytes).ToLower();
        }
    }

    // Manual DTOs for Stripe webhook events (no NuGet package needed)
    public class StripeWebhookEvent
    {
        public string id { get; set; } = "";
        public string @object { get; set; } = "";
        public string type { get; set; } = "";
        public long created { get; set; }
        public StripeWebhookData data { get; set; } = new();
        public bool livemode { get; set; }
    }

    public class StripeWebhookData
    {
        public object @object { get; set; } = new();
    }

    public class StripePaymentIntentData
    {
        public string id { get; set; } = "";
        public string @object { get; set; } = "";
        public long amount { get; set; }
        public string currency { get; set; } = "";
        public string status { get; set; } = "";
        public string? latest_charge { get; set; }
        public Dictionary<string, string> metadata { get; set; } = new();
    }

    public class StripeChargeData
    {
        public string id { get; set; } = "";
        public string @object { get; set; } = "";
        public long amount { get; set; }
        public string currency { get; set; } = "";
        public string status { get; set; } = "";
        public string payment_intent { get; set; } = "";
    }

    // Option 2: If you want to use Stripe.net package constants, define them manually
    public static class StripeEventTypes
    {
        public const string PaymentIntentSucceeded = "payment_intent.succeeded";
        public const string PaymentIntentPaymentFailed = "payment_intent.payment_failed";
        public const string PaymentIntentCanceled = "payment_intent.canceled";
        public const string ChargeSucceeded = "charge.succeeded";
        public const string ChargeFailed = "charge.failed";
        public const string InvoicePaymentSucceeded = "invoice.payment_succeeded";
        public const string InvoicePaymentFailed = "invoice.payment_failed";
        public const string CustomerSubscriptionCreated = "customer.subscription.created";
        public const string CustomerSubscriptionDeleted = "customer.subscription.deleted";
    }
}

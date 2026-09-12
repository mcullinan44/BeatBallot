using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.Services.Local;
using System.ComponentModel.DataAnnotations;
using Stripe;

namespace BeatBallot.Web.Services.Local
{
    public class Tip
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = "";
        public string DjUserId { get; set; } = "";
        public string TipperName { get; set; } = "";
        public string? TipperEmail { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string PaymentIntentId { get; set; } = "";
        public TipStatus Status { get; set; } = TipStatus.Pending;
        public string? StripeChargeId { get; set; }
    }

    public enum TipStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Refunded
    }

    public class TipRequest
    {
        [Required]
        [Range(0.50, 500.00, ErrorMessage = "Tip amount must be between $0.50 and $500.00")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50, ErrorMessage = "Name cannot exceed 50 characters")]
        public string TipperName { get; set; } = "";

        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string? TipperEmail { get; set; }

        [StringLength(200, ErrorMessage = "Message cannot exceed 200 characters")]
        public string? Message { get; set; }
    }
    public class TipStatistics
    {
        public string SessionId { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int TipCount { get; set; }
        public decimal AverageAmount { get; set; }
        public decimal LargestTip { get; set; }
        public decimal SmallestTip { get; set; }
        public DateTime? FirstTipTime { get; set; }
        public DateTime? LastTipTime { get; set; }
        public int UniqueTippers { get; set; }
    }
    public class DjPaymentSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DjUserId { get; set; } = "";
        public string? StripeAccountId { get; set; }
        public bool IsPaymentEnabled { get; set; } = false;
        public bool ShowTipJar { get; set; } = true;
        public decimal MinTipAmount { get; set; } = 1.00m;
        public decimal MaxTipAmount { get; set; } = 100.00m;
        public string? PresetAmounts { get; set; } = "1,5,10,20"; // Comma-separated
        public string? TipJarMessage { get; set; }

        // Simple payment methods (alternative to Stripe)
        public string? VenmoHandle { get; set; }
        public string? CashAppHandle { get; set; }
        public string? ZelleEmail { get; set; }
        public string? PayPalMeLink { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
    public class SessionInfo
    {
        public string SessionId { get; set; } = "";
        public string DjUserId { get; set; } = "";
        public string DjName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class TipNotification
    {
        public string SessionId { get; set; } = "";
        public Tip Tip { get; set; } = new();
        public string DjName { get; set; } = "";
    }


    public interface ITipService
    {
        Task<PaymentIntent> CreatePaymentIntentAsync(string sessionId, TipRequest tipRequest);
        Task<Tip> RecordTipAsync(string sessionId, TipRequest tipRequest, string paymentIntentId);
        Task<bool> ConfirmTipPaymentAsync(string paymentIntentId, string stripeChargeId);
        Task<List<Tip>> GetSessionTipsAsync(string sessionId);
        Task<DjPaymentSettings?> GetDjPaymentSettingsAsync(string djUserId);
        Task<DjPaymentSettings> SaveDjPaymentSettingsAsync(DjPaymentSettings settings);
        Task<decimal> GetSessionTotalTipsAsync(string sessionId);
        Task<List<Tip>> GetDjTipsAsync(string djUserId, DateTime? startDate = null, DateTime? endDate = null);
    }

    public class TipService : ITipService
    {
        private readonly IConfiguration _configuration;
        private readonly ISongRequestCacheService _cacheService;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly string _stripeSecretKey;

        public TipService(
            IConfiguration configuration,
            ISongRequestCacheService cacheService)
        {
            _configuration = configuration;
            _cacheService = cacheService;
            _stripeSecretKey = configuration["Stripe:SecretKey"] ?? "";

            StripeConfiguration.ApiKey = _stripeSecretKey;
            _paymentIntentService = new PaymentIntentService();
        }

        public async Task<PaymentIntent> CreatePaymentIntentAsync(string sessionId, TipRequest tipRequest)
        {
            try
            {
                // Get DJ payment settings
                var djSettings = await GetDjPaymentSettingsForSessionAsync(sessionId);
                if (djSettings?.StripeAccountId == null)
                {
                    throw new InvalidOperationException("DJ has not set up payment processing");
                }

                var options = new PaymentIntentCreateOptions
                {
                    Amount = (long)(tipRequest.Amount * 100), // Convert to cents
                    Currency = "usd",
                    Metadata = new Dictionary<string, string>
                    {
                        { "session_id", sessionId },
                        { "tipper_name", tipRequest.TipperName },
                        { "tip_message", tipRequest.Message ?? "" }
                    },
                    TransferData = new PaymentIntentTransferDataOptions
                    {
                        Destination = djSettings.StripeAccountId,
                    },
                    ApplicationFeeAmount = (long)(tipRequest.Amount * 100 * (decimal)0.05) // 5% platform fee
                };

                return await _paymentIntentService.CreateAsync(options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create payment intent: {ex.Message}", ex);
            }
        }

        public async Task<Tip> RecordTipAsync(string sessionId, TipRequest tipRequest, string paymentIntentId)
        {
            var tip = new Tip
            {
                SessionId = sessionId,
                DjUserId = await GetDjUserIdForSessionAsync(sessionId),
                TipperName = tipRequest.TipperName,
                TipperEmail = tipRequest.TipperEmail,
                Amount = tipRequest.Amount,
                Message = tipRequest.Message,
                PaymentIntentId = paymentIntentId,
                Status = TipStatus.Processing
            };

            // Store in cache service (you'll need to implement this)
            await _cacheService.SaveTipAsync(tip);
            return tip;
        }

        public async Task<bool> ConfirmTipPaymentAsync(string paymentIntentId, string stripeChargeId)
        {
            try
            {
                // Update tip status in cache
                await _cacheService.UpdateTipStatusAsync(paymentIntentId, TipStatus.Completed, stripeChargeId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error confirming tip payment: {ex.Message}");
                return false;
            }
        }

        public async Task<List<Tip>> GetSessionTipsAsync(string sessionId)
        {
            return await _cacheService.GetSessionTipsAsync(sessionId);
        }

        public async Task<DjPaymentSettings?> GetDjPaymentSettingsAsync(string djUserId)
        {
            return await _cacheService.GetDjPaymentSettingsAsync(djUserId);
        }

        public async Task<DjPaymentSettings> SaveDjPaymentSettingsAsync(DjPaymentSettings settings)
        {
            settings.UpdatedAt = DateTime.UtcNow;
            return await _cacheService.SaveDjPaymentSettingsAsync(settings);
        }

        public async Task<decimal> GetSessionTotalTipsAsync(string sessionId)
        {
            var tips = await GetSessionTipsAsync(sessionId);
            return tips.Where(t => t.Status == TipStatus.Completed).Sum(t => t.Amount);
        }

        public async Task<List<Tip>> GetDjTipsAsync(string djUserId, DateTime? startDate = null, DateTime? endDate = null)
        {
            return await _cacheService.GetDjTipsAsync(djUserId, startDate, endDate);
        }

        private async Task<DjPaymentSettings?> GetDjPaymentSettingsForSessionAsync(string sessionId)
        {
            var djUserId = await GetDjUserIdForSessionAsync(sessionId);
            return await GetDjPaymentSettingsAsync(djUserId);
        }

        private async Task<string> GetDjUserIdForSessionAsync(string sessionId)
        {
            // You'll need to implement this based on your session management
            var sessionInfo = await _cacheService.GetSessionInfoAsync(sessionId);
            return sessionInfo?.DjUserId ?? "";
        }
    }
}

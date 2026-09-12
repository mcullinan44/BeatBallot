using Microsoft.JSInterop;

namespace BeatBallot.Web.Admin
{
    public interface IAdminAuthService
    {
        Task<bool> ValidatePasswordAsync(string password);
        Task<bool> IsAuthenticatedAsync();
        Task LoginAsync();
        Task LogoutAsync();
    }

    // Replace your AdminAuthService with this cookie-based version

    public class AdminAuthService : IAdminAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IJSRuntime _jsRuntime;
        private readonly string _adminPassword;
        private const string ADMIN_COOKIE_NAME = "SwayFlo_AdminAuth";
        private const int COOKIE_EXPIRY_HOURS = 24;

        public AdminAuthService(IConfiguration configuration, IJSRuntime jsRuntime)
        {
            _configuration = configuration;
            _jsRuntime = jsRuntime;
            _adminPassword = _configuration.GetValue<string>("AdminSettings:Password")
                ?? throw new InvalidOperationException("Admin password not configured");
        }

        public Task<bool> ValidatePasswordAsync(string password)
        {
            return Task.FromResult(password == _adminPassword);
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {
                var cookieValue = await _jsRuntime.InvokeAsync<string>("blazorCookies.get", ADMIN_COOKIE_NAME);

                if (string.IsNullOrEmpty(cookieValue))
                    return false;

                // Simple validation - in production you might want to use JWT or encrypted tokens
                var parts = cookieValue.Split('|');
                if (parts.Length != 2)
                    return false;

                var token = parts[0];
                if (!long.TryParse(parts[1], out var expiry))
                    return false;

                // Check if token is expired
                var expiryDate = DateTimeOffset.FromUnixTimeSeconds(expiry);
                if (DateTimeOffset.UtcNow > expiryDate)
                {
                    await LogoutAsync(); // Clear expired cookie
                    return false;
                }

                // Simple token validation (you can make this more sophisticated)
                var expectedToken = GenerateToken();
                return token == expectedToken;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task LoginAsync()
        {
            try
            {
                var token = GenerateToken();
                var expiry = DateTimeOffset.UtcNow.AddHours(COOKIE_EXPIRY_HOURS).ToUnixTimeSeconds();
                var cookieValue = $"{token}|{expiry}";

                await _jsRuntime.InvokeVoidAsync("blazorCookies.set", ADMIN_COOKIE_NAME, cookieValue, COOKIE_EXPIRY_HOURS);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to set authentication cookie", ex);
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("blazorCookies.remove", ADMIN_COOKIE_NAME);
            }
            catch (Exception)
            {
                // Ignore errors during logout
            }
        }

        private string GenerateToken()
        {
            // Simple token generation - in production use something more secure
            var data = $"admin|{_adminPassword}|{DateTime.UtcNow:yyyyMMdd}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));
        }
    }
}

namespace BeatBallot.Web.Services
{
    public interface ITokenProvider
    {
        Task<string?> GetAccessTokenAsync();
        Task<bool> IsTokenValidAsync();
    }

    public class TokenProvider : ITokenProvider
    {
        private readonly Blazored.LocalStorage.ILocalStorageService _localStorage;

        public TokenProvider(Blazored.LocalStorage.ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                var accessToken = await _localStorage.GetItemAsync<string>("sat");

                if (string.IsNullOrEmpty(accessToken))
                    return null;

                if (!await IsTokenValidAsync())
                    return null;

                return accessToken;
            }
            catch (Exception ex) 
            {
                return null;
            }
        }

        public async Task<bool> IsTokenValidAsync()
        {
            try
            {
                var expiresIn = await _localStorage.GetItemAsync<int>("satexp");
                var createdAt = await _localStorage.GetItemAsync<DateTime>("satcr");

                return DateTime.UtcNow < createdAt.AddSeconds(expiresIn);
            }
            catch
            {
                return false;
            }
        }
    }
}

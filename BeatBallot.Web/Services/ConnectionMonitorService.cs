using Microsoft.JSInterop;

namespace BeatBallot.Web.Services
{
    public interface IConnectionMonitorService
    {
        Task StartMonitoringAsync();
        event Action<bool> ConnectionStateChanged;
    }

    public class ConnectionMonitorService : IConnectionMonitorService
    {
        private readonly IJSRuntime _jsRuntime;
        private Timer? _connectionTimer;

        public event Action<bool>? ConnectionStateChanged;

        public ConnectionMonitorService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task StartMonitoringAsync()
        {
            _connectionTimer = new Timer(async _ =>
            {
                try
                {
                    // Simple ping to check connection
                    await _jsRuntime.InvokeVoidAsync("console.log", "Connection check");
                    ConnectionStateChanged?.Invoke(true);
                }
                catch
                {
                    ConnectionStateChanged?.Invoke(false);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        public void Dispose()
        {
            _connectionTimer?.Dispose();
        }
    }
}

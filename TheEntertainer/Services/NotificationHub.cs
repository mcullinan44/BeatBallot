using Microsoft.AspNetCore.SignalR;

namespace TheEntertainer.Services
{
    public class NotificationHub: Hub
    {

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine("A client connected.");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine("A client disconnected.");
            await base.OnDisconnectedAsync(exception);
        }

    }
}

using Entertainer.Models;
using Microsoft.AspNetCore.SignalR;
using Swan.Formatters;
using System;
using Newtonsoft.Json;


namespace TheEntertainer.Services
{
    public class NotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendTrackVoteNotification(VoteNotification notification, Jam jam)
        {
            string jsonString = JsonConvert.SerializeObject(notification);
            VoteTarget voteTarget = jam.VoteTargets.FirstOrDefault(i => i.ExternalTrackUri == notification.ExternalTrackUri);
            voteTarget.TotalVotes += notification.Value;
            await _hubContext.Clients.All.SendAsync("SendVoteToHost", jsonString);
            await _hubContext.Clients.All.SendAsync("SendVoteToUsers", jsonString);
        }

    }
}

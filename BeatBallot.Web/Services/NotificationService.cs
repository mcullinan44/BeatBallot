using BeatBallot.Models;
using BeatBallot.Web.Pages;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;

namespace BeatBallot.Web.Services
{
    public class NotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendTrackVoteNotification(VoteNotification notification, Session session)
        {
            string jsonString = JsonConvert.SerializeObject(notification);
            VoteTarget voteTarget = session.VoteTargets.FirstOrDefault(i => i.ExternalTrackUri == notification.ExternalTrackUri);
            voteTarget.TotalVotes += notification.Value;

            await _hubContext.Clients.All.SendAsync("SendVoteToUsers", jsonString);
        }

    }

}
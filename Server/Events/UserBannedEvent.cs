using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user is banned.
    /// </summary>
    public class UserBannedEvent : BaseEvent
    {
        public User User { get; }
        public string BannedBy { get; }

        public UserBannedEvent(User user, string bannedBy = "")
        {
            User = user;
            BannedBy = bannedBy;
        }
    }
}

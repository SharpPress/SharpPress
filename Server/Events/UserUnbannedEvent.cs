using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user is unbanned.
    /// </summary>
    public class UserUnbannedEvent : BaseEvent
    {
        public User User { get; }
        public string UnbannedBy { get; }

        public UserUnbannedEvent(User user, string unbannedBy = "")
        {
            User = user;
            UnbannedBy = unbannedBy;
        }
    }
}

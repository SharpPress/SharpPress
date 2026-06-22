using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user successfully logs in.
    /// </summary>
    public class UserLoggedInEvent : BaseEvent
    {
        public User User { get; }
        public string IpAddress { get; }

        public UserLoggedInEvent(User user, string ipAddress = "")
        {
            User = user;
            IpAddress = ipAddress;
        }
    }
}

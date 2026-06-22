using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user logs out.
    /// </summary>
    public class UserLoggedOutEvent : BaseEvent
    {
        public User User { get; }

        public UserLoggedOutEvent(User user)
        {
            User = user;
        }
    }
}

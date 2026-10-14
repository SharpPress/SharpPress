using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user profile is updated.
    /// </summary>
    public class UserProfileUpdatedEvent : BaseEvent
    {
        public User User { get; }

        public UserProfileUpdatedEvent(User user)
        {
            User = user;
        }
    }
}

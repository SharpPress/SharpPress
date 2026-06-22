using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user changes their password.
    /// </summary>
    public class PasswordChangedEvent : BaseEvent
    {
        public User User { get; }

        public PasswordChangedEvent(User user)
        {
            User = user;
        }
    }
}

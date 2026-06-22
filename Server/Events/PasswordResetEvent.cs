using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a password reset is confirmed.
    /// </summary>
    public class PasswordResetEvent : BaseEvent
    {
        public User User { get; }

        public PasswordResetEvent(User user)
        {
            User = user;
        }
    }
}

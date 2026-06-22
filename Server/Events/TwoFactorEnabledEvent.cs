using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when two-factor authentication is enabled for a user.
    /// </summary>
    public class TwoFactorEnabledEvent : BaseEvent
    {
        public User User { get; }

        public TwoFactorEnabledEvent(User user)
        {
            User = user;
        }
    }
}

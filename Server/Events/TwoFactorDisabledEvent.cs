using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when two-factor authentication is disabled for a user.
    /// </summary>
    public class TwoFactorDisabledEvent : BaseEvent
    {
        public User User { get; }
        public string DisabledBy { get; }

        public TwoFactorDisabledEvent(User user, string disabledBy = "")
        {
            User = user;
            DisabledBy = disabledBy;
        }
    }
}

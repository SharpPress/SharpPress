using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user account is unlocked.
    /// </summary>
    public class UserUnlockedEvent : BaseEvent
    {
        public User User { get; }
        public string UnlockedBy { get; }

        public UserUnlockedEvent(User user, string unlockedBy = "")
        {
            User = user;
            UnlockedBy = unlockedBy;
        }
    }
}

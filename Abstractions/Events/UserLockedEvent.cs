using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user account is locked.
    /// </summary>
    public class UserLockedEvent : BaseEvent
    {
        public User User { get; }
        public int LockDurationMinutes { get; }
        public string LockedBy { get; }

        public UserLockedEvent(User user, int lockDurationMinutes = 0, string lockedBy = "")
        {
            User = user;
            LockDurationMinutes = lockDurationMinutes;
            LockedBy = lockedBy;
        }
    }
}

using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user is deleted.
    /// </summary>
    public class UserDeletedEvent : BaseEvent
    {
        public User User { get; }
        public string DeletedBy { get; }

        public UserDeletedEvent(User user, string deletedBy = "")
        {
            User = user;
            DeletedBy = deletedBy;
        }
    }
}

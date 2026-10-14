using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a user's role is changed.
    /// </summary>
    public class UserRoleChangedEvent : BaseEvent
    {
        public User User { get; }
        public UserRole OldRole { get; }
        public UserRole NewRole { get; }
        public string ChangedBy { get; }

        public UserRoleChangedEvent(User user, UserRole oldRole, UserRole newRole, string changedBy = "")
        {
            User = user;
            OldRole = oldRole;
            NewRole = newRole;
            ChangedBy = changedBy;
        }
    }
}

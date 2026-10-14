using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when site settings are updated.
    /// </summary>
    public class SettingsUpdatedEvent : BaseEvent
    {
        public SiteSettings Settings { get; }
        public string UpdatedBy { get; }

        public SettingsUpdatedEvent(SiteSettings settings, string updatedBy = "")
        {
            Settings = settings;
            UpdatedBy = updatedBy;
        }
    }
}

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a failed login attempt occurs.
    /// </summary>
    public class FailedLoginAttemptEvent : BaseEvent
    {
        public string Username { get; }
        public string IpAddress { get; }

        public FailedLoginAttemptEvent(string username, string ipAddress = "")
        {
            Username = username;
            IpAddress = ipAddress;
        }
    }
}

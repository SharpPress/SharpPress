namespace SharpPress.Events
{
    /// <summary>
    /// Event published when an account is automatically locked out due to excessive failed login attempts.
    /// </summary>
    public class AccountLockedOutEvent : BaseEvent
    {
        public string Username { get; }
        public int FailedAttempts { get; }
        public int LockDurationMinutes { get; }

        public AccountLockedOutEvent(string username, int failedAttempts, int lockDurationMinutes)
        {
            Username = username;
            FailedAttempts = failedAttempts;
            LockDurationMinutes = lockDurationMinutes;
        }
    }
}

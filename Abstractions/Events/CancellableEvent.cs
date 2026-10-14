namespace SharpPress.Events
{
    public abstract class CancellableEvent : BaseEvent
    {
        public bool IsCancelled { get; private set; }
        public string? CancelReason { get; private set; }

        public void Cancel(string? reason = null)
        {
            IsCancelled = true;
            CancelReason = reason;
        }
    }
}

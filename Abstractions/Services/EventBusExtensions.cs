using SharpPress.Events;

namespace SharpPress.Services
{
    public class EventCancelledException : InvalidOperationException
    {
        public EventCancelledException(string message) : base(message) { }
    }

    public static class EventBusExtensions
    {
        public static async Task PublishOrThrowAsync<T>(this IEventBus bus, T eventData) where T : CancellableEvent
        {
            if (!await bus.PublishCancellableAsync(eventData))
                throw new EventCancelledException(eventData.CancelReason ?? "The action was cancelled by a plugin.");
        }
    }
}

using SharpPress.Events;

namespace SharpPress.Services
{
    public interface IEvent
    {
        Guid Id { get; }
        DateTime Timestamp { get; }
    }

    public interface IEventHandler<in T> where T : IEvent
    {
        Task HandleAsync(T eventData);
    }

    public interface IEventBus
    {
        Task PublishAsync<T>(T eventData) where T : IEvent;
        Task<bool> PublishCancellableAsync<T>(T eventData) where T : CancellableEvent;
        IDisposable Subscribe<T>(IEventHandler<T> handler, int priority = 0) where T : IEvent;
        IDisposable Subscribe<T>(Func<T, Task> handler, int priority = 0) where T : IEvent;
    }
}

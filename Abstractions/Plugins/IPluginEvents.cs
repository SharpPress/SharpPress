using SharpPress.Events;
using SharpPress.Services;

namespace SharpPress.Plugins
{
    public interface IPluginEvents
    {
        void On<T>(Func<T, Task> handler, int priority = 0) where T : IEvent;
        void On<T>(Action<T> handler, int priority = 0) where T : IEvent;
        Task PublishAsync<T>(T eventData) where T : IEvent;
        Task<bool> PublishCancellableAsync<T>(T eventData) where T : CancellableEvent;
    }
}

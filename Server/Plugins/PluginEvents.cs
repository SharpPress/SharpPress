using SharpPress.Events;
using SharpPress.Services;

namespace SharpPress.Plugins
{
    public sealed class PluginEvents : IPluginEvents
    {
        private readonly IEventBus _bus;
        private readonly PluginScope _scope;

        public PluginEvents(IEventBus bus, PluginScope scope)
        {
            _bus = bus;
            _scope = scope;
        }

        public void On<T>(Func<T, Task> handler, int priority = 0) where T : IEvent
        {
            var subscription = _bus.Subscribe(handler, priority);
            _scope.OnDispose(subscription.Dispose);
        }

        public void On<T>(Action<T> handler, int priority = 0) where T : IEvent
        {
            On<T>(e =>
            {
                handler(e);
                return Task.CompletedTask;
            }, priority);
        }

        public Task PublishAsync<T>(T eventData) where T : IEvent => _bus.PublishAsync(eventData);

        public Task<bool> PublishCancellableAsync<T>(T eventData) where T : CancellableEvent => _bus.PublishCancellableAsync(eventData);
    }
}

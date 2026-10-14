using SharpPress.Events;
using System.Collections.Concurrent;

namespace SharpPress.Services
{
    public class InMemoryEventBus : IEventBus
    {
        private readonly object _lock = new();
        private readonly List<Subscription> _subscriptions = new();
        private readonly ConcurrentDictionary<Type, Subscription[]> _resolved = new();
        private readonly Logger _logger;

        public InMemoryEventBus(Logger logger)
        {
            _logger = logger;
        }

        public IDisposable Subscribe<T>(IEventHandler<T> handler, int priority = 0) where T : IEvent
            => Add(typeof(T), priority, handler, async e => await handler.HandleAsync((T)e));

        public IDisposable Subscribe<T>(Func<T, Task> handler, int priority = 0) where T : IEvent
            => Add(typeof(T), priority, handler, async e => await handler((T)e));

        public async Task PublishAsync<T>(T eventData) where T : IEvent
        {
            if (eventData == null) return;

            var subscriptions = Resolve(eventData.GetType());
            if (subscriptions.Length == 0) return;

            var tasks = subscriptions.Select(subscription => Invoke(subscription, eventData));
            await Task.WhenAll(tasks);
        }

        public async Task<bool> PublishCancellableAsync<T>(T eventData) where T : CancellableEvent
        {
            if (eventData == null) return true;

            foreach (var subscription in Resolve(eventData.GetType()))
            {
                await Invoke(subscription, eventData);
                if (eventData.IsCancelled) return false;
            }

            return !eventData.IsCancelled;
        }

        private async Task Invoke(Subscription subscription, object eventData)
        {
            try
            {
                await subscription.Handler(eventData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ EventBus: Handler for event '{eventData.GetType().Name}' failed: {ex.Message}");
            }
        }

        private Subscription[] Resolve(Type eventType)
        {
            return _resolved.GetOrAdd(eventType, type =>
            {
                lock (_lock)
                {
                    return _subscriptions
                        .Where(s => s.EventType.IsAssignableFrom(type))
                        .OrderBy(s => s.Priority)
                        .ToArray();
                }
            });
        }

        private IDisposable Add(Type eventType, int priority, object key, Func<object, Task> handler)
        {
            lock (_lock)
            {
                var existing = _subscriptions.FirstOrDefault(s => s.EventType == eventType && ReferenceEquals(s.Key, key));
                if (existing != null) return existing;

                var subscription = new Subscription(this, eventType, priority, key, handler);
                _subscriptions.Add(subscription);
                _resolved.Clear();
                return subscription;
            }
        }

        private void Remove(Subscription subscription)
        {
            lock (_lock)
            {
                if (_subscriptions.Remove(subscription))
                    _resolved.Clear();
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly InMemoryEventBus _owner;

            public Type EventType { get; }
            public int Priority { get; }
            public object Key { get; }
            public Func<object, Task> Handler { get; }

            public Subscription(InMemoryEventBus owner, Type eventType, int priority, object key, Func<object, Task> handler)
            {
                _owner = owner;
                EventType = eventType;
                Priority = priority;
                Key = key;
                Handler = handler;
            }

            public void Dispose() => _owner.Remove(this);
        }
    }
}

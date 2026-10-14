using System.Collections.Concurrent;
using System.Reflection;

namespace SharpPress.Services
{
    public interface IServiceSlot
    {
        void RemoveOwner(string owner);
    }

    public sealed class ServiceSlot<T> : IServiceSlot where T : class
    {
        private readonly object _lock = new();
        private readonly List<Layer> _layers = new();
        private readonly T _default;
        private volatile T _current;

        public ServiceSlot(T defaultImplementation)
        {
            _default = defaultImplementation;
            _current = defaultImplementation;
        }

        public T Current => _current;

        public void Replace(string owner, T implementation)
        {
            lock (_lock)
            {
                _layers.Add(new Layer(owner, implementation, null));
                Rebuild();
            }
        }

        public void Decorate(string owner, Func<T, T> decorator)
        {
            lock (_lock)
            {
                _layers.Add(new Layer(owner, null, decorator));
                Rebuild();
            }
        }

        public void RemoveOwner(string owner)
        {
            lock (_lock)
            {
                if (_layers.RemoveAll(l => l.Owner == owner) > 0) Rebuild();
            }
        }

        private void Rebuild()
        {
            var current = _default;
            foreach (var layer in _layers)
                current = layer.Implementation ?? layer.Decorator!(current);
            _current = current;
        }

        private sealed record Layer(string Owner, T? Implementation, Func<T, T>? Decorator);
    }

    public class ServiceProxy<T> : DispatchProxy where T : class
    {
        public ServiceSlot<T> Slot { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                return targetMethod!.Invoke(Slot.Current, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    public sealed class ServiceOverrideRegistry
    {
        private readonly ConcurrentDictionary<Type, IServiceSlot> _slots = new();
        private readonly IServiceProvider _serviceProvider;

        public ServiceOverrideRegistry(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public T CreateProxy<T>(T defaultImplementation) where T : class
        {
            var slot = new ServiceSlot<T>(defaultImplementation);
            _slots[typeof(T)] = slot;
            var proxy = DispatchProxy.Create<T, ServiceProxy<T>>();
            ((ServiceProxy<T>)(object)proxy).Slot = slot;
            return proxy;
        }

        public void Replace<T>(string owner, T implementation) where T : class
            => GetSlot<T>().Replace(owner, implementation);

        public void Decorate<T>(string owner, Func<T, T> decorator) where T : class
            => GetSlot<T>().Decorate(owner, decorator);

        public void RemoveOwner(string owner)
        {
            foreach (var slot in _slots.Values) slot.RemoveOwner(owner);
        }

        public bool IsOverridable(Type type) => _slots.ContainsKey(type);

        private ServiceSlot<T> GetSlot<T>() where T : class
        {
            if (_slots.TryGetValue(typeof(T), out var slot) && slot is ServiceSlot<T> typed) return typed;

            if (typeof(T).IsInterface)
            {
                _serviceProvider.GetService<T>();
                if (_slots.TryGetValue(typeof(T), out slot) && slot is ServiceSlot<T> created) return created;
            }

            throw new InvalidOperationException($"{typeof(T).Name} is not an overridable service.");
        }
    }

    public sealed class PluginServices : SharpPress.Plugins.IPluginServices
    {
        private readonly ServiceOverrideRegistry _registry;
        private readonly string _owner;

        public PluginServices(ServiceOverrideRegistry registry, string owner)
        {
            _registry = registry;
            _owner = owner;
        }

        public void Replace<T>(T implementation) where T : class => _registry.Replace(_owner, implementation);

        public void Decorate<T>(Func<T, T> decorator) where T : class => _registry.Decorate(_owner, decorator);
    }

    public static class OverridableServiceExtensions
    {
        public static IServiceCollection AddOverridable<TService, TImpl>(this IServiceCollection services, Func<IServiceProvider, TImpl>? factory = null)
            where TService : class
            where TImpl : class, TService
        {
            if (factory == null) services.AddSingleton<TImpl>();
            else services.AddSingleton(factory);
            services.AddSingleton<TService>(sp => sp.GetRequiredService<ServiceOverrideRegistry>().CreateProxy<TService>(sp.GetRequiredService<TImpl>()));
            return services;
        }
    }
}

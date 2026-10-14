namespace SharpPress.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly object _lock = new();
        private readonly List<DashboardWidget> _widgets = new();

        public IReadOnlyList<DashboardWidget> Widgets
        {
            get
            {
                lock (_lock)
                    return _widgets.OrderBy(w => w.Order).ToList();
            }
        }

        public IDisposable Register(DashboardWidget widget)
        {
            lock (_lock)
                _widgets.Add(widget);

            return new Removal(() =>
            {
                lock (_lock)
                    _widgets.Remove(widget);
            });
        }

        private sealed class Removal : IDisposable
        {
            private Action? _action;

            public Removal(Action action)
            {
                _action = action;
            }

            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}

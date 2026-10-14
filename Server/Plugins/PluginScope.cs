namespace SharpPress.Plugins
{
    public sealed class PluginScope : IDisposable
    {
        private readonly object _lock = new();
        private readonly Stack<Action> _cleanups = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Action<Exception>? _onError;
        private bool _disposed;

        public PluginScope(Action<Exception>? onError = null)
        {
            _onError = onError;
        }

        public CancellationToken Token => _cts.Token;

        public void OnDispose(Action cleanup)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _cleanups.Push(cleanup);
                    return;
                }
            }

            cleanup();
        }

        public void Dispose()
        {
            List<Action> actions;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                actions = new List<Action>(_cleanups);
                _cleanups.Clear();
            }

            try { _cts.Cancel(); }
            catch (Exception ex) { _onError?.Invoke(ex); }

            foreach (var action in actions)
            {
                try { action(); }
                catch (Exception ex) { _onError?.Invoke(ex); }
            }

            _cts.Dispose();
        }
    }
}

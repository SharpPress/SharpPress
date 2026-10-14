using Newtonsoft.Json;
using SharpPress.Plugins;

namespace SharpPress.Services
{
    public class SchedulerService : IScheduler, IDisposable
    {
        private readonly object _lock = new();
        private readonly Logger _logger;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly string _statePath = Path.Combine(PluginPaths.Root, "scheduler-state.json");
        private Dictionary<string, DateTime> _lastRuns = new();

        public SchedulerService(Logger logger)
        {
            _logger = logger;
            LoadState();
        }

        public IDisposable Every(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately = false)
        {
            if (interval < TimeSpan.FromSeconds(1))
                throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be at least one second.");

            return Start(name, job, () =>
            {
                if (runImmediately && !HasRun(name)) return TimeSpan.Zero;

                var last = LastRun(name);
                if (last == null) return interval;

                var remaining = last.Value + interval - DateTime.UtcNow;
                return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }, _ => interval);
        }

        public IDisposable DailyAt(string name, TimeOnly utcTime, Func<CancellationToken, Task> job)
        {
            TimeSpan UntilNext()
            {
                var now = DateTime.UtcNow;
                var next = now.Date + utcTime.ToTimeSpan();
                if (next <= now) next = next.AddDays(1);
                return next - now;
            }

            return Start(name, job, UntilNext, _ => UntilNext());
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        private IDisposable Start(string name, Func<CancellationToken, Task> job, Func<TimeSpan> firstDelay, Func<DateTime, TimeSpan> nextDelay)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var delay = firstDelay();

                    while (!token.IsCancellationRequested)
                    {
                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay, token);

                        try
                        {
                            await job(token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Scheduled job '{name}' failed: {ex.Message}");
                        }

                        var finished = DateTime.UtcNow;
                        RecordRun(name, finished);
                        delay = nextDelay(finished);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);

            return new Cancellation(cts);
        }

        private bool HasRun(string name)
        {
            lock (_lock)
                return _lastRuns.ContainsKey(name);
        }

        private DateTime? LastRun(string name)
        {
            lock (_lock)
                return _lastRuns.TryGetValue(name, out var value) ? value : null;
        }

        private void RecordRun(string name, DateTime when)
        {
            lock (_lock)
            {
                _lastRuns[name] = when;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                    File.WriteAllText(_statePath, JsonConvert.SerializeObject(_lastRuns, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Scheduler state could not be saved: {ex.Message}");
                }
            }
        }

        private void LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                    _lastRuns = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(_statePath)) ?? new();
            }
            catch
            {
                _lastRuns = new();
            }
        }

        private sealed class Cancellation : IDisposable
        {
            private CancellationTokenSource? _cts;

            public Cancellation(CancellationTokenSource cts)
            {
                _cts = cts;
            }

            public void Dispose()
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                if (cts == null) return;

                try { cts.Cancel(); }
                finally { cts.Dispose(); }
            }
        }
    }
}

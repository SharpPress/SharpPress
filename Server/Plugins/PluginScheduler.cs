using SharpPress.Services;

namespace SharpPress.Plugins
{
    public sealed class PluginScheduler : IPluginScheduler
    {
        private readonly IScheduler _scheduler;
        private readonly PluginScope _scope;
        private readonly string _plugin;

        public PluginScheduler(IScheduler scheduler, PluginScope scope, string plugin)
        {
            _scheduler = scheduler;
            _scope = scope;
            _plugin = plugin;
        }

        public void Every(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately = false)
        {
            var registration = _scheduler.Every($"{_plugin}:{name}", interval, job, runImmediately);
            _scope.OnDispose(registration.Dispose);
        }

        public void DailyAt(string name, TimeOnly utcTime, Func<CancellationToken, Task> job)
        {
            var registration = _scheduler.DailyAt($"{_plugin}:{name}", utcTime, job);
            _scope.OnDispose(registration.Dispose);
        }
    }
}

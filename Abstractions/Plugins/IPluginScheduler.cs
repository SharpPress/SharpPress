namespace SharpPress.Plugins
{
    public interface IPluginScheduler
    {
        void Every(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately = false);
        void DailyAt(string name, TimeOnly utcTime, Func<CancellationToken, Task> job);
    }
}

namespace SharpPress.Services
{
    public interface IScheduler
    {
        IDisposable Every(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately = false);
        IDisposable DailyAt(string name, TimeOnly utcTime, Func<CancellationToken, Task> job);
    }
}

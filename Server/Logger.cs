using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPress.Services
{
    public class Logger : IDisposable
    {
        private readonly string _logsFolder;
        private readonly string _logFile;
        private readonly object _logLock = new();

        private static readonly object _prepareLock = new object();

        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly TimeSpan _flushInterval;
        private readonly int _maxBatchSize;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _flushTask;
        private volatile bool _disposed;

        public Logger(string logsFolder = "logs", TimeSpan? flushInterval = null, int maxBatchSize = 50)
        {
            _logsFolder = logsFolder;
            _logFile = Path.Combine(_logsFolder, "latest.log");
            _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
            _maxBatchSize = maxBatchSize;

            _flushTask = Task.Run(FlushLoopAsync);
        }

        public void PrepareLogs()
        {
            lock (_prepareLock)
            {
                try
                {
                    if (!Directory.Exists(_logsFolder)) Directory.CreateDirectory(_logsFolder);

                    if (File.Exists(_logFile))
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string zipFile = Path.Combine(_logsFolder, $"latest_{timestamp}.zip");

                        using (var archive = ZipFile.Open(zipFile, ZipArchiveMode.Create))
                        {
                            archive.CreateEntryFromFile(_logFile, "latest.log");
                        }

                        File.Delete(_logFile);

                        File.WriteAllText(_logFile, string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error preparing logs: {ex.Message}");
                }
            }
        }

        public void Log(string message) => WriteLog("INFO", message);
        public void LogError(string message) => WriteLog("ERROR", message);
        public void LogWarning(string message) => WriteLog("WARNING", message);
        public void LogSecurity(string message) => WriteLog("SECURITY", message);

        private void WriteLog(string level, string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

            Console.WriteLine(logEntry);

            _logQueue.Enqueue(logEntry);
        }

        private async Task FlushLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushInterval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    FlushToDisk();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Logger] Flush error: {ex.Message}");
                }
            }
        }

        private void FlushToDisk()
        {
            if (_logQueue.IsEmpty) return;

            if (!Directory.Exists(_logsFolder))
                Directory.CreateDirectory(_logsFolder);

            var batch = new System.Text.StringBuilder();

            for (int i = 0; i < _maxBatchSize && _logQueue.TryDequeue(out var entry); i++)
            {
                batch.AppendLine(entry);
            }

            if (batch.Length > 0)
            {
                lock (_logLock)
                {
                    File.AppendAllText(_logFile, batch.ToString());
                }
            }
        }

        public void Flush()
        {
            FlushToDisk();
        }

        public async Task FlushAsync()
        {
            _cts.Cancel();

            try
            {
                await _flushTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {

            }

            FlushToDisk();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
                _flushTask.Wait(TimeSpan.FromSeconds(10));
            }
            catch (AggregateException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            FlushToDisk();

            _cts.Dispose();
        }
    }

    internal class LoggerAdapter : ILogger<FeatherDatabase>
    {
        private readonly Logger _logger;
        public LoggerAdapter(Logger logger) => _logger = logger;
        public IDisposable BeginScope<TState>(TState state) => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Error:
                case LogLevel.Critical:
                    _logger.LogError(message);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning(message);
                    break;
                default:
                    _logger.Log(message);
                    break;
            }
        }
        private class NullDisposable : IDisposable
        {
            public void Dispose() { }
            public static readonly NullDisposable Instance = new();
        }
    }
}

using SharpPress.Models;
using SharpPress.Plugins;
using SharpPress.Servers;
using SharpPress.Services;
using System.Collections.Concurrent;

namespace SharpPress.Helpers
{
    public class DownloadJobProcessor
    {
        private readonly ConcurrentQueue<MarketPlugin> _jobQueue = new ConcurrentQueue<MarketPlugin>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly Logger _logger;
        private readonly PluginManager _pluginManager;
        private readonly PackageManager _packageManager;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private Task _processingTask;

        public DownloadJobProcessor(Logger logger, PluginManager pluginManager, PackageManager packageManager)
        {
            _logger = logger;
            _pluginManager = pluginManager;
            _packageManager = packageManager;
            _processingTask = Task.Run(ProcessJobsAsync);
        }

        public void EnqueueJob(MarketPlugin downloadRequest)
        {
            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _jobQueue.Enqueue(downloadRequest);
                _signal.Release();
            }
        }

        private async Task ProcessJobsAsync()
        {
            try
            {
                while (true)
                {
                    await _signal.WaitAsync(_cancellationTokenSource.Token);

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (_jobQueue.TryDequeue(out var downloadRequest))
                    {
                        try
                        {
                            await ProcessDownloadJobAsync(downloadRequest);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error processing market download job: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessDownloadJobAsync(MarketPlugin downloadRequest)
        {
            if (!PluginPaths.IsSafeName(downloadRequest.Name))
            {
                _logger.LogError($"Rejected market download with unsafe plugin name: '{downloadRequest.Name}'");
                return;
            }

            using var client = new HttpClient();
            var downloadResponse = await client.GetAsync(downloadRequest.DownloadLink, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);

            if (!downloadResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to download file from URL: {downloadRequest.DownloadLink}. Status: {downloadResponse.StatusCode}");
                return;
            }

            var content = await downloadResponse.Content.ReadAsByteArrayAsync(_cancellationTokenSource.Token);

            PluginLoadResult result;
            if (downloadRequest.isPackaged)
            {
                var package = await _packageManager.GetPackageFromByteArray(content);
                if (package == null)
                {
                    _logger.LogError($"Can't Install Packaged Plugin: '{downloadRequest.Name}' From '{downloadRequest.DownloadLink}'");
                    return;
                }

                result = await _packageManager.InstallPackage(package, "marketplace");
            }
            else
            {
                result = await _pluginManager.InstallPluginAsync($"{downloadRequest.Name}.dll", content, "marketplace");
            }

            if (result.Success)
                _logger.Log($"Successfully installed plugin '{downloadRequest.Name}' from the marketplace.");
            else
                _logger.LogError($"Marketplace plugin '{downloadRequest.Name}' could not be installed: {result.Message}");
        }

        public async Task StopAsync()
        {
            _cancellationTokenSource.Cancel();
            await _processingTask;
        }
    }
}

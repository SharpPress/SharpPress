using SharpPress.Services;

namespace SamplePlugin
{
    public class CountingEmailService : IEmailService
    {
        private readonly IEmailService _inner;

        public CountingEmailService(IEmailService inner)
        {
            _inner = inner;
        }

        public Func<string, string, string, bool, Task> EmailSendAction
        {
            get
            {
                Interlocked.Increment(ref SampleState.EmailAccesses);
                return _inner.EmailSendAction;
            }
        }

        public void ReplaceEmailSendAction(Func<string, string, string, bool, Task> action) => _inner.ReplaceEmailSendAction(action);

        public Task SendAsync(string to, string subject, string body, bool isHtml = false)
        {
            SampleState.Context?.Logger.Log($"SamplePlugin: sending e-mail '{subject}' to {to}");
            return _inner.SendAsync(to, subject, body, isHtml);
        }
    }

    public class CountingCacheService : ICacheService
    {
        private readonly ICacheService _inner;

        public CountingCacheService(ICacheService inner)
        {
            _inner = inner;
        }

        public T Get<T>(string key)
        {
            Interlocked.Increment(ref SampleState.CacheReads);
            return _inner.Get<T>(key);
        }

        public void Set<T>(string key, T value, TimeSpan? expiry = null) => _inner.Set(key, value, expiry);

        public bool Remove(string key) => _inner.Remove(key);

        public bool Exists(string key) => _inner.Exists(key);
    }
}

namespace SharpPress.Services
{
    public interface IPluginLogger
    {
        void Log(string message);
        void LogError(string message);
        void LogWarning(string message);
        void LogSecurity(string message);
    }
}

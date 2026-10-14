namespace SharpPress.Plugins
{
    public interface IPluginSettings
    {
        T Get<T>() where T : class, new();
        void Save<T>(T value) where T : class;
        void Expose<T>(string title) where T : class, new();
    }
}
